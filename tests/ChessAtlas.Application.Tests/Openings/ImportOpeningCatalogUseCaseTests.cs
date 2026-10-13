using System.Runtime.CompilerServices;
using ChessAtlas.Application.Common;
using ChessAtlas.Application.Openings;
using ChessAtlas.Domain.Openings;
using ChessAtlas.Domain.Provenance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessAtlas.Application.Tests.Openings;

public sealed class ImportOpeningCatalogUseCaseTests
{
    private static DataProvenance TestProvenance => DataProvenance.Create(
        provider: "test/catalogue",
        uri: "https://example.invalid/catalogue",
        license: "CC0-1.0",
        redistributionAllowed: true,
        versionOrCommit: "abc123");

    private static (ImportOpeningCatalogUseCase UseCase, FakeImporter Importer) Build(
        params OpeningCatalogRow[] rows)
    {
        var importer = new FakeImporter();
        var useCase = new ImportOpeningCatalogUseCase(
            new FakeSource(rows), importer, NullLogger<ImportOpeningCatalogUseCase>.Instance);
        return (useCase, importer);
    }

    [Fact]
    public async Task Valid_rows_are_imported_and_counted()
    {
        var (useCase, importer) = Build(
            new OpeningCatalogRow("B20", "Sicilian Defense", "1. e4 c5"),
            new OpeningCatalogRow("B20", "Sicilian Defense: Najdorf Variation", "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6"));

        var summary = await useCase.ExecuteAsync();

        Assert.Equal(2, summary.RowsRead);
        Assert.Equal(2, summary.RowsImported);
        Assert.Equal(0, summary.RowsRejected);
        Assert.Equal(2, importer.SavedItems.Count);
        Assert.Single(importer.RegisteredProvenances);
    }

    [Fact]
    public async Task A_row_with_an_illegal_line_is_rejected_and_sampled_not_fatal()
    {
        var (useCase, _) = Build(
            new OpeningCatalogRow("B20", "Sicilian Defense", "1. e4 c5"),
            new OpeningCatalogRow("B20", "Impossible Line", "1. e4 e5 2. Qh7"),
            new OpeningCatalogRow("B21", "Another Good Line", "1. e4 c5 2. Nf3"));

        var summary = await useCase.ExecuteAsync();

        Assert.Equal(3, summary.RowsRead);
        Assert.Equal(2, summary.RowsImported);
        Assert.Equal(1, summary.RowsRejected);
        Assert.Contains(summary.RejectionSamples, r => r.Contains("Impossible Line", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rows_without_a_name_or_moves_are_rejected()
    {
        var (useCase, _) = Build(
            new OpeningCatalogRow("B20", "   ", "1. e4 c5"),
            new OpeningCatalogRow("B20", "No Moves", "   "));

        var summary = await useCase.ExecuteAsync();

        Assert.Equal(0, summary.RowsImported);
        Assert.Equal(2, summary.RowsRejected);
    }

    [Fact]
    public async Task A_bad_eco_code_is_rejected_rather_than_guessed()
    {
        var (useCase, _) = Build(new OpeningCatalogRow("Z99", "Sicilian Defense", "1. e4 c5"));

        var summary = await useCase.ExecuteAsync();

        Assert.Equal(1, summary.RowsRejected);
        Assert.Contains(summary.RejectionSamples, r => r.Contains("Z99", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_rows_within_one_run_are_counted_once()
    {
        var (useCase, importer) = Build(
            new OpeningCatalogRow("B20", "Sicilian Defense", "1. e4 c5"),
            new OpeningCatalogRow("B20", "Sicilian Defense", "1. e4 c5"));

        var summary = await useCase.ExecuteAsync();

        Assert.Equal(2, summary.RowsRead);
        Assert.Equal(1, summary.RowsImported);
        Assert.Equal(1, summary.RowsSkippedAsDuplicates);
        Assert.Single(importer.SavedItems);
    }

    [Fact]
    public async Task Batches_are_flushed_at_the_configured_size()
    {
        var rows = Enumerable.Range(0, 25)
            .Select(i => new OpeningCatalogRow("B20", $"Sicilian Defense: Line {i}", $"1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 {5 + i}. a3"))
            .ToArray();

        var (useCase, importer) = Build(rows);

        var summary = await useCase.ExecuteAsync(new ImportOpeningCatalogOptions { BatchSize = 10 });

        Assert.Equal(25, summary.RowsImported);
        Assert.Equal(new[] { 10, 10, 5 }, importer.BatchSizes);
    }

    [Fact]
    public async Task MaxRows_caps_the_import_for_previews_and_tests()
    {
        var rows = Enumerable.Range(0, 50)
            .Select(i => new OpeningCatalogRow("B20", $"Line {i}", "1. e4 c5"))
            .ToArray();

        var (useCase, _) = Build(rows);

        var summary = await useCase.ExecuteAsync(new ImportOpeningCatalogOptions { MaxRows = 7 });

        Assert.Equal(7, summary.RowsRead);
    }

    [Fact]
    public async Task Progress_is_reported_while_importing()
    {
        var rows = Enumerable.Range(0, 12)
            .Select(i => new OpeningCatalogRow("B20", $"Line {i}", "1. e4 c5"))
            .ToArray();

        var (useCase, _) = Build(rows);
        var recorder = new RecordingProgress();

        await useCase.ExecuteAsync(new ImportOpeningCatalogOptions { BatchSize = 4 }, recorder);

        // A direct IProgress<T> rather than Progress<T>, which would hop threads and race the assert.
        Assert.NotEmpty(recorder.Reports);
        Assert.Contains(recorder.Reports, r => r.Stage == "Importing openings");
        Assert.Contains(recorder.Reports, r => r.Completed == 12);
    }

    [Fact]
    public async Task Cancellation_stops_the_import()
    {
        var rows = Enumerable.Range(0, 100)
            .Select(i => new OpeningCatalogRow("B20", $"Line {i}", "1. e4 c5"))
            .ToArray();

        var (useCase, _) = Build(rows);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Replacing_first_deletes_the_previous_snapshot()
    {
        var (useCase, importer) = Build(new OpeningCatalogRow("B20", "Sicilian Defense", "1. e4 c5"));

        await useCase.ExecuteAsync(new ImportOpeningCatalogOptions { ReplaceExistingFromSameProvider = true });

        Assert.Equal(1, importer.DeleteCalls);
    }

    [Fact]
    public async Task Rejections_are_sampled_not_unbounded()
    {
        var rows = Enumerable.Range(0, 200)
            .Select(i => new OpeningCatalogRow("Z99", $"Bad {i}", "1. e4 c5"))
            .ToArray();

        var (useCase, _) = Build(rows);

        var summary = await useCase.ExecuteAsync();

        Assert.Equal(200, summary.RowsRejected);
        Assert.Equal(50, summary.RejectionSamples.Count);
    }

    // ---- classification -----------------------------------------------------------------------

    [Fact]
    public async Task A_gambit_named_by_the_source_keeps_that_origin()
    {
        var (useCase, importer) = Build(
            new OpeningCatalogRow("A00", "Amar Opening: Paris Gambit", "1. Nh3 d5 2. g3 e5 3. f4"));

        await useCase.ExecuteAsync();

        var item = Assert.Single(importer.SavedItems);
        Assert.Equal(OpeningLineKind.Gambit, item.Kind);
        Assert.Equal(ClassificationOrigin.UpstreamName, item.KindOrigin);
        Assert.Equal(VerificationStatus.ReferenceVerified, item.VerificationStatus);
    }

    [Fact]
    public async Task A_trap_named_by_the_source_is_stored_unverified_with_an_explanatory_note()
    {
        // The catalogue says "trap"; it does not say why. Chess Atlas must not adopt the claim as
        // verified theory, so the row lands as unverified with a note saying exactly that.
        var (useCase, importer) = Build(
            new OpeningCatalogRow("C71", "Ruy Lopez: Noah's Ark Trap", "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6"));

        await useCase.ExecuteAsync();

        var item = Assert.Single(importer.SavedItems);
        Assert.Equal(OpeningLineKind.Trap, item.Kind);
        Assert.Equal(ClassificationOrigin.UpstreamName, item.KindOrigin);
        Assert.Equal(VerificationStatus.Unverified, item.VerificationStatus);
        Assert.False(string.IsNullOrWhiteSpace(item.Notes));
    }

    [Fact]
    public async Task A_single_move_top_level_name_is_the_main_line_and_deeper_names_are_variations()
    {
        var (useCase, importer) = Build(
            new OpeningCatalogRow("B00", "Nimzowitsch Defense", "1. e4 Nc6"),
            new OpeningCatalogRow("B00", "Nimzowitsch Defense: Kennedy Variation", "1. e4 Nc6 2. d4 e5"));

        await useCase.ExecuteAsync();

        Assert.Equal(OpeningLineKind.MainLine, importer.SavedItems[0].Kind);
        Assert.Equal(OpeningLineKind.SubVariation, importer.SavedItems[1].Kind);
    }

    [Fact]
    public async Task The_canonical_name_is_normalised_from_the_source_path()
    {
        var (useCase, importer) = Build(
            new OpeningCatalogRow("B20", "  Sicilian Defense :  Najdorf Variation ", "1. e4 c5 2. Nf3 d6"));

        await useCase.ExecuteAsync();

        Assert.Equal("Sicilian Defense: Najdorf Variation", Assert.Single(importer.SavedItems).CanonicalName);
    }

    [Fact]
    public async Task Options_reject_a_batch_size_below_one() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Build().UseCase.ExecuteAsync(new ImportOpeningCatalogOptions { BatchSize = 0 }));

    // ---- doubles ------------------------------------------------------------------------------

    private sealed class FakeSource(OpeningCatalogRow[] rows) : IOpeningCatalogSource
    {
        public string Provider => "test/catalogue";
        public DataProvenance Provenance => TestProvenance;

        public async IAsyncEnumerable<OpeningCatalogRow> ReadRowsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
        }
    }

    private sealed class RecordingProgress : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = [];

        public void Report(OperationProgress value) => Reports.Add(value);
    }

    private sealed class FakeImporter : IOpeningCatalogImporter
    {
        public List<OpeningImportItem> SavedItems { get; } = [];
        public List<int> BatchSizes { get; } = [];
        public List<DataProvenance> RegisteredProvenances { get; } = [];
        public int DeleteCalls { get; private set; }

        public Task<OpeningCatalogState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OpeningCatalogState(0, 0, 0, null, null));

        public Task<int> RegisterProvenanceAsync(
            DataProvenance provenance,
            CancellationToken cancellationToken = default)
        {
            RegisteredProvenances.Add(provenance);
            return Task.FromResult(42);
        }

        public Task ImportBatchAsync(
            IReadOnlyList<OpeningImportItem> items,
            CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(items.Count);
            SavedItems.AddRange(items);
            return Task.CompletedTask;
        }

        public Task<int> DeleteByProvenanceAsync(int provenanceId, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.FromResult(0);
        }
    }
}

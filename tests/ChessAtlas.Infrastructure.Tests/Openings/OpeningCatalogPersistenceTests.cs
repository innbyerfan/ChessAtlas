using ChessAtlas.Application.Openings;
using ChessAtlas.Domain.Chess;
using ChessAtlas.Domain.Openings;
using ChessAtlas.Domain.Provenance;
using ChessAtlas.Infrastructure.Openings;
using ChessAtlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessAtlas.Infrastructure.Tests.Openings;

public sealed class OpeningCatalogPersistenceTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    // The caller owns the context lifetime: these build services over an already-open context
    // rather than creating and disposing one of their own.
    private static OpeningCatalogImporter Importer(ChessAtlasDbContext context) =>
        new(context, NullLogger<OpeningCatalogImporter>.Instance);

    private OpeningCatalogRepository Repository() => new(_db.CreateContext());

    private static DataProvenance Provenance() => DataProvenance.Create(
        provider: "test/catalogue",
        uri: "https://example.invalid/",
        license: "CC0-1.0",
        redistributionAllowed: true,
        versionOrCommit: "deadbeef");

    private static OpeningImportItem Item(
        string canonicalName,
        string san,
        string? eco = "B20",
        OpeningLineKind kind = OpeningLineKind.SubVariation,
        VerificationStatus status = VerificationStatus.ReferenceVerified,
        int? provenanceId = 1,
        string? notes = null) =>
        new(canonicalName, eco, san, kind, ClassificationOrigin.UpstreamName, status, provenanceId, notes);

    [Fact]
    public void Migrations_create_the_expected_schema()
    {
        // __EFMigrationsLock is created by EF Core 10's SQLite migrator alongside the history table.
        Assert.Equal(
            new[]
            {
                "DataProvenances", "OpeningAliases", "OpeningLines", "Openings",
                "__EFMigrationsHistory", "__EFMigrationsLock",
            },
            _db.TableNames());

        var indexes = _db.IndexNames();
        Assert.Contains(indexes, i => i.StartsWith("IX_OpeningLines_TerminalPositionKey", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.StartsWith("IX_Openings_CanonicalName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Provenance_is_registered_once_per_snapshot()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);

        var first = await importer.RegisterProvenanceAsync(Provenance());
        var second = await importer.RegisterProvenanceAsync(Provenance());

        Assert.Equal(first, second);
        Assert.Equal(1, await context.DataProvenances.CountAsync());
    }

    [Fact]
    public async Task Importing_creates_the_opening_and_its_ancestors()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);
        var provenanceId = await importer.RegisterProvenanceAsync(Provenance());

        await importer.ImportBatchAsync(
            [Item("Sicilian Defense: Najdorf Variation: English Attack", "1. e4 c5 2. Nf3 d6", provenanceId: provenanceId)]);

        var names = await context.Openings.Select(o => o.CanonicalName).ToListAsync();

        Assert.Contains("Sicilian Defense", names);
        Assert.Contains("Sicilian Defense: Najdorf Variation", names);
        Assert.Contains("Sicilian Defense: Najdorf Variation: English Attack", names);
    }

    [Fact]
    public async Task Ancestors_are_linked_to_their_parents()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);
        var provenanceId = await importer.RegisterProvenanceAsync(Provenance());

        await importer.ImportBatchAsync(
            [Item("Sicilian Defense: Najdorf Variation", "1. e4 c5 2. Nf3 d6", provenanceId: provenanceId)]);

        var child = await context.Openings.SingleAsync(o => o.CanonicalName == "Sicilian Defense: Najdorf Variation");
        var parent = await context.Openings.SingleAsync(o => o.CanonicalName == "Sicilian Defense");

        Assert.Equal(parent.Id, child.ParentOpeningId);
        Assert.Equal(2, child.Depth);
        Assert.Equal("Sicilian Defense", child.FamilyKey);
        Assert.Equal("Najdorf Variation", child.TerminalName);
    }

    [Fact]
    public async Task Reimporting_the_same_batch_changes_nothing()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);
        var provenanceId = await importer.RegisterProvenanceAsync(Provenance());
        var batch = new[] { Item("Sicilian Defense", "1. e4 c5", provenanceId: provenanceId) };

        await importer.ImportBatchAsync(batch);
        await importer.ImportBatchAsync(batch);
        await importer.ImportBatchAsync(batch);

        Assert.Equal(1, await context.OpeningLines.CountAsync());
        Assert.Equal(1, await context.Openings.CountAsync());
    }

    [Fact]
    public async Task The_same_position_reached_by_different_names_is_stored_twice_and_found_together()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);
        var provenanceId = await importer.RegisterProvenanceAsync(Provenance());

        await importer.ImportBatchAsync(
            [
                Item("Sicilian Defense", "1. e4 c5", provenanceId: provenanceId),
                Item("Sicilian Defense: Steinitz Variation", "1. e4 c5", eco: "B20", provenanceId: provenanceId),
            ]);

        // Two different openings, one terminal position: the transposition view.
        Assert.Equal(2, await context.OpeningLines.CountAsync());
        Assert.Equal(1, await context.OpeningLines.Select(l => l.TerminalPositionKey).Distinct().CountAsync());

        var repository = Repository();
        var key = await context.OpeningLines.Select(l => l.TerminalPositionKey).FirstAsync();
        var lines = await repository.GetLinesEndingAtAsync(key);

        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lines.Select(l => l.OpeningId).Distinct().Count());
    }

    [Fact]
    public async Task Delete_by_provenance_removes_only_that_snapshot()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);

        var keep = await importer.RegisterProvenanceAsync(Provenance());
        var remove = await importer.RegisterProvenanceAsync(
            DataProvenance.Create(
                provider: "test/catalogue",
                uri: "https://example.invalid/",
                license: "CC0-1.0",
                redistributionAllowed: true,
                versionOrCommit: "other"));

        await importer.ImportBatchAsync([Item("Sicilian Defense", "1. e4 c5", provenanceId: keep)]);
        await importer.ImportBatchAsync([Item("French Defense", "1. e4 e6", eco: "C00", provenanceId: remove)]);

        var removed = await importer.DeleteByProvenanceAsync(remove);

        Assert.Equal(1, removed);
        Assert.Equal("Sicilian Defense", await context.OpeningLines.Select(l => l.Name).SingleAsync());
    }

    [Fact]
    public async Task Catalogue_state_reports_counts_and_the_latest_snapshot()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);
        var provenanceId = await importer.RegisterProvenanceAsync(Provenance());

        await importer.ImportBatchAsync([Item("Sicilian Defense", "1. e4 c5", provenanceId: provenanceId)]);

        var state = await importer.GetStateAsync();

        Assert.Equal(1, state.LineCount);
        Assert.Equal(1, state.OpeningCount);
        Assert.False(state.IsEmpty);
        Assert.Equal("deadbeef", state.LatestProvenance?.VersionOrCommit);
    }

    [Fact]
    public async Task An_empty_catalogue_reports_empty_rather_than_throwing()
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);

        Assert.True((await importer.GetStateAsync()).IsEmpty);
    }

    // ---- search -------------------------------------------------------------------------------

    private async Task SeedAsync(params OpeningImportItem[] items)
    {
        await using var context = _db.CreateContext();
        var importer = new OpeningCatalogImporter(context, NullLogger<OpeningCatalogImporter>.Instance);
        var provenanceId = await importer.RegisterProvenanceAsync(Provenance());

        await importer.ImportBatchAsync(
            items.Select(i => i with { ProvenanceId = provenanceId }).ToArray());
    }

    private static readonly OpeningImportItem[] SampleCatalogue =
    [
        Item("Ruy Lopez", "1. e4 e5 2. Nf3 Nc6 3. Bb5", eco: "C60", kind: OpeningLineKind.MainLine),
        Item("Ruy Lopez: Berlin Defense", "1. e4 e5 2. Nf3 Nc6 3. Bb5 Nf6", eco: "C65"),
        Item("Sicilian Defense", "1. e4 c5", eco: "B20", kind: OpeningLineKind.MainLine),
        Item("Sicilian Defense: Najdorf Variation", "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6", eco: "B90"),
        Item("Amar Opening: Paris Gambit", "1. Nh3 d5 2. g3 e5 3. f4", eco: "A00", kind: OpeningLineKind.Gambit),
    ];

    [Fact]
    public async Task Search_with_no_filter_returns_everything_paged()
    {
        await SeedAsync(SampleCatalogue);

        var page = await Repository().SearchAsync(new OpeningSearchQuery { PageSize = 3 });

        // Six, not five: "Amar Opening: Paris Gambit" makes the importer create its ancestor
        // "Amar Opening", which is an opening in its own right.
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.False(page.HasPreviousPage);
    }

    [Fact]
    public async Task Search_matches_on_name_and_eco()
    {
        await SeedAsync(SampleCatalogue);
        var repository = Repository();

        Assert.Equal(2, (await repository.SearchAsync(new OpeningSearchQuery { Text = "Sicilian" })).TotalCount);
        Assert.Equal(1, (await repository.SearchAsync(new OpeningSearchQuery { Text = "Najdorf" })).TotalCount);
        Assert.Equal(1, (await repository.SearchAsync(new OpeningSearchQuery { Text = "C65" })).TotalCount);
        Assert.Equal(0, (await repository.SearchAsync(new OpeningSearchQuery { Text = "Grünfeld" })).TotalCount);
    }

    [Fact]
    public async Task Search_filters_by_eco_prefix()
    {
        await SeedAsync(SampleCatalogue);
        var repository = Repository();

        Assert.Equal(2, (await repository.SearchAsync(new OpeningSearchQuery { EcoPrefix = "C6" })).TotalCount);
        Assert.Equal(2, (await repository.SearchAsync(new OpeningSearchQuery { EcoPrefix = "B" })).TotalCount);
        Assert.Equal(1, (await repository.SearchAsync(new OpeningSearchQuery { EcoPrefix = "A" })).TotalCount);
    }

    [Fact]
    public async Task Search_filters_by_family()
    {
        await SeedAsync(SampleCatalogue);

        var page = await Repository().SearchAsync(new OpeningSearchQuery { FamilyKey = "Ruy Lopez" });

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, i => Assert.Equal("Ruy Lopez", i.FamilyKey));
    }

    [Fact]
    public async Task Search_filters_by_line_kind_so_gambits_can_be_found_on_their_own()
    {
        await SeedAsync(SampleCatalogue);

        var page = await Repository().SearchAsync(new OpeningSearchQuery { LineKind = OpeningLineKind.Gambit });

        var item = Assert.Single(page.Items);
        Assert.Equal("Amar Opening: Paris Gambit", item.CanonicalName);
    }

    [Fact]
    public async Task Search_sorts_by_eco_with_uncoded_openings_last()
    {
        await SeedAsync(SampleCatalogue);

        var page = await Repository().SearchAsync(
            new OpeningSearchQuery { Sort = OpeningSortOrder.Eco, PageSize = 100 });

        // The generated "Amar Opening" ancestor carries no ECO code and sorts after the coded ones,
        // which is the behaviour this test exists to pin down.
        Assert.Equal(
            new string?[] { "A00", "B20", "B90", "C60", "C65", null },
            page.Items.Select(i => i.EcoCode));
    }

    [Fact]
    public async Task Search_reports_the_eco_section_name()
    {
        await SeedAsync(SampleCatalogue);

        var page = await Repository().SearchAsync(new OpeningSearchQuery { Text = "Najdorf" });

        Assert.Equal("Semi-Open Games (except the French)", Assert.Single(page.Items).EcoFamily);
    }

    [Fact]
    public async Task Search_paging_is_clamped_to_a_sane_range()
    {
        await SeedAsync(SampleCatalogue);

        var page = await Repository().SearchAsync(new OpeningSearchQuery { Page = 0, PageSize = 10_000 });

        Assert.Equal(1, page.Page);
        Assert.Equal(200, page.PageSize);
    }

    [Fact]
    public async Task Detail_returns_aliases_children_and_the_parent_name()
    {
        await SeedAsync(SampleCatalogue);
        await using (var context = _db.CreateContext())
        {
            var opening = await context.Openings.SingleAsync(o => o.CanonicalName == "Sicilian Defense");
            opening.AddAlias("Sicilian Defence", "en-GB");
            opening.AddAlias("سیسیلی", "fa");
            opening.SetDescription("Black answers 1.e4 with 1...c5.");
            await context.SaveChangesAsync();
        }

        var detail = await Repository().GetDetailByCanonicalNameAsync("Sicilian Defense");

        Assert.NotNull(detail);
        Assert.Equal("Sicilian Defense", detail.Summary.CanonicalName);
        Assert.Equal("Black answers 1.e4 with 1...c5.", detail.Description);
        Assert.Equal(2, detail.Aliases.Count);
        Assert.Equal("Sicilian Defense: Najdorf Variation", Assert.Single(detail.Children).CanonicalName);
        Assert.Null(detail.ParentOpeningName);
    }

    [Fact]
    public async Task Detail_of_a_child_names_its_parent()
    {
        await SeedAsync(SampleCatalogue);

        var detail = await Repository().GetDetailByCanonicalNameAsync("Sicilian Defense: Najdorf Variation");

        Assert.NotNull(detail);
        Assert.Equal("Sicilian Defense", detail.ParentOpeningName);
        Assert.Equal(1, detail.Summary.Depth - 1);
    }

    [Fact]
    public async Task Looking_up_a_missing_opening_returns_null_not_an_exception()
    {
        await SeedAsync(SampleCatalogue);

        Assert.Null(await Repository().GetDetailByCanonicalNameAsync("Not A Real Opening"));
        Assert.Null(await Repository().GetDetailAsync(999_999));
    }

    [Fact]
    public async Task Prefix_lookup_feeds_a_picker()
    {
        await SeedAsync(SampleCatalogue);

        var names = await Repository().FindNamesByPrefixAsync("Sicilian", 10);

        Assert.Equal(2, names.Count);
        Assert.Equal("Sicilian Defense", names[0]);
    }

    [Fact]
    public async Task Lines_carry_the_licence_of_the_source_they_came_from()
    {
        await SeedAsync(SampleCatalogue);
        await using var context = _db.CreateContext();
        var key = await context.OpeningLines.Select(l => l.TerminalPositionKey).FirstAsync();

        var line = (await Repository().GetLinesEndingAtAsync(key)).First();

        Assert.Equal("test/catalogue", line.SourceProvider);
        Assert.Equal("CC0-1.0", line.SourceLicense);
    }

    [Fact]
    public async Task An_imported_line_can_be_replayed_from_the_database()
    {
        await SeedAsync(SampleCatalogue);
        await using var context = _db.CreateContext();
        var stored = await context.OpeningLines.SingleAsync(l => l.Name == "Sicilian Defense");

        var sequence = stored.Resolve();

        Assert.Equal("e2e4 c7c5", sequence.ToUciText());
        Assert.Equal(sequence.TerminalPositionKey, stored.TerminalPositionKey);
        Assert.Equal(
            MoveSequence.FromSan("1. e4 c5").TerminalPositionKey,
            stored.TerminalPositionKey);
    }
}

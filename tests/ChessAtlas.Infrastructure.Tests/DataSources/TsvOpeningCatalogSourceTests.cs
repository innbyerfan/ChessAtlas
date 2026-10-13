using ChessAtlas.Infrastructure.DataSources;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessAtlas.Infrastructure.Tests.DataSources;

public sealed class TsvOpeningCatalogSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chessatlas-tsv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// <c>IAsyncEnumerable</c> has no LINQ operators in the BCL, and pulling in System.Linq.Async
    /// for two calls is not worth a dependency. This is the whole of what the tests need.
    /// </summary>
    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }

    private string WriteCatalogue(params string[] lines)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllLines(Path.Combine(_root, "a.tsv"), new[] { "eco\tname\tpgn" }.Concat(lines));
        return _root;
    }

    private static TsvOpeningCatalogSource Source(string directory) =>
        new(directory, LichessChessOpenings.CreateProvenance(), NullLogger<TsvOpeningCatalogSource>.Instance);

    [Fact]
    public async Task Rows_are_streamed_in_file_order()
    {
        var source = Source(WriteCatalogue(
            "A00\tAmar Opening\t1. Nh3",
            "A00\tAmar Opening: Paris Gambit\t1. Nh3 d5 2. g3 e5 3. f4"));

        var rows = await CollectAsync(source.ReadRowsAsync());

        Assert.Equal(2, rows.Count);
        Assert.Equal("A00", rows[0].EcoCode);
        Assert.Equal("Amar Opening", rows[0].Name);
        Assert.Equal("1. Nh3", rows[0].MoveText);
        Assert.Equal("Amar Opening: Paris Gambit", rows[1].Name);
    }

    [Fact]
    public async Task A_short_line_is_yielded_with_empty_moves_so_the_importer_can_reject_it()
    {
        var source = Source(WriteCatalogue("A00\tTruncated Row"));

        var rows = await CollectAsync(source.ReadRowsAsync());
        var row = Assert.Single(rows);

        Assert.Equal("Truncated Row", row.Name);
        Assert.Equal(string.Empty, row.MoveText);
    }

    [Fact]
    public async Task A_missing_directory_yields_nothing_instead_of_throwing()
    {
        var source = Source(Path.Combine(_root, "does-not-exist"));

        Assert.False(source.IsAvailable);
        Assert.Empty(await CollectAsync(source.ReadRowsAsync()));
    }

    [Fact]
    public void IsAvailable_is_true_once_the_files_are_there()
    {
        Assert.False(Source(_root).IsAvailable);

        WriteCatalogue("A00\tAmar Opening\t1. Nh3");

        Assert.True(Source(_root).IsAvailable);
    }

    [Fact]
    public async Task Enumeration_stops_when_cancelled()
    {
        var source = Source(WriteCatalogue(
            Enumerable.Range(0, 5000).Select(i => $"A00\tLine {i}\t1. Nh3").ToArray()));

        using var cts = new CancellationTokenSource();
        var count = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.ReadRowsAsync(cts.Token))
            {
                if (++count == 10) await cts.CancelAsync();
            }
        });

        Assert.Equal(10, count);
    }

    [Fact]
    public void Provenance_names_the_source_and_its_licence()
    {
        var provenance = LichessChessOpenings.CreateProvenance();

        Assert.Equal("lichess-org/chess-openings", provenance.Provider);
        Assert.Equal("CC0-1.0", provenance.License);
        Assert.True(provenance.RedistributionAllowed);
        Assert.False(string.IsNullOrWhiteSpace(provenance.VersionOrCommit));
        Assert.Equal("lichess-org/chess-openings", Source(_root).Provider);
    }

    [Fact]
    public void The_bundled_snapshot_is_found_by_walking_up_from_the_build_output()
    {
        // Tests run from tests/ChessAtlas.Infrastructure.Tests/bin/..., so this proves the same
        // resolution the packaged app relies on, against the files actually committed to the repo.
        var directory = LichessChessOpenings.ResolveDirectory(AppContext.BaseDirectory);

        Assert.False(string.IsNullOrEmpty(directory), "the bundled CC0 catalogue was not found from the test output");
        Assert.True(Source(directory!).IsAvailable);
        Assert.Equal(5, Source(directory!).EnumerateFiles().Count());
    }
}

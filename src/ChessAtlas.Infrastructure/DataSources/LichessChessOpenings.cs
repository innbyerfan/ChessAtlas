using ChessAtlas.Domain.Provenance;

namespace ChessAtlas.Infrastructure.DataSources;

/// <summary>
/// Identity of the bundled opening catalogue: lichess-org/chess-openings.
///
/// <para>
/// The snapshot identity is duplicated here and in
/// <c>data/openings/lichess-chess-openings/SOURCE.md</c>. Both have to be updated together when the
/// snapshot is refreshed — the file is what a human reads, this is what ends up on every imported row.
/// </para>
/// </summary>
public static class LichessChessOpenings
{
    public const string Provider = "lichess-org/chess-openings";

    public const string RepositoryUri = "https://github.com/lichess-org/chess-openings";

    /// <summary>Commit the bundled files were taken from.</summary>
    public const string SnapshotCommit = "a6189a30dc273ccb21fc2536a9a2fefd5592a67a";

    /// <summary>CC0 1.0: no attribution required, and redistribution inside the app is allowed.</summary>
    public const string License = "CC0-1.0";

    public const string RelativeDirectory = "data/openings/lichess-chess-openings";

    public static DataProvenance CreateProvenance() => DataProvenance.Create(
        provider: Provider,
        uri: RepositoryUri,
        license: License,
        redistributionAllowed: true,
        versionOrCommit: SnapshotCommit,
        notes: "Opening names, ECO codes and one reference line per name. Naming catalogue only: " +
               "no evaluations, no trap theory, no model games.");

    /// <summary>
    /// Locates the catalogue directory. Looks next to the running assembly first (the packaged app
    /// ships the files beside the binary), then walks up towards the repository root so tests and
    /// <c>dotnet run</c> from a build output find the same files a developer sees.
    /// </summary>
    public static string? ResolveDirectory(string? startDirectory = null)
    {
        var cursor = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);

        for (var depth = 0; cursor is not null && depth < 12; depth++)
        {
            var candidate = Path.Combine(cursor.FullName, RelativeDirectory);
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.tsv").Any())
                return candidate;

            cursor = cursor.Parent;
        }

        return null;
    }
}

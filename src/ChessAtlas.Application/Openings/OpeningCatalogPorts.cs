using ChessAtlas.Application.Common;
using ChessAtlas.Domain.Chess;
using ChessAtlas.Domain.Openings;
using ChessAtlas.Domain.Provenance;

namespace ChessAtlas.Application.Openings;

/// <summary>
/// One raw row from an opening catalogue source, before it has been validated. Keeping this as a
/// separate shape from the domain entities is what lets a corrupt row be reported instead of
/// aborting the import.
/// </summary>
public sealed record OpeningCatalogRow(string? EcoCode, string Name, string MoveText);

/// <summary>
/// A source of opening catalogue rows. Implemented by the bundled CC0 snapshot today; a remote
/// source would implement the same port, and nothing above this line would change.
/// </summary>
public interface IOpeningCatalogSource
{
    /// <summary>Stable identifier used to deduplicate and to attribute rows.</summary>
    string Provider { get; }

    /// <summary>Licence and snapshot identity of the data this source yields.</summary>
    DataProvenance Provenance { get; }

    /// <summary>
    /// Streams rows. Implementations must not buffer the source: the bundled snapshot is 3.8k rows,
    /// but the same port is what a full game-derived catalogue would use.
    /// </summary>
    IAsyncEnumerable<OpeningCatalogRow> ReadRowsAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one validated row contributes to an import batch.</summary>
public sealed record OpeningImportItem(
    string CanonicalName,
    string? EcoCode,
    string SanText,
    OpeningLineKind Kind,
    ClassificationOrigin KindOrigin,
    VerificationStatus VerificationStatus,
    int? ProvenanceId,
    string? Notes = null);

/// <summary>Snapshot of the catalogue after an import.</summary>
public sealed record OpeningCatalogState(
    int OpeningCount,
    int LineCount,
    int ProvenanceCount,
    DataProvenance? LatestProvenance,
    DateTimeOffset? LastImportedAtUtc)
{
    public bool IsEmpty => OpeningCount == 0 && LineCount == 0;
}

/// <summary>Result of a catalogue import, including what was skipped and why.</summary>
public sealed record OpeningImportSummary(
    int RowsRead,
    int RowsImported,
    int RowsSkippedAsDuplicates,
    int RowsRejected,
    TimeSpan Elapsed,
    IReadOnlyList<string> RejectionSamples,
    DataProvenance Provenance)
{
    public bool IsEmpty => RowsImported == 0 && RowsRead == 0;
}

/// <summary>Write side of the catalogue: batched, transactional, idempotent.</summary>
public interface IOpeningCatalogImporter
{
    Task<OpeningCatalogState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a batch inside one transaction. Re-importing identical rows must be a no-op, so an
    /// interrupted import can simply be restarted.
    /// </summary>
    Task ImportBatchAsync(IReadOnlyList<OpeningImportItem> items, CancellationToken cancellationToken = default);

    /// <summary>Records the snapshot identity of the source that was just imported.</summary>
    Task<int> RegisterProvenanceAsync(DataProvenance provenance, CancellationToken cancellationToken = default);

    /// <summary>Removes everything imported from a given provenance, for a clean re-import.</summary>
    Task<int> DeleteByProvenanceAsync(int provenanceId, CancellationToken cancellationToken = default);
}

/// <summary>Read side of the catalogue.</summary>
public interface IOpeningCatalogRepository
{
    Task<PagedResult<OpeningSummaryDto>> SearchAsync(
        OpeningSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<OpeningDetailDto?> GetDetailAsync(int openingId, CancellationToken cancellationToken = default);

    Task<OpeningDetailDto?> GetDetailByCanonicalNameAsync(
        string canonicalName,
        CancellationToken cancellationToken = default);

    /// <summary>Every named line whose terminal position matches — the transposition view.</summary>
    Task<IReadOnlyList<OpeningLineDto>> GetLinesEndingAtAsync(
        string positionKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpeningLineDto>> GetLinesForOpeningAsync(
        int openingId,
        CancellationToken cancellationToken = default);

    /// <summary>Opening names whose canonical name starts with <paramref name="prefix"/>. For pickers.</summary>
    Task<IReadOnlyList<string>> FindNamesByPrefixAsync(
        string prefix,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Filter and paging for the opening list. Every field optional; no filter means the whole catalogue.</summary>
public sealed record OpeningSearchQuery
{
    /// <summary>Free text matched against canonical names, aliases and ECO codes.</summary>
    public string? Text { get; init; }

    /// <summary>ECO prefix: <c>B</c>, <c>B9</c> or a full <c>B90</c>.</summary>
    public string? EcoPrefix { get; init; }

    /// <summary>Exact family, i.e. the first path segment.</summary>
    public string? FamilyKey { get; init; }

    public int? MinDepth { get; init; }
    public int? MaxDepth { get; init; }

    public OpeningDifficulty? Difficulty { get; init; }

    public VerificationStatus? VerificationStatus { get; init; }

    /// <summary>Only openings that own at least one line of this kind (gambit, trap, …).</summary>
    public OpeningLineKind? LineKind { get; init; }

    /// <summary>
    /// Filter by the colour that made the last move of the opening's shortest line — the practical
    /// meaning of "openings for White" when picking repertoire material.
    /// </summary>
    public Side? LastMoveBy { get; init; }

    public OpeningSortOrder Sort { get; init; } = OpeningSortOrder.Name;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    /// <summary>Clamps paging into a sane range so a bad query cannot ask SQLite for ten million rows.</summary>
    public OpeningSearchQuery Normalise()
    {
        var page = Page < 1 ? 1 : Page;
        var pageSize = Math.Clamp(PageSize, 1, 200);
        return Page == page && PageSize == pageSize
            ? this
            : this with { Page = page, PageSize = pageSize };
    }
}

public enum OpeningSortOrder
{
    Name = 0,
    Eco = 1,
    Family = 2,
    LineCountDescending = 3,
    RecentlyUpdated = 4,
}

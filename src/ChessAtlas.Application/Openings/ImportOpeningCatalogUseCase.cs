using System.Diagnostics;
using ChessAtlas.Application.Common;
using ChessAtlas.Domain.Chess;
using ChessAtlas.Domain.Openings;
using Microsoft.Extensions.Logging;

namespace ChessAtlas.Application.Openings;

/// <summary>Knobs for <see cref="ImportOpeningCatalogUseCase"/>.</summary>
public sealed record ImportOpeningCatalogOptions
{
    /// <summary>Rows per database transaction. Small enough to keep memory flat, large enough to amortise commits.</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>
    /// Optional ceiling, used by tests and by a "dry run" preview. Null imports everything.
    /// </summary>
    public int? MaxRows { get; init; }

    /// <summary>
    /// When true, rows previously imported from the same provider are removed first, giving a clean
    /// snapshot. Off by default so an interrupted import can simply be resumed — re-importing is
    /// idempotent either way.
    /// </summary>
    public bool ReplaceExistingFromSameProvider { get; init; }

    public static ImportOpeningCatalogOptions Default => new();
}

/// <summary>
/// Imports an opening catalogue into the database.
///
/// <para>
/// The shape of the operation is dictated by the data: rows are streamed from the source, validated
/// one at a time, and flushed in bounded batches. Nothing here holds the catalogue in memory, so the
/// same code path handles the bundled 3.8k-row CC0 snapshot and a source ten thousand times larger.
/// </para>
///
/// <para>
/// A row that does not parse is counted and sampled, never fatal. An import that dies halfway is
/// safe to restart because batches are transactional and the unique index on
/// (opening, move sequence) makes a re-import a no-op.
/// </para>
/// </summary>
public sealed class ImportOpeningCatalogUseCase
{
    private const int RejectionSampleLimit = 50;

    private readonly IOpeningCatalogSource _source;
    private readonly IOpeningCatalogImporter _importer;
    private readonly ILogger<ImportOpeningCatalogUseCase> _logger;

    public ImportOpeningCatalogUseCase(
        IOpeningCatalogSource source,
        IOpeningCatalogImporter importer,
        ILogger<ImportOpeningCatalogUseCase> logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OpeningImportSummary> ExecuteAsync(
        ImportOpeningCatalogOptions? options = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= ImportOpeningCatalogOptions.Default;
        if (options.BatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Batch size must be at least 1.");

        var stopwatch = Stopwatch.StartNew();
        var provenance = _source.Provenance;

        _logger.LogInformation(
            "Starting opening catalogue import from {Provider} ({License}, snapshot {Snapshot}).",
            provenance.Provider, provenance.License, provenance.VersionOrCommit ?? "unknown");

        var provenanceId = await _importer.RegisterProvenanceAsync(provenance, cancellationToken).ConfigureAwait(false);

        if (options.ReplaceExistingFromSameProvider)
        {
            var removed = await _importer.DeleteByProvenanceAsync(provenanceId, cancellationToken).ConfigureAwait(false);
            if (removed > 0)
                _logger.LogInformation("Replaced {Count} lines previously imported from {Provider}.", removed, provenance.Provider);
        }

        var rejections = new List<string>();
        var batch = new List<OpeningImportItem>(options.BatchSize);
        var seenInThisRun = new HashSet<string>(StringComparer.Ordinal);

        var rowsRead = 0;
        var rowsImported = 0;
        var rowsDuplicated = 0;
        var rowsRejected = 0;

        progress?.Report(new OperationProgress("Reading catalogue", 0, options.MaxRows ?? 0, $"Source: {_source.Provider}"));

        await foreach (var row in _source.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.MaxRows is int limit && rowsRead >= limit) break;
            rowsRead++;

            if (!TryBuildItem(row, provenanceId, out var item, out var reason))
            {
                rowsRejected++;
                if (rejections.Count < RejectionSampleLimit)
                    rejections.Add($"row {rowsRead} '{row.Name}': {reason}");
                continue;
            }

            var dedupeKey = item.CanonicalName + "\n" + item.SanText;
            if (!seenInThisRun.Add(dedupeKey))
            {
                rowsDuplicated++;
                continue;
            }

            batch.Add(item);

            if (batch.Count < options.BatchSize) continue;

            await _importer.ImportBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            rowsImported += batch.Count;
            batch.Clear();

            progress?.Report(new OperationProgress(
                "Importing openings", rowsImported, options.MaxRows ?? 0, $"{rowsImported} lines committed"));
        }

        if (batch.Count > 0)
        {
            await _importer.ImportBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            rowsImported += batch.Count;
            batch.Clear();
        }

        stopwatch.Stop();
        progress?.Report(new OperationProgress("Importing openings", rowsImported, rowsImported, "Complete"));

        _logger.LogInformation(
            "Opening catalogue import finished in {Elapsed}: {Imported} imported, {Duplicates} duplicates, {Rejected} rejected.",
            stopwatch.Elapsed, rowsImported, rowsDuplicated, rowsRejected);

        return new OpeningImportSummary(
            RowsRead: rowsRead,
            RowsImported: rowsImported,
            RowsSkippedAsDuplicates: rowsDuplicated,
            RowsRejected: rowsRejected,
            Elapsed: stopwatch.Elapsed,
            RejectionSamples: rejections,
            Provenance: provenance);
    }

    /// <summary>
    /// Validates a raw row and turns it into an import item.
    ///
    /// <para>
    /// Validation is "can this be replayed as a legal chess line", nothing more. Note what this
    /// deliberately does <em>not</em> do: it never upgrades a row to <see cref="OpeningLineKind.Trap"/>.
    /// A trap is a claim that a specific move loses, and no naming catalogue supports that claim —
    /// publishing one would be inventing theory. The word "Gambit" is taken from the upstream name
    /// itself and recorded as <see cref="ClassificationOrigin.UpstreamName"/>, so the UI can show it
    /// as sourced rather than as an editorial judgement.
    /// </para>
    /// </summary>
    internal static bool TryBuildItem(
        OpeningCatalogRow row,
        int provenanceId,
        out OpeningImportItem item,
        out string rejectionReason)
    {
        item = default!;
        rejectionReason = string.Empty;

        if (string.IsNullOrWhiteSpace(row.Name))
        {
            rejectionReason = "the row has no name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(row.MoveText))
        {
            rejectionReason = "the row has no moves.";
            return false;
        }

        string[] segments;
        try
        {
            segments = Opening.SplitPath(row.Name);
        }
        catch (ArgumentException ex)
        {
            rejectionReason = ex.Message;
            return false;
        }

        EcoCode? eco = null;
        if (!string.IsNullOrWhiteSpace(row.EcoCode))
        {
            if (!EcoCode.TryParse(row.EcoCode, out var parsed))
            {
                rejectionReason = $"'{row.EcoCode}' is not a valid ECO code.";
                return false;
            }

            eco = parsed;
        }

        try
        {
            // Replaying the line is the validation. A source that lists an illegal continuation is
            // broken, and shipping it would put an unplayable move in front of the user.
            _ = MoveSequence.FromSan(row.MoveText);
        }
        catch (FormatException ex)
        {
            rejectionReason = ex.Message;
            return false;
        }

        var classification = ClassifyByUpstreamName(segments);

        item = new OpeningImportItem(
            CanonicalName: string.Join(": ", segments),
            EcoCode: eco?.Value,
            SanText: row.MoveText.Trim(),
            Kind: classification.Kind,
            KindOrigin: ClassificationOrigin.UpstreamName,
            VerificationStatus: classification.VerificationStatus,
            ProvenanceId: provenanceId,
            Notes: classification.Notes);

        return true;
    }

    private readonly record struct RowClassification(
        OpeningLineKind Kind,
        VerificationStatus VerificationStatus,
        string? Notes)
    {
        public static RowClassification Verified(OpeningLineKind kind) =>
            new(kind, VerificationStatus.ReferenceVerified, null);

        public static RowClassification Unverified(OpeningLineKind kind, string notes) =>
            new(kind, VerificationStatus.Unverified, notes);
    }

    /// <summary>
    /// Reads the kind off the upstream naming, which is a source we can point at. The one case that
    /// gets downgraded is a trap: the catalogue supplies a name and a move order but no explanation
    /// of why the line loses, so the row is stored as a trap the <em>source</em> asserts, marked
    /// unverified, with a note saying exactly that. Chess Atlas does not adopt the claim.
    /// </summary>
    private static RowClassification ClassifyByUpstreamName(string[] segments)
    {
        var terminal = segments[^1];

        if (Contains(terminal, "gambit")) return RowClassification.Verified(OpeningLineKind.Gambit);

        if (Contains(terminal, "trap"))
        {
            return RowClassification.Unverified(
                OpeningLineKind.Trap,
                "Named as a trap by the imported catalogue, which supplies the name and the move " +
                "order but not why the line loses. Not independently verified by Chess Atlas.");
        }

        // A top-level name is the opening itself, so its line is the main line; anything below it is
        // a variation. (The reference line for a top-level name is not always one move long - the
        // Nimzowitsch Defense is "1. e4 Nc6" - so depth, not length, decides.)
        return RowClassification.Verified(
            segments.Length == 1 ? OpeningLineKind.MainLine : OpeningLineKind.SubVariation);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

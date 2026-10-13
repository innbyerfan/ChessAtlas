using ChessAtlas.Application.Openings;
using ChessAtlas.Domain.Openings;
using ChessAtlas.Domain.Provenance;
using ChessAtlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChessAtlas.Infrastructure.Openings;

/// <summary>
/// Persists imported opening catalogue rows in bounded, transactional batches.
///
/// <para>
/// Idempotency is the load-bearing property. An import of a large catalogue can be interrupted by a
/// crash, a cancellation or a full disk, and the user's only sane recovery is "run it again". That
/// works because (a) openings are looked up by canonical name rather than assumed absent, (b) a line
/// already present for an opening is skipped, and (c) a unique index on
/// <c>(OpeningId, StartingFen, UciText)</c> is the final backstop if two runs overlap.
/// </para>
/// </summary>
public sealed class OpeningCatalogImporter : IOpeningCatalogImporter
{
    private readonly ChessAtlasDbContext _db;
    private readonly ILogger<OpeningCatalogImporter> _logger;

    public OpeningCatalogImporter(ChessAtlasDbContext db, ILogger<OpeningCatalogImporter> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OpeningCatalogState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var openingCount = await _db.Openings.CountAsync(cancellationToken).ConfigureAwait(false);
        var lineCount = await _db.OpeningLines.CountAsync(cancellationToken).ConfigureAwait(false);
        var provenanceCount = await _db.DataProvenances.CountAsync(cancellationToken).ConfigureAwait(false);

        var latest = await _db.DataProvenances
            .AsNoTracking()
            .OrderByDescending(p => p.RetrievedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OpeningCatalogState(openingCount, lineCount, provenanceCount, latest, latest?.RetrievedAt);
    }

    public async Task<int> RegisterProvenanceAsync(
        DataProvenance provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        var version = provenance.VersionOrCommit ?? string.Empty;
        var existing = await _db.DataProvenances
            .FirstOrDefaultAsync(p => p.Provider == provenance.Provider && (p.VersionOrCommit ?? string.Empty) == version, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null) return existing.Id;

        _db.DataProvenances.Add(provenance);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Registered data provenance {Provider} ({License}, snapshot {Snapshot}) as #{Id}.",
            provenance.Provider, provenance.License, provenance.VersionOrCommit ?? "unknown", provenance.Id);

        return provenance.Id;
    }

    public async Task ImportBatchAsync(
        IReadOnlyList<OpeningImportItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var openings = await ResolveOpeningsAsync(items, cancellationToken).ConfigureAwait(false);
        var existingLineKeys = await LoadExistingLineKeysAsync(openings.Values, cancellationToken).ConfigureAwait(false);

        var added = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!openings.TryGetValue(item.CanonicalName, out var openingId))
            {
                // Should not happen: ResolveOpeningsAsync creates every name in the batch.
                _logger.LogWarning("No opening row resolved for '{Name}'; the line was skipped.", item.CanonicalName);
                continue;
            }

            OpeningLine line;
            try
            {
                line = OpeningLine.Create(
                    openingId: openingId,
                    name: item.CanonicalName,
                    sanText: item.SanText,
                    kind: item.Kind,
                    kindOrigin: item.KindOrigin,
                    verificationStatus: item.VerificationStatus,
                    eco: EcoCode.TryParse(item.EcoCode, out var eco) ? eco : null,
                    notes: item.Notes,
                    provenanceId: item.ProvenanceId);
            }
            catch (FormatException ex)
            {
                // The use case already validated these; reaching here means the row changed between
                // validation and persistence. Report it rather than failing the batch.
                _logger.LogWarning(ex, "Line '{Name}' could not be built and was skipped.", item.CanonicalName);
                continue;
            }

            if (!existingLineKeys.Add(KeyFor(openingId, line.StartingFen, line.UciText))) continue;

            _db.OpeningLines.Add(line);
            added++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Committed a batch of {Total} rows; {Added} were new.", items.Count, added);
    }

    public async Task<int> DeleteByProvenanceAsync(int provenanceId, CancellationToken cancellationToken = default)
    {
        var removed = await _db.OpeningLines
            .Where(l => l.ProvenanceId == provenanceId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Deleted {Count} lines belonging to provenance #{Id}.", removed, provenanceId);
        return removed;
    }

    /// <summary>
    /// Returns the id of every opening named in the batch, creating the missing ones — and their
    /// ancestors, so "Sicilian Defense: Najdorf Variation: English Attack" is reachable by walking
    /// the tree even if the source never listed the middle levels on their own.
    /// </summary>
    private async Task<Dictionary<string, int>> ResolveOpeningsAsync(
        IReadOnlyList<OpeningImportItem> items,
        CancellationToken cancellationToken)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var segments = Opening.SplitPath(item.CanonicalName);
            for (var depth = 1; depth <= segments.Length; depth++)
                wanted.Add(string.Join(": ", segments.Take(depth)));
        }

        var known = await _db.Openings
            .Where(o => wanted.Contains(o.CanonicalName))
            .ToDictionaryAsync(o => o.CanonicalName, o => o.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var missing = wanted.Where(name => !known.ContainsKey(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (missing.Count == 0) return known;

        // Saved one depth level at a time: parents get their ids before children reference them, and
        // the batch costs a handful of round trips rather than one per opening.
        var byDepth = missing
            .GroupBy(name => Opening.SplitPath(name).Length)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in byDepth)
        {
            var created = new List<(string Name, Opening Opening)>();

            foreach (var name in group.OrderBy(n => n, StringComparer.Ordinal))
            {
                var segments = Opening.SplitPath(name);
                int? parentId = null;
                if (segments.Length > 1)
                {
                    var parentName = string.Join(": ", segments.Take(segments.Length - 1));
                    if (known.TryGetValue(parentName, out var pid)) parentId = pid;
                }

                var eco = items.FirstOrDefault(i => i.CanonicalName == name)?.EcoCode;
                var opening = Opening.Create(
                    name,
                    EcoCode.TryParse(eco, out var parsed) ? parsed : null,
                    parentOpeningId: parentId);

                _db.Openings.Add(opening);
                created.Add((name, opening));
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var (name, opening) in created) known[name] = opening.Id;
        }

        return known;
    }

    private async Task<HashSet<string>> LoadExistingLineKeysAsync(
        IEnumerable<int> openingIds,
        CancellationToken cancellationToken)
    {
        var ids = openingIds.ToList();
        if (ids.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        var existing = await _db.OpeningLines
            .AsNoTracking()
            .Where(l => ids.Contains(l.OpeningId))
            .Select(l => new { l.OpeningId, l.StartingFen, l.UciText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return existing
            .Select(l => KeyFor(l.OpeningId, l.StartingFen, l.UciText))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string KeyFor(int openingId, string startingFen, string uciText) =>
        $"{openingId}\n{startingFen}\n{uciText}";
}

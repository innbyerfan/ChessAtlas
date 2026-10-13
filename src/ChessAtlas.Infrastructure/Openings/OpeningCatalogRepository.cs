using ChessAtlas.Application.Common;
using ChessAtlas.Application.Openings;
using ChessAtlas.Domain.Chess;
using ChessAtlas.Domain.Openings;
using ChessAtlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessAtlas.Infrastructure.Openings;

/// <summary>
/// Read side of the opening catalogue.
///
/// <para>
/// Every method projects straight to a DTO with <c>AsNoTracking</c>. Entities are never handed to
/// the application layer: a list of 25 opening rows should cost 25 small records, not 25 tracked
/// graphs with their aliases and lines attached.
/// </para>
/// </summary>
public sealed class OpeningCatalogRepository : IOpeningCatalogRepository
{
    private readonly ChessAtlasDbContext _db;

    public OpeningCatalogRepository(ChessAtlasDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<PagedResult<OpeningSummaryDto>> SearchAsync(
        OpeningSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Normalise();

        var filtered = _db.Openings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            var like = $"%{text}%";
            filtered = filtered.Where(o =>
                EF.Functions.Like(o.CanonicalName, like) ||
                EF.Functions.Like(o.TerminalName, like) ||
                (o.EcoCodeValue != null && EF.Functions.Like(o.EcoCodeValue, like)) ||
                o.Aliases.Any(a => EF.Functions.Like(a.Alias, like)));
        }

        if (!string.IsNullOrWhiteSpace(query.EcoPrefix))
        {
            var prefix = query.EcoPrefix.Trim().ToUpperInvariant();
            var like = prefix + "%";
            filtered = filtered.Where(o => o.EcoCodeValue != null && EF.Functions.Like(o.EcoCodeValue, like));
        }

        if (!string.IsNullOrWhiteSpace(query.FamilyKey))
        {
            var family = query.FamilyKey.Trim();
            filtered = filtered.Where(o => o.FamilyKey == family);
        }

        if (query.MinDepth is int min) filtered = filtered.Where(o => o.Depth >= min);
        if (query.MaxDepth is int max) filtered = filtered.Where(o => o.Depth <= max);
        if (query.Difficulty is OpeningDifficulty difficulty) filtered = filtered.Where(o => o.Difficulty == difficulty);

        if (query.VerificationStatus is VerificationStatus status)
            filtered = filtered.Where(o => o.Lines.Any(l => l.VerificationStatus == status));

        if (query.LineKind is OpeningLineKind kind)
            filtered = filtered.Where(o => o.Lines.Any(l => l.Kind == kind));

        if (query.LastMoveBy is Side side)
        {
            // A line of odd length ends on a black move, so it is White to move next.
            var endsWithWhite = side == Side.White;
            filtered = filtered.Where(o => o.Lines.Any(l => (l.PlyCount % 2 == 1) == endsWithWhite));
        }

        var total = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        var ordered = query.Sort switch
        {
            OpeningSortOrder.Eco => filtered
                .OrderBy(o => o.EcoCodeValue == null)
                .ThenBy(o => o.EcoCodeValue)
                .ThenBy(o => o.CanonicalName),
            OpeningSortOrder.Family => filtered
                .OrderBy(o => o.FamilyKey)
                .ThenBy(o => o.Depth)
                .ThenBy(o => o.CanonicalName),
            OpeningSortOrder.LineCountDescending => filtered
                .OrderByDescending(o => o.Lines.Count)
                .ThenBy(o => o.CanonicalName),
            OpeningSortOrder.RecentlyUpdated => filtered
                .OrderByDescending(o => o.UpdatedAtUtc)
                .ThenBy(o => o.CanonicalName),
            _ => filtered.OrderBy(o => o.CanonicalName),
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new OpeningSummaryDto(
                o.Id,
                o.CanonicalName,
                o.TerminalName,
                o.FamilyKey,
                o.Depth,
                o.EcoCodeValue,
                (string?)null,
                o.Difficulty,
                o.Lines.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The ECO section name comes from the value object, not from SQL, so the wording lives in one
        // place and stays in step with EcoCode.Family.
        var enriched = items
            .Select(i => i with { EcoFamily = EcoCode.TryParse(i.EcoCode, out var code) ? code.Family : null })
            .ToList();

        return new PagedResult<OpeningSummaryDto>(enriched, total, query.Page, query.PageSize);
    }

    public async Task<OpeningDetailDto?> GetDetailAsync(int openingId, CancellationToken cancellationToken = default)
    {
        var opening = await _db.Openings
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == openingId, cancellationToken)
            .ConfigureAwait(false);

        return opening is null ? null : await BuildDetailAsync(opening, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpeningDetailDto?> GetDetailByCanonicalNameAsync(
        string canonicalName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);

        var name = canonicalName.Trim();
        var opening = await _db.Openings
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.CanonicalName == name, cancellationToken)
            .ConfigureAwait(false);

        return opening is null ? null : await BuildDetailAsync(opening, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpeningLineDto>> GetLinesEndingAtAsync(
        string positionKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionKey);

        return await _db.OpeningLines
            .AsNoTracking()
            .Where(l => l.TerminalPositionKey == positionKey)
            .OrderBy(l => l.PlyCount)
            .ThenBy(l => l.Name)
            .Select(l => new OpeningLineDto(
                l.Id,
                l.OpeningId,
                l.ParentLineId,
                l.Name,
                l.EcoCodeValue,
                l.SanText,
                l.UciText,
                l.PlyCount,
                l.TerminalPositionKey,
                l.Kind,
                l.KindOrigin,
                l.VerificationStatus,
                l.Difficulty,
                l.Victim,
                l.Rationale,
                l.Notes,
                _db.DataProvenances.Where(p => p.Id == l.ProvenanceId).Select(p => p.Provider).FirstOrDefault(),
                _db.DataProvenances.Where(p => p.Id == l.ProvenanceId).Select(p => p.License).FirstOrDefault()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpeningLineDto>> GetLinesForOpeningAsync(
        int openingId,
        CancellationToken cancellationToken = default)
    {
        return await _db.OpeningLines
            .AsNoTracking()
            .Where(l => l.OpeningId == openingId)
            .OrderBy(l => l.PlyCount)
            .ThenBy(l => l.Name)
            .Select(l => new OpeningLineDto(
                l.Id,
                l.OpeningId,
                l.ParentLineId,
                l.Name,
                l.EcoCodeValue,
                l.SanText,
                l.UciText,
                l.PlyCount,
                l.TerminalPositionKey,
                l.Kind,
                l.KindOrigin,
                l.VerificationStatus,
                l.Difficulty,
                l.Victim,
                l.Rationale,
                l.Notes,
                _db.DataProvenances.Where(p => p.Id == l.ProvenanceId).Select(p => p.Provider).FirstOrDefault(),
                _db.DataProvenances.Where(p => p.Id == l.ProvenanceId).Select(p => p.License).FirstOrDefault()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> FindNamesByPrefixAsync(
        string prefix,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var like = prefix.Trim() + "%";
        return await _db.Openings
            .AsNoTracking()
            .Where(o => EF.Functions.Like(o.CanonicalName, like))
            .OrderBy(o => o.CanonicalName)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(o => o.CanonicalName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OpeningDetailDto> BuildDetailAsync(
        Domain.Openings.Opening opening,
        CancellationToken cancellationToken)
    {
        var aliases = await _db.OpeningAliases
            .AsNoTracking()
            .Where(a => a.OpeningId == opening.Id)
            .OrderBy(a => a.Alias)
            .Select(a => a.Alias)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = await GetLinesForOpeningAsync(opening.Id, cancellationToken).ConfigureAwait(false);

        string? parentName = null;
        if (opening.ParentOpeningId is int parentId)
        {
            parentName = await _db.Openings
                .AsNoTracking()
                .Where(o => o.Id == parentId)
                .Select(o => o.CanonicalName)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var children = await _db.Openings
            .AsNoTracking()
            .Where(o => o.ParentOpeningId == opening.Id)
            .OrderBy(o => o.CanonicalName)
            .Select(o => new OpeningSummaryDto(
                o.Id, o.CanonicalName, o.TerminalName, o.FamilyKey, o.Depth,
                o.EcoCodeValue, (string?)null, o.Difficulty, o.Lines.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var summary = new OpeningSummaryDto(
            opening.Id,
            opening.CanonicalName,
            opening.TerminalName,
            opening.FamilyKey,
            opening.Depth,
            opening.EcoCodeValue,
            opening.Eco?.Family,
            opening.Difficulty,
            lines.Count);

        return new OpeningDetailDto(
            summary,
            opening.Description,
            aliases,
            lines,
            opening.ParentOpeningId,
            parentName,
            children
                .Select(c => c with { EcoFamily = EcoCode.TryParse(c.EcoCode, out var code) ? code.Family : null })
                .ToList());
    }
}

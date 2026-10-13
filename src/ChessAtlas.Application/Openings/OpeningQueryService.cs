using ChessAtlas.Application.Common;
using ChessAtlas.Domain.Chess;
using Microsoft.Extensions.Logging;

namespace ChessAtlas.Application.Openings;

/// <summary>
/// Answers the opening list and the opening explorer.
///
/// <para>
/// These sit in the application layer rather than the view models for a specific reason: both are
/// questions about chess, not about layout. The explorer in particular has to know that a position
/// key is a normalised FEN and that several move orders can share one — that is domain knowledge,
/// and putting it in a view model is how it gets duplicated and then gets inconsistent.
/// </para>
/// </summary>
public sealed class OpeningQueryService
{
    private readonly IOpeningCatalogRepository _repository;
    private readonly ILogger<OpeningQueryService> _logger;

    public OpeningQueryService(IOpeningCatalogRepository repository, ILogger<OpeningQueryService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PagedResult<OpeningSummaryDto>> SearchAsync(
        OpeningSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _repository.SearchAsync(query.Normalise(), cancellationToken);
    }

    public Task<OpeningDetailDto?> GetOpeningAsync(int openingId, CancellationToken cancellationToken = default) =>
        _repository.GetDetailAsync(openingId, cancellationToken);

    /// <summary>
    /// Builds the explorer view for a position: every named line that ends here (the transposition
    /// view), plus each legal continuation annotated with what the catalogue knows about it.
    /// </summary>
    public async Task<OpeningPositionDto> ExploreAsync(
        string fen,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        Position position;
        try
        {
            position = Position.FromFen(fen);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Explorer was asked for an invalid FEN: {Fen}", fen);
            throw;
        }

        var positionKey = position.ToPositionKey();
        var linesHere = await _repository.GetLinesEndingAtAsync(positionKey, cancellationToken).ConfigureAwait(false);

        var nextMoves = new List<ExplorerMoveDto>();
        foreach (var move in position.GenerateLegalMoves())
        {
            position.MakeMove(move);
            var childKey = position.ToPositionKey();
            position.UndoMove();

            var childLines = await _repository.GetLinesEndingAtAsync(childKey, cancellationToken).ConfigureAwait(false);

            nextMoves.Add(new ExplorerMoveDto(
                Uci: move.ToUci(),
                San: San.ToSan(position, move),
                ResultingPositionKey: childKey,
                NamedLineCount: childLines.Count,
                OpeningNames: childLines
                    .Select(l => l.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList()));
        }

        // Offer the interesting continuations first: the ones the catalogue actually has a name for.
        var ordered = nextMoves
            .OrderByDescending(m => m.NamedLineCount)
            .ThenBy(m => m.San, StringComparer.Ordinal)
            .ToList();

        return new OpeningPositionDto(
            Fen: position.ToFen(),
            PositionKey: positionKey,
            SideToMove: position.SideToMove.ToString(),
            IsCheck: position.IsInCheck,
            LinesEndingHere: linesHere,
            NextMoves: ordered);
    }
}

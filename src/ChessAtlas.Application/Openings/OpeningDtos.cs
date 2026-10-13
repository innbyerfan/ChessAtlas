using ChessAtlas.Domain.Openings;

namespace ChessAtlas.Application.Openings;

/// <summary>One row in the opening list. Deliberately small — the list can be thousands of rows.</summary>
public sealed record OpeningSummaryDto(
    int Id,
    string CanonicalName,
    string TerminalName,
    string FamilyKey,
    int Depth,
    string? EcoCode,
    string? EcoFamily,
    OpeningDifficulty Difficulty,
    int LineCount);

/// <summary>Everything the opening detail page shows, in one round trip.</summary>
public sealed record OpeningDetailDto(
    OpeningSummaryDto Summary,
    string? Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<OpeningLineDto> Lines,
    int? ParentOpeningId,
    string? ParentOpeningName,
    IReadOnlyList<OpeningSummaryDto> Children);

/// <summary>A single line, ready to display and to hand to the board.</summary>
public sealed record OpeningLineDto(
    int Id,
    int OpeningId,
    int? ParentLineId,
    string Name,
    string? EcoCode,
    string SanText,
    string UciText,
    int PlyCount,
    string TerminalPositionKey,
    OpeningLineKind Kind,
    ClassificationOrigin KindOrigin,
    VerificationStatus VerificationStatus,
    OpeningDifficulty Difficulty,
    TrapVictim Victim,
    string? Rationale,
    string? Notes,
    string? SourceProvider,
    string? SourceLicense);

/// <summary>
/// A position in the explorer: the board, plus every named line that ends here. Lines arriving by
/// different move orders appear together — that is the transposition view.
/// </summary>
public sealed record OpeningPositionDto(
    string Fen,
    string PositionKey,
    string SideToMove,
    bool IsCheck,
    IReadOnlyList<OpeningLineDto> LinesEndingHere,
    IReadOnlyList<ExplorerMoveDto> NextMoves);

/// <summary>A legal continuation from an explorer position, annotated with what is known about it.</summary>
public sealed record ExplorerMoveDto(
    string Uci,
    string San,
    string ResultingPositionKey,
    int NamedLineCount,
    IReadOnlyList<string> OpeningNames);

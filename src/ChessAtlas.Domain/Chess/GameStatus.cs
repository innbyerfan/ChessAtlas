namespace ChessAtlas.Domain.Chess;

/// <summary>Where a game stands. Everything except <see cref="InProgress"/> is terminal.</summary>
public enum GameStatus
{
    InProgress = 0,
    Checkmate = 1,
    Stalemate = 2,
    DrawByInsufficientMaterial = 3,
    DrawByFiftyMoveRule = 4,
    DrawByThreefoldRepetition = 5,
}

public static class GameStatusExtensions
{
    public static bool IsTerminal(this GameStatus status) => status != GameStatus.InProgress;

    public static bool IsDraw(this GameStatus status) => status is
        GameStatus.Stalemate
        or GameStatus.DrawByInsufficientMaterial
        or GameStatus.DrawByFiftyMoveRule
        or GameStatus.DrawByThreefoldRepetition;
}

/// <summary>The PGN result tokens: <c>1-0</c>, <c>0-1</c>, <c>1/2-1/2</c> and <c>*</c>.</summary>
public enum GameResult
{
    Unknown = 0,
    WhiteWins = 1,
    BlackWins = 2,
    Draw = 3,
}

public static class GameResultExtensions
{
    public static string ToPgnToken(this GameResult result) => result switch
    {
        GameResult.WhiteWins => "1-0",
        GameResult.BlackWins => "0-1",
        GameResult.Draw => "1/2-1/2",
        _ => "*",
    };

    public static GameResult ParsePgnToken(string? token) => (token ?? string.Empty).Trim() switch
    {
        "1-0" => GameResult.WhiteWins,
        "0-1" => GameResult.BlackWins,
        "1/2-1/2" => GameResult.Draw,
        _ => GameResult.Unknown,
    };

    public static GameResult FromStatus(GameStatus status, Side sideToMove) => status switch
    {
        GameStatus.Checkmate => sideToMove == Side.White ? GameResult.BlackWins : GameResult.WhiteWins,
        GameStatus.Stalemate or GameStatus.DrawByInsufficientMaterial or GameStatus.DrawByFiftyMoveRule
            or GameStatus.DrawByThreefoldRepetition => GameResult.Draw,
        _ => GameResult.Unknown,
    };
}

/// <summary>Raised when a caller asks the position to play something the rules forbid.</summary>
public sealed class IllegalMoveException : Exception
{
    public IllegalMoveException(Move move, string reason)
        : base($"Illegal move {move}: {reason}")
    {
        Move = move;
        Reason = reason;
    }

    public Move Move { get; }
    public string Reason { get; }
}

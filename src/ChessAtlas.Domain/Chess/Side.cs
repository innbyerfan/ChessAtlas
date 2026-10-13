namespace ChessAtlas.Domain.Chess;

/// <summary>The two sides of a chess game.</summary>
public enum Side
{
    White = 0,
    Black = 1,
}

/// <summary>Extension helpers for <see cref="Side"/>.</summary>
public static class SideExtensions
{
    public static Side Other(this Side side) => side == Side.White ? Side.Black : Side.White;

    /// <summary>1 for White, 2 for Black — matches the PGN/FEN convention.</summary>
    public static int MoveNumber(this Side side) => side == Side.White ? 1 : 2;
}

namespace ChessAtlas.Domain.Chess;

/// <summary>Castling availability, encoded the same way as the FEN <c>KQkq</c> field.</summary>
[Flags]
public enum CastlingRights
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    White = WhiteKingSide | WhiteQueenSide,
    Black = BlackKingSide | BlackQueenSide,
    All = White | Black,
}

public static class CastlingRightsExtensions
{
    public static bool Has(this CastlingRights rights, CastlingRights flag) => (rights & flag) == flag;

    public static CastlingRights ForSide(this CastlingRights rights, Side side) =>
        rights & (side == Side.White ? CastlingRights.White : CastlingRights.Black);

    public static string ToFenField(this CastlingRights rights)
    {
        if (rights == CastlingRights.None) return "-";
        return string.Concat(
            rights.Has(CastlingRights.WhiteKingSide) ? "K" : "",
            rights.Has(CastlingRights.WhiteQueenSide) ? "Q" : "",
            rights.Has(CastlingRights.BlackKingSide) ? "k" : "",
            rights.Has(CastlingRights.BlackQueenSide) ? "q" : "");
    }

    public static CastlingRights ParseFenField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field) || field == "-") return CastlingRights.None;

        var rights = CastlingRights.None;
        foreach (var c in field)
        {
            rights |= c switch
            {
                'K' => CastlingRights.WhiteKingSide,
                'Q' => CastlingRights.WhiteQueenSide,
                'k' => CastlingRights.BlackKingSide,
                'q' => CastlingRights.BlackQueenSide,
                _ => CastlingRights.None,
            };
        }

        return rights;
    }

    /// <summary>
    /// Bitmask that clears exactly the rights a move on <paramref name="square"/> can destroy:
    /// the right belonging to a rook standing there, plus both rights if a king stands there.
    /// Applied to both the origin and the destination, it also covers rook captures.
    /// </summary>
    public static CastlingRights MaskFor(Square square)
    {
        if (square.File == 4)
            return square.Rank switch
            {
                0 => CastlingRights.All & ~CastlingRights.White,
                7 => CastlingRights.All & ~CastlingRights.Black,
                _ => CastlingRights.All,
            };

        if (square.File is 0 or 7)
        {
            var flag = (square.File, square.Rank) switch
            {
                (7, 0) => CastlingRights.WhiteKingSide,
                (0, 0) => CastlingRights.WhiteQueenSide,
                (7, 7) => CastlingRights.BlackKingSide,
                (0, 7) => CastlingRights.BlackQueenSide,
                _ => CastlingRights.None,
            };
            return CastlingRights.All & ~flag;
        }

        return CastlingRights.All;
    }
}

using System.Diagnostics.CodeAnalysis;

namespace ChessAtlas.Domain.Chess;

/// <summary>The six chess piece types.</summary>
public enum PieceType
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6,
}

/// <summary>A piece is a type owned by a side. Empty squares are modelled as <c>null</c>, not as a piece.</summary>
public readonly struct Piece : IEquatable<Piece>
{
    public Piece(Side side, PieceType type)
    {
        if (type is PieceType.None or < PieceType.Pawn or > PieceType.King)
            throw new ArgumentOutOfRangeException(nameof(type), "A piece must have a concrete type.");
        Side = side;
        Type = type;
    }

    public Side Side { get; }
    public PieceType Type { get; }

    public static Piece White(PieceType type) => new(Side.White, type);
    public static Piece Black(PieceType type) => new(Side.Black, type);
    public static Piece Of(Side side, PieceType type) => new(side, type);

    /// <summary>FEN character, e.g. <c>P</c> / <c>k</c>.</summary>
    public char ToFenChar()
    {
        var c = Type switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            PieceType.King => 'k',
            _ => throw new InvalidOperationException($"Cannot serialise {Type}."),
        };
        return Side == Side.White ? char.ToUpperInvariant(c) : c;
    }

    public static bool TryParseFenChar(char c, out Piece piece)
    {
        var side = char.IsUpper(c) ? Side.White : Side.Black;
        var type = char.ToLowerInvariant(c) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => PieceType.None,
        };
        if (type == PieceType.None)
        {
            piece = default;
            return false;
        }

        piece = new Piece(side, type);
        return true;
    }

    /// <summary>Standard ASCII glyph used for plain-text board dumps in tests and logs.</summary>
    public char ToAsciiGlyph() => ToFenChar();

    public bool Equals(Piece other) => Side == other.Side && Type == other.Type;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Piece other && Equals(other);
    public override int GetHashCode() => ((int)Side << 8) | (int)Type;
    public override string ToString() => ToFenChar().ToString();

    public static bool operator ==(Piece left, Piece right) => left.Equals(right);
    public static bool operator !=(Piece left, Piece right) => !left.Equals(right);
}

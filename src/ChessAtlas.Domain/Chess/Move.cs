using System.Diagnostics.CodeAnalysis;

namespace ChessAtlas.Domain.Chess;

/// <summary>
/// A chess move. Deliberately minimal: origin, destination and (for promotions) the promoted
/// piece type. Everything else — capture, castling, en passant, double push — is a property of
/// the <see cref="Position"/> the move is played in, never of the move itself. That keeps a move
/// from ever disagreeing with the board it belongs to.
/// </summary>
public readonly struct Move : IEquatable<Move>
{
    public static readonly Move Null = default;

    public Move(Square from, Square to, PieceType promotion = PieceType.None)
    {
        From = from;
        To = to;
        Promotion = promotion;
    }

    public Square From { get; }
    public Square To { get; }

    /// <summary>Non-<see cref="PieceType.None"/> only for pawn promotions.</summary>
    public PieceType Promotion { get; }

    public bool IsPromotion => Promotion != PieceType.None;
    public bool IsNull => From.Index == 0 && To.Index == 0 && Promotion == PieceType.None;

    /// <summary>Long algebraic notation, e.g. <c>e2e4</c> or <c>e7e8q</c>. This is the UCI wire format.</summary>
    public string ToUci() => Promotion == PieceType.None
        ? $"{From}{To}"
        : $"{From}{To}{PromotionChar(Promotion)}";

    public static bool TryParseUci(string? text, out Move move)
    {
        move = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.AsSpan().Trim();
        if (span.Length is not (4 or 5)) return false;
        if (!Square.TryParse(span[..2], out var from)) return false;
        if (!Square.TryParse(span[2..4], out var to)) return false;
        if (from == to) return false;

        var promotion = PieceType.None;
        if (span.Length == 5)
        {
            promotion = char.ToLowerInvariant(span[4]) switch
            {
                'q' => PieceType.Queen,
                'r' => PieceType.Rook,
                'b' => PieceType.Bishop,
                'n' => PieceType.Knight,
                _ => PieceType.None,
            };
            if (promotion == PieceType.None) return false;
        }

        move = new Move(from, to, promotion);
        return true;
    }

    public static Move ParseUci(string text) =>
        TryParseUci(text, out var move) ? move : throw new FormatException($"'{text}' is not a valid UCI move.");

    private static char PromotionChar(PieceType type) => type switch
    {
        PieceType.Queen => 'q',
        PieceType.Rook => 'r',
        PieceType.Bishop => 'b',
        PieceType.Knight => 'n',
        _ => throw new InvalidOperationException($"'{type}' cannot be promoted to."),
    };

    public override string ToString() => ToUci();

    public bool Equals(Move other) => From == other.From && To == other.To && Promotion == other.Promotion;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Move other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(From.Index, To.Index, Promotion);

    public static bool operator ==(Move left, Move right) => left.Equals(right);
    public static bool operator !=(Move left, Move right) => !left.Equals(right);
}

using System.Text;

namespace ChessAtlas.Domain.Chess;

/// <summary>
/// Standard Algebraic Notation, generated and parsed against a concrete <see cref="Position"/>.
/// SAN is inherently ambiguous without the position — "Nbd2" only names a move once you know which
/// knights can reach d2 — so both directions take the position they apply to.
/// </summary>
public static class San
{
    /// <summary>
    /// Renders <paramref name="move"/> in SAN, including the disambiguation the position requires
    /// and a trailing <c>+</c> / <c>#</c> when the move gives check or mate.
    /// </summary>
    public static string ToSan(Position position, Move move)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!position.IsLegal(move)) throw new IllegalMoveException(move, "SAN is only defined for legal moves.");

        var core = ToCore(position, move);

        position.MakeMove(move);
        var givesCheck = position.IsInCheck;
        var hasReply = givesCheck && position.GenerateLegalMoves().Count > 0;
        position.UndoMove();

        return givesCheck ? core + (hasReply ? "+" : "#") : core;
    }

    /// <summary>
    /// Resolves SAN text to a move. Rather than hand-parsing every dialect, each legal move is
    /// rendered and compared after normalisation — which makes parsing and rendering round-trip by
    /// construction and tolerates <c>0-0</c>, missing <c>x</c>, missing <c>=</c> and stray
    /// annotation marks.
    /// </summary>
    public static bool TryParse(Position position, string? san, out Move move)
    {
        move = default;
        ArgumentNullException.ThrowIfNull(position);
        if (string.IsNullOrWhiteSpace(san)) return false;

        var target = Normalise(san);
        if (target.Length == 0) return false;

        foreach (var candidate in position.GenerateLegalMoves())
        {
            if (Normalise(ToCore(position, candidate)) == target ||
                Normalise(ToSan(position, candidate)) == target)
            {
                move = candidate;
                return true;
            }
        }

        return false;
    }

    public static Move Parse(Position position, string san) =>
        TryParse(position, san, out var move)
            ? move
            : throw new FormatException($"'{san}' is not a legal move in the position {position.ToFen()}.");

    private static string ToCore(Position position, Move move)
    {
        var piece = position.At(move.From) ?? throw new InvalidOperationException("Origin square is empty.");
        var isCapture = position.IsOccupied(move.To) ||
                        (piece.Type == PieceType.Pawn && move.From.File != move.To.File);
        var isCastling = piece.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2;

        if (isCastling) return move.To.File == 6 ? "O-O" : "O-O-O";

        var sb = new StringBuilder(8);

        if (piece.Type == PieceType.Pawn)
        {
            if (isCapture)
            {
                sb.Append((char)('a' + move.From.File));
                sb.Append('x');
            }

            sb.Append(move.To);
            if (move.IsPromotion)
            {
                sb.Append('=');
                sb.Append(PieceLetter(move.Promotion));
            }

            return sb.ToString();
        }

        sb.Append(PieceLetter(piece.Type));
        sb.Append(Disambiguation(position, move));
        if (isCapture) sb.Append('x');
        sb.Append(move.To);
        return sb.ToString();
    }

    private static string Disambiguation(Position position, Move move)
    {
        var piece = position.At(move.From)!.Value;
        var rivals = new List<Move>();

        foreach (var candidate in position.GenerateLegalMoves())
        {
            if (candidate.To != move.To || candidate.From == move.From) continue;
            if (position.At(candidate.From) is Piece other && other.Type == piece.Type && other.Side == piece.Side)
                rivals.Add(candidate);
        }

        if (rivals.Count == 0) return string.Empty;

        var sameFile = rivals.Any(r => r.From.File == move.From.File);
        var sameRank = rivals.Any(r => r.From.Rank == move.From.Rank);

        if (!sameFile) return ((char)('a' + move.From.File)).ToString();
        if (!sameRank) return ((char)('1' + move.From.Rank)).ToString();
        return move.From.ToString();
    }

    private static char PieceLetter(PieceType type) => type switch
    {
        PieceType.King => 'K',
        PieceType.Queen => 'Q',
        PieceType.Rook => 'R',
        PieceType.Bishop => 'B',
        PieceType.Knight => 'N',
        _ => throw new InvalidOperationException($"'{type}' has no SAN letter."),
    };

    private static string Normalise(string san)
    {
        var sb = new StringBuilder(san.Length);
        foreach (var c in san.Trim())
        {
            switch (c)
            {
                case 'x' or 'X' or '-' or '=' or '+' or '#' or '!' or '?' or ' ':
                    continue;
                case '0':
                    sb.Append('O');
                    break;
                default:
                    sb.Append(char.ToUpperInvariant(c));
                    break;
            }
        }

        return sb.ToString();
    }
}

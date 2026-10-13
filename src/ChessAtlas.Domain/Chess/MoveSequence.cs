using System.Text;

namespace ChessAtlas.Domain.Chess;

/// <summary>
/// An ordered sequence of moves anchored to a starting position.
///
/// <para>
/// A line is never stored as loose text. It is built by replaying SAN or UCI through a real
/// <see cref="Position"/>, so constructing one <em>is</em> validating it: an illegal move, a
/// transposition typo or a truncated variation fails at parse time with the ply that broke, not
/// later at render time. Both notations are then available on demand, plus the terminal position
/// and its <see cref="Position.ToPositionKey"/> for transposition lookups.
/// </para>
/// </summary>
public sealed class MoveSequence : IEquatable<MoveSequence>
{
    private readonly IReadOnlyList<Move> _moves;

    private MoveSequence(IReadOnlyList<Move> moves, string startingFen, Position terminal)
    {
        _moves = moves;
        StartingFen = startingFen;
        TerminalPosition = terminal;
    }

    public string StartingFen { get; }
    public Position TerminalPosition { get; }
    public int PlyCount => _moves.Count;
    public IReadOnlyList<Move> Moves => _moves;
    public Move LastMove => _moves.Count == 0 ? Move.Null : _moves[^1];

    /// <summary>Transposition key of the position this line ends in.</summary>
    public string TerminalPositionKey => TerminalPosition.ToPositionKey();

    public static MoveSequence Empty(string? startingFen = null)
    {
        var fen = startingFen ?? Position.StandardFen;
        return new MoveSequence([], fen, Position.FromFen(fen));
    }

    /// <summary>Builds a line from SAN move text such as <c>1. e4 e5 2. Nf3 Nc6</c>.</summary>
    public static MoveSequence FromSan(string sanText, string? startingFen = null)
    {
        ArgumentException.ThrowIfNull(sanText);

        var fen = startingFen ?? Position.StandardFen;
        var position = Position.FromFen(fen);
        var moves = new List<Move>();
        var ply = 0;

        foreach (var token in Tokenise(sanText))
        {
            if (IsMoveNumber(token) || IsResultToken(token)) continue;
            if (IsComment(token)) continue;

            if (!San.TryParse(position, token, out var move))
                throw new FormatException(
                    $"Illegal or unresolvable move '{token}' at ply {ply + 1} of the line '{sanText.Trim()}'.");

            moves.Add(move);
            position.MakeMove(move);
            ply++;
        }

        return new MoveSequence(moves, fen, position);
    }

    /// <summary>Builds a line from UCI move text such as <c>e2e4 e7e5 g1f3</c>.</summary>
    public static MoveSequence FromUci(string uciText, string? startingFen = null)
    {
        ArgumentException.ThrowIfNull(uciText);

        var fen = startingFen ?? Position.StandardFen;
        var position = Position.FromFen(fen);
        var moves = new List<Move>();
        var ply = 0;

        foreach (var raw in uciText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Move.TryParseUci(raw, out var move))
                throw new FormatException($"'{raw}' is not a valid UCI move (ply {ply + 1}).");
            if (!position.IsLegal(move))
                throw new FormatException($"Illegal move '{raw}' at ply {ply + 1} of the line.");

            moves.Add(move);
            position.MakeMove(move);
            ply++;
        }

        return new MoveSequence(moves, fen, position);
    }

    /// <summary>Renders the line with move numbers, e.g. <c>1. e4 e5 2. Nf3</c>.</summary>
    public string ToSanText()
    {
        if (_moves.Count == 0) return string.Empty;

        var position = Position.FromFen(StartingFen);
        var sb = new StringBuilder();
        var moveNumber = ExtractFullmoveNumber(StartingFen);
        var side = position.SideToMove;

        for (var i = 0; i < _moves.Count; i++)
        {
            if (side == Side.White)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(moveNumber).Append('.');
            }
            else if (i == 0)
            {
                sb.Append(moveNumber).Append("...");
            }

            sb.Append(' ').Append(San.ToSan(position, _moves[i]));
            position.MakeMove(_moves[i]);

            if (side == Side.Black) moveNumber++;
            side = side.Other();
        }

        return sb.ToString();
    }

    /// <summary>Renders the line in the UCI wire format, e.g. <c>e2e4 e7e5 g1f3</c>.</summary>
    public string ToUciText() => string.Join(' ', _moves.Select(m => m.ToUci()));

    /// <summary>Replays the first <paramref name="plyCount"/> half-moves.</summary>
    public MoveSequence Take(int plyCount)
    {
        if (plyCount < 0 || plyCount > _moves.Count)
            throw new ArgumentOutOfRangeException(nameof(plyCount));

        var position = Position.FromFen(StartingFen);
        var taken = new Move[plyCount];
        for (var i = 0; i < plyCount; i++)
        {
            taken[i] = _moves[i];
            position.MakeMove(taken[i]);
        }

        return new MoveSequence(taken, StartingFen, position);
    }

    public bool StartsWith(MoveSequence other)
    {
        if (other._moves.Count > _moves.Count) return false;
        if (!string.Equals(StartingFen, other.StartingFen, StringComparison.Ordinal)) return false;
        for (var i = 0; i < other._moves.Count; i++)
            if (_moves[i] != other._moves[i]) return false;
        return true;
    }

    private static int ExtractFullmoveNumber(string fen)
    {
        var fields = fen.Split(' ');
        return fields.Length >= 6 && int.TryParse(fields[5], out var full) ? Math.Max(1, full) : 1;
    }

    private static IEnumerable<string> Tokenise(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (text[i] == '{')
            {
                var end = text.IndexOf('}', i + 1);
                var stop = end < 0 ? text.Length : end + 1;
                yield return text[i..stop];
                i = stop;
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{') i++;
            yield return text[start..i];
        }
    }

    private static bool IsComment(string token) => token.StartsWith('{');

    private static bool IsMoveNumber(string token)
    {
        var digits = 0;
        foreach (var c in token)
        {
            if (char.IsDigit(c))
            {
                digits++;
                continue;
            }

            if (c is '.' or '…') continue;
            return false;
        }

        return digits > 0;
    }

    private static bool IsResultToken(string token) =>
        token is "1-0" or "0-1" or "1/2-1/2" or "*";

    public bool Equals(MoveSequence? other) =>
        other is not null &&
        string.Equals(StartingFen, other.StartingFen, StringComparison.Ordinal) &&
        _moves.SequenceEqual(other._moves);

    public override bool Equals(object? obj) => Equals(obj as MoveSequence);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StartingFen, StringComparer.Ordinal);
        foreach (var move in _moves) hash.Add(move);
        return hash.ToHashCode();
    }

    public override string ToString() => ToSanText();
}

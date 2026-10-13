using System.Text;

namespace ChessAtlas.Domain.Chess;

/// <summary>
/// Writes a <see cref="PgnGame"/> back to PGN text: seven tag roster first, comments preserved,
/// variations nested in parentheses, output wrapped at a fixed column so diffs stay readable.
/// </summary>
public static class PgnWriter
{
    public static string Write(PgnGame game, int maxLineLength = 80)
    {
        ArgumentNullException.ThrowIfNull(game);

        var sb = new StringBuilder(1024);

        foreach (var key in game.OrderedTagKeys())
            sb.Append('[').Append(key).Append(" \"").Append(EscapeTagValue(game.GetTag(key) ?? string.Empty))
              .Append("\"]\n");

        sb.Append('\n');

        var body = new StringBuilder(1024);
        var emitter = new Emitter(body, maxLineLength);

        var startingFen = game.StartingFen;
        var startSide = Side.White;
        var startMoveNumber = 1;
        if (startingFen is { Length: > 0 } fen)
        {
            var fields = fen.Trim().Split(' ');
            if (fields.Length >= 6)
            {
                startSide = fields[1].Equals("b", StringComparison.OrdinalIgnoreCase) ? Side.Black : Side.White;
                if (int.TryParse(fields[5], out var full)) startMoveNumber = full;
            }
        }

        WriteLine(game.Moves, emitter, startSide, startMoveNumber);
        emitter.Append(game.Result.ToPgnToken());
        emitter.Flush();

        sb.Append(body);
        return sb.ToString();
    }

    private static void WriteLine(
        IReadOnlyList<PgnMove> line,
        Emitter emitter,
        Side firstSideToMove,
        int firstMoveNumber)
    {
        var moveNumber = firstMoveNumber;
        var side = firstSideToMove;
        var needsBlackNumber = true; // the first black move in a fresh line always shows its number

        foreach (var node in line)
        {
            if (node.CommentBefore is { Length: > 0 } before)
            {
                emitter.Append($"{{{before}}}");
                needsBlackNumber = true;
            }

            if (side == Side.White)
            {
                emitter.Append($"{moveNumber}.");
                needsBlackNumber = false;
            }
            else if (needsBlackNumber)
            {
                emitter.Append($"{moveNumber}...");
            }

            emitter.Append(node.San);
            foreach (var nag in node.Nags) emitter.Append($"${nag}");
            if (node.CommentAfter is { Length: > 0 } after)
            {
                emitter.Append($"{{{after}}}");
                needsBlackNumber = true;
            }

            foreach (var variation in node.Variations)
            {
                emitter.Append("(");
                WriteLine(variation, emitter, side, moveNumber);
                emitter.Append(")");
                needsBlackNumber = true;
            }

            if (side == Side.Black) moveNumber++;
            side = side.Other();
            if (side == Side.White) needsBlackNumber = false;
        }
    }

    private static string EscapeTagValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private readonly struct Emitter
    {
        private readonly StringBuilder _sb;
        private readonly int _max;
        private int _column;

        internal Emitter(StringBuilder sb, int maxLineLength)
        {
            _sb = sb;
            _max = maxLineLength;
            _column = 0;
        }

        internal void Append(string token)
        {
            if (_column == 0)
            {
                _sb.Append(token);
                _column = token.Length;
                return;
            }

            const string separator = " ";
            if (_column + separator.Length + token.Length > _max)
            {
                _sb.Append('\n');
                _sb.Append(token);
                _column = token.Length;
            }
            else
            {
                _sb.Append(separator);
                _sb.Append(token);
                _column += separator.Length + token.Length;
            }
        }

        internal void Flush()
        {
            _sb.Append('\n');
            _column = 0;
        }
    }
}

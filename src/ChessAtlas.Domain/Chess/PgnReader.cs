using System.Text;

namespace ChessAtlas.Domain.Chess;

/// <summary>
/// Streaming PGN reader.
///
/// <para>
/// Multi-million game archives must never be materialised in memory, so the reader is an
/// <see cref="IEnumerable{T}"/> over <see cref="PgnGame"/> that parses one game at a time from a
/// <see cref="TextReader"/>. Enumerating it lazily keeps peak memory at roughly one game regardless
/// of file size; the caller decides when to stop.
/// </para>
///
/// <para>
/// It is deliberately forgiving. Real-world PGN is messy: unresolvable SAN, missing results,
/// comments spanning lines, nested variations of uneven depth. A game that cannot be parsed is
/// reported with <see cref="PgnGame.HasErrors"/> and its warnings rather than aborting the whole
/// file — an importer that dies on game 4,000,000 of a 9 GB archive is worse than one that skips it.
/// </para>
/// </summary>
public static class PgnReader
{
    private enum TokenKind
    {
        San,
        Comment,
        Nag,
        OpenParen,
        CloseParen,
        Result,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Nag);

    public static IEnumerable<PgnGame> Read(string pgnText)
    {
        using var reader = new StringReader(pgnText);
        foreach (var game in Read(reader)) yield return game;
    }

    public static IEnumerable<PgnGame> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var tagBuffer = new List<(string Key, string Value)>();
        var moveBuffer = new StringBuilder();
        var inMoveText = false;
        var hasContent = false;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (IsTagPairLine(line))
            {
                if (inMoveText)
                {
                    // A tag pair after move text means the previous game ended without a result.
                    foreach (var game in Finish(tagBuffer, moveBuffer)) yield return game;
                    tagBuffer.Clear();
                    moveBuffer.Clear();
                    inMoveText = false;
                    hasContent = false;
                }

                tagBuffer.AddRange(ParseTagPairs(line));
                hasContent = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (tagBuffer.Count > 0 && !inMoveText) inMoveText = true;
                continue;
            }

            inMoveText = true;
            hasContent = true;
            moveBuffer.Append(line).Append('\n');

            if (EndsWithResult(moveBuffer))
            {
                foreach (var game in Finish(tagBuffer, moveBuffer)) yield return game;
                tagBuffer.Clear();
                moveBuffer.Clear();
                inMoveText = false;
                hasContent = false;
            }
        }

        if (hasContent || tagBuffer.Count > 0 || moveBuffer.Length > 0)
            foreach (var game in Finish(tagBuffer, moveBuffer)) yield return game;
    }

    /// <summary>Reads a whole file, one game at a time. The stream is closed when enumeration ends.</summary>
    public static IEnumerable<PgnGame> ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        foreach (var game in Read(reader)) yield return game;
    }

    private static IEnumerable<PgnGame> Finish(
        List<(string Key, string Value)> tags,
        StringBuilder moveText)
    {
        if (tags.Count == 0 && moveText.Length == 0) yield break;

        var game = new PgnGame();
        foreach (var (key, value) in tags) game.SetTag(key, value);

        ParseMoveText(game, moveText.ToString());
        yield return game;
    }

    private static void ParseMoveText(PgnGame game, string moveText)
    {
        var tokens = Tokenise(moveText, game);
        if (tokens.Count == 0)
        {
            game.Moves = [];
            return;
        }

        var position = game.StartingFen is { Length: > 0 } fen
            ? TryBuildStart(fen, game)
            : Position.StartPosition;

        if (position is null)
        {
            game.HasErrors = true;
            game.Moves = [];
            return;
        }

        var index = 0;
        var moves = ParseLine(tokens, ref index, position, game, startingPly: position.PlyCount, inVariation: false);
        game.Moves = moves;
    }

    private static Position? TryBuildStart(string fen, PgnGame game)
    {
        try
        {
            return Position.FromFen(fen);
        }
        catch (FormatException ex)
        {
            game.ParseWarnings.Add($"Unusable FEN tag '{fen}': {ex.Message}");
            return null;
        }
    }

    private static List<PgnMove> ParseLine(
        List<Token> tokens,
        ref int index,
        Position position,
        PgnGame game,
        int startingPly,
        bool inVariation)
    {
        var line = new List<PgnMove>();
        string? pendingComment = null;
        var ply = startingPly;

        while (index < tokens.Count)
        {
            var token = tokens[index];

            switch (token.Kind)
            {
                case TokenKind.CloseParen:
                    if (!inVariation)
                    {
                        game.ParseWarnings.Add("Unbalanced ')' in move text; ignored.");
                        index++;
                        continue;
                    }

                    return line;

                case TokenKind.Result:
                    index++;
                    return line;

                case TokenKind.Comment:
                    pendingComment = pendingComment is null
                        ? token.Text
                        : pendingComment + " " + token.Text;
                    index++;
                    continue;

                case TokenKind.Nag:
                    if (line.Count > 0) line[^1].Nags.Add(token.Nag);
                    else game.ParseWarnings.Add($"NAG ${token.Nag} appears before any move; ignored.");
                    index++;
                    continue;

                case TokenKind.OpenParen:
                {
                    index++;
                    var variationPosition = position.Clone();
                    var variation = ParseLine(
                        tokens, ref index, variationPosition, game, startingPly: ply, inVariation: true);
                    if (index < tokens.Count && tokens[index].Kind == TokenKind.CloseParen) index++;

                    if (line.Count > 0) line[^1].Variations.Add(variation);
                    else game.ParseWarnings.Add("A variation appears before any move; ignored.");
                    continue;
                }

                case TokenKind.San:
                {
                    if (!San.TryParse(position, token.Text, out var move))
                    {
                        game.ParseWarnings.Add(
                            $"Could not resolve '{token.Text}' at ply {ply + 1}; the rest of this line was skipped.");
                        game.HasErrors = true;
                        return line;
                    }

                    var node = new PgnMove
                    {
                        SourceSan = token.Text,
                        San = San.ToSan(position, move),
                        Move = move,
                        PlyNumber = ply + 1,
                        CommentBefore = pendingComment,
                    };
                    pendingComment = null;
                    index++;

                    var trailing = new List<string>();
                    while (index < tokens.Count && tokens[index].Kind is TokenKind.Comment or TokenKind.Nag)
                    {
                        if (tokens[index].Kind == TokenKind.Comment) trailing.Add(tokens[index].Text);
                        else node.Nags.Add(tokens[index].Nag);
                        index++;
                    }

                    if (trailing.Count > 0) node.CommentAfter = string.Join(" ", trailing);

                    while (index < tokens.Count && tokens[index].Kind == TokenKind.OpenParen)
                    {
                        index++;
                        var variationPosition = position.Clone();
                        var variation = ParseLine(
                            tokens, ref index, variationPosition, game, startingPly: ply, inVariation: true);
                        if (index < tokens.Count && tokens[index].Kind == TokenKind.CloseParen) index++;
                        node.Variations.Add(variation);
                    }

                    position.MakeMove(move);
                    ply++;
                    line.Add(node);
                    continue;
                }

                default:
                    index++;
                    continue;
            }
        }

        if (inVariation) game.ParseWarnings.Add("A variation was never closed; treated as ending here.");
        return line;
    }

    private static List<Token> Tokenise(string moveText, PgnGame game)
    {
        var tokens = new List<Token>();
        var i = 0;
        var span = moveText.AsSpan();

        while (i < span.Length)
        {
            var c = span[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '{')
            {
                var end = moveText.IndexOf('}', i + 1);
                if (end < 0)
                {
                    game.ParseWarnings.Add("Unterminated '{' comment; the remainder was read as a comment.");
                    tokens.Add(new Token(TokenKind.Comment, moveText[(i + 1)..].Trim(), 0));
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.Comment, moveText[(i + 1)..end].Trim(), 0));
                i = end + 1;
                continue;
            }

            if (c == ';')
            {
                var end = moveText.IndexOf('\n', i + 1);
                if (end < 0) return tokens;
                i = end + 1;
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new Token(TokenKind.OpenParen, "(", 0));
                i++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new Token(TokenKind.CloseParen, ")", 0));
                i++;
                continue;
            }

            if (c == '$')
            {
                var start = i + 1;
                var cursor = start;
                while (cursor < span.Length && char.IsDigit(span[cursor])) cursor++;
                if (cursor > start && int.TryParse(moveText[start..cursor], out var nag))
                    tokens.Add(new Token(TokenKind.Nag, $"${nag}", nag));
                i = cursor;
                continue;
            }

            // A plain word: a SAN move, a move number, or a result token.
            var wordStart = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i]) && span[i] is not ('{' or '(' or ')' or ';'))
                i++;

            var word = moveText[wordStart..i];
            if (IsMoveNumber(word) || word.Length == 0) continue;

            if (IsResultToken(word))
            {
                tokens.Add(new Token(TokenKind.Result, word, 0));
                continue;
            }

            tokens.Add(new Token(TokenKind.San, word, 0));
        }

        return tokens;
    }

    private static bool IsMoveNumber(string word)
    {
        var digits = 0;
        foreach (var c in word)
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

    private static bool IsResultToken(string word) =>
        word is "1-0" or "0-1" or "1/2-1/2" or "*";

    private static bool IsTagPairLine(string line)
    {
        var trimmed = line.AsSpan().TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '[';
    }

    /// <summary>
    /// Extracts every tag pair on a line.
    ///
    /// <para>
    /// Scanned by hand rather than split on whitespace, for two reasons that both occur in real
    /// files: some exports put several tag pairs on one line, and a value may contain an escaped
    /// quote. Splitting on the first space swallows the rest of the line into the first tag;
    /// splitting on the last quote merges two tags into one value.
    /// </para>
    /// </summary>
    private static List<(string Key, string Value)> ParseTagPairs(string line)
    {
        var result = new List<(string, string)>();
        var i = 0;

        while (i < line.Length)
        {
            if (line[i] != '[')
            {
                i++;
                continue;
            }

            var keyStart = i + 1;
            var cursor = keyStart;
            while (cursor < line.Length && !char.IsWhiteSpace(line[cursor]) && line[cursor] != '"') cursor++;

            var key = line[keyStart..cursor].Trim();
            if (key.Length == 0)
            {
                i++;
                continue;
            }

            var quoteOpen = line.IndexOf('"', cursor);
            if (quoteOpen < 0)
            {
                i++;
                continue;
            }

            var value = new StringBuilder();
            var j = quoteOpen + 1;
            var closed = false;
            while (j < line.Length)
            {
                var c = line[j];
                if (c == '\\' && j + 1 < line.Length)
                {
                    value.Append(line[j + 1]);
                    j += 2;
                    continue;
                }

                if (c == '"')
                {
                    closed = true;
                    j++;
                    break;
                }

                value.Append(c);
                j++;
            }

            if (!closed)
            {
                i++;
                continue;
            }

            result.Add((key, value.ToString()));
            i = j;
        }

        return result;
    }
    private static bool EndsWithResult(StringBuilder moveText)
    {
        // Cheap guard before tokenising: only a trailing result token ends a game.
        var text = moveText.ToString().TrimEnd();
        return text.EndsWith("1-0", StringComparison.Ordinal)
               || text.EndsWith("0-1", StringComparison.Ordinal)
               || text.EndsWith("1/2-1/2", StringComparison.Ordinal)
               || text.EndsWith("*", StringComparison.Ordinal);
    }
}

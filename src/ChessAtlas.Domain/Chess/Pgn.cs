using System.Text;

namespace ChessAtlas.Domain.Chess;

/// <summary>One move in a PGN game, with the annotations that hang off it.</summary>
public sealed class PgnMove
{
    /// <summary>The move as written in the source file, verbatim.</summary>
    public required string SourceSan { get; init; }

    /// <summary>The move after it was resolved against the position — canonical SAN.</summary>
    public required string San { get; init; }

    public required Move Move { get; init; }

    /// <summary>1-based half-move number counted from the start of the game.</summary>
    public required int PlyNumber { get; init; }

    public string? CommentBefore { get; set; }
    public string? CommentAfter { get; set; }

    /// <summary>Numeric Annotation Glyphs, e.g. <c>1</c> for <c>$1</c> ("!").</summary>
    public List<int> Nags { get; } = [];

    /// <summary>
    /// Alternative continuations. Each variation starts from the position <em>before</em> this move
    /// and is itself a full line, which may nest further variations.
    /// </summary>
    public List<List<PgnMove>> Variations { get; } = [];
}

/// <summary>A parsed PGN game: tag pairs, a move tree and any problems found on the way.</summary>
public sealed class PgnGame
{
    /// <summary>The seven tag roster keys, in canonical order, followed by any extras.</summary>
    public static readonly string[] SevenTagRoster = ["Event", "Site", "Date", "Round", "White", "Black", "Result"];

    private readonly Dictionary<string, string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _tagOrder = [];

    public IReadOnlyList<PgnMove> Moves { get; internal set; } = [];

    /// <summary>Problems that did not stop the parse — truncated games, unresolvable SAN, and so on.</summary>
    public List<string> ParseWarnings { get; } = [];

    /// <summary>True when the game could not be parsed into a usable move list.</summary>
    public bool HasErrors { get; internal set; }

    public IReadOnlyDictionary<string, string> Tags => _tags;

    public string? GetTag(string key) => _tags.TryGetValue(key, out var value) ? value : null;

    public void SetTag(string key, string value)
    {
        if (!_tags.ContainsKey(key)) _tagOrder.Add(key);
        _tags[key] = value;
    }

    public string Event => GetTag("Event") ?? "?";
    public string Site => GetTag("Site") ?? "?";
    public string Date => GetTag("Date") ?? "?";
    public string Round => GetTag("Round") ?? "?";
    public string WhitePlayer => GetTag("White") ?? "?";
    public string BlackPlayer => GetTag("Black") ?? "?";

    public GameResult Result
    {
        get => GameResultExtensions.ParsePgnToken(GetTag("Result"));
        set => SetTag("Result", value.ToPgnToken());
    }

    public string? StartingFen => GetTag("FEN");

    public string? EcoCode => GetTag("ECO");
    public string? TimeControl => GetTag("TimeControl");
    public string? WhiteElo => GetTag("WhiteElo");
    public string? BlackElo => GetTag("BlackElo");

    /// <summary>Tag keys in the order they should be written out: seven tag roster first.</summary>
    public IEnumerable<string> OrderedTagKeys()
    {
        foreach (var key in SevenTagRoster)
            if (_tags.ContainsKey(key)) yield return key;
        foreach (var key in _tagOrder)
            if (!SevenTagRoster.Contains(key, StringComparer.OrdinalIgnoreCase)) yield return key;
    }

    /// <summary>Flattens the move tree into the main line only.</summary>
    public IEnumerable<PgnMove> MainLine()
    {
        foreach (var move in Moves) yield return move;
    }

    /// <summary>Every move in the tree, including all nested variations, in depth-first order.</summary>
    public IEnumerable<PgnMove> AllMoves()
    {
        IEnumerable<PgnMove> Walk(IEnumerable<PgnMove> line)
        {
            foreach (var move in line)
            {
                yield return move;
                foreach (var variation in move.Variations)
                    foreach (var nested in Walk(variation))
                        yield return nested;
            }
        }

        return Walk(Moves);
    }

    /// <summary>Replays the main line and returns the resulting position.</summary>
    public Position Replay()
    {
        var position = StartingFen is { Length: > 0 } fen ? Position.FromFen(fen) : Position.StartPosition;
        foreach (var move in Moves) position.MakeMove(move.Move);
        return position;
    }

    public override string ToString() =>
        $"{WhitePlayer} vs {BlackPlayer}, {Event} {Date} [{Result.ToPgnToken()}]";
}

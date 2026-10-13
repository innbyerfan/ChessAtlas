using ChessAtlas.Domain.Chess;
using Xunit;

namespace ChessAtlas.Domain.Tests.Chess;

public sealed class PgnTests
{
    private const string SimpleGame = """
        [Event "Test"]
        [Site "Internet"]
        [Date "2026.10.13"]
        [Round "1"]
        [White "Atlas"]
        [Black "Rival"]
        [Result "1-0"]

        1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0
        """;

    [Fact]
    public void Tags_and_moves_are_parsed()
    {
        var game = Assert.Single(PgnReader.Read(SimpleGame));

        Assert.Equal("Test", game.Event);
        Assert.Equal("Atlas", game.WhitePlayer);
        Assert.Equal("Rival", game.BlackPlayer);
        Assert.Equal("2026.10.13", game.Date);
        Assert.Equal(GameResult.WhiteWins, game.Result);
        Assert.False(game.HasErrors);
        Assert.Equal(6, game.Moves.Count);
        Assert.Equal("e4", game.Moves[0].San);
        Assert.Equal("a6", game.Moves[5].San);
    }

    [Fact]
    public void Move_numbers_are_recorded_as_ply_numbers()
    {
        var game = Assert.Single(PgnReader.Read(SimpleGame));

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, game.Moves.Select(m => m.PlyNumber));
    }

    [Fact]
    public void Replay_produces_the_position_the_moves_describe()
    {
        var game = Assert.Single(PgnReader.Read(SimpleGame));
        var position = game.Replay();

        Assert.Equal("r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4", position.ToFen());
    }

    [Fact]
    public void Comments_before_and_after_a_move_are_kept_separately()
    {
        const string pgn = """
            [Event "c"] [Result "*"]

            1. e4 {the best move} e5 {solid} 2. Nf3 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.Equal("the best move", game.Moves[0].CommentAfter);
        Assert.Equal("solid", game.Moves[1].CommentAfter);
        Assert.Null(game.Moves[2].CommentAfter);
    }

    [Fact]
    public void A_comment_before_the_first_move_is_not_lost()
    {
        const string pgn = """
            [Result "*"]

            {A game about the Ruy Lopez.} 1. e4 e5 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.Equal("A game about the Ruy Lopez.", game.Moves[0].CommentBefore);
    }

    [Fact]
    public void Nags_are_attached_to_the_move_they_follow()
    {
        const string pgn = """
            [Result "*"]

            1. e4 $1 e5 $2 2. Nf3 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.Contains(1, game.Moves[0].Nags);
        Assert.Contains(2, game.Moves[1].Nags);
    }

    [Fact]
    public void Variations_are_nested_and_do_not_disturb_the_main_line()
    {
        const string pgn = """
            [Result "*"]

            1. e4 e5 2. Nf3 (2. Nc3 Nf6 3. Bc4 (3. f4)) 2... Nc6 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.Equal(4, game.Moves.Count);
        Assert.Equal("e4", game.Moves[0].San);
        Assert.Equal("e5", game.Moves[1].San);
        Assert.Equal("Nf3", game.Moves[2].San);
        Assert.Equal("Nc6", game.Moves[3].San);

        // The variation follows 2. Nf3, so it hangs off the third move of the main line.
        var variation = Assert.Single(game.Moves[2].Variations);
        Assert.Equal(2, variation.Count);
        Assert.Equal("Nc3", variation[0].San);

        var nested = Assert.Single(variation[1].Variations);
        Assert.Equal("f4", Assert.Single(nested).San);
    }

    [Fact]
    public void AllMoves_walks_the_whole_tree()
    {
        const string pgn = """
            [Result "*"]

            1. e4 e5 2. Nf3 (2. Nc3 Nf6 3. Bc4 (3. f4)) 2... Nc6 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        // 4 main-line moves + Nc3, Nf6, Bc4 + f4
        Assert.Equal(8, game.AllMoves().Count());
    }

    [Fact]
    public void A_game_starting_from_fen_continues_from_that_position()
    {
        const string pgn = """
            [Result "*"]
            [FEN "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3"]

            3. Bb5 a6 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.Equal(2, game.Moves.Count);
        Assert.Equal("Bb5", game.Moves[0].San);
        Assert.Equal("r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4", game.Replay().ToFen());
    }

    [Fact]
    public void Several_games_are_streamed_one_at_a_time()
    {
        var text = string.Join("\n\n", Enumerable.Repeat(SimpleGame, 5));
        var games = PgnReader.Read(text).ToList();

        Assert.Equal(5, games.Count);
        Assert.All(games, g => Assert.Equal(6, g.Moves.Count));
    }

    [Fact]
    public void Enumeration_is_lazy_so_a_huge_file_is_not_buffered()
    {
        var text = string.Join("\n\n", Enumerable.Repeat(SimpleGame, 1000));

        // Taking one game must not parse the other 999.
        var first = PgnReader.Read(text).Take(1).ToList();

        Assert.Single(first);
        Assert.Equal(6, first[0].Moves.Count);
    }

    [Fact]
    public void A_game_without_a_result_is_flushed_when_the_next_game_starts()
    {
        const string pgn = """
            [Event "first"]
            [Result "*"]

            1. e4 e5

            [Event "second"]
            [Result "1-0"]

            1. d4 d5 2. c4 1-0
            """;

        var games = PgnReader.Read(pgn).ToList();

        Assert.Equal(2, games.Count);
        Assert.Equal("first", games[0].Event);
        Assert.Equal(2, games[0].Moves.Count);
        Assert.Equal("second", games[1].Event);
        Assert.Equal(3, games[1].Moves.Count);
    }

    [Fact]
    public void An_unresolvable_move_is_reported_and_the_rest_of_the_line_skipped()
    {
        const string pgn = """
            [Result "*"]

            1. e4 e5 2. Qh7 Nf3 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.True(game.HasErrors);
        Assert.Equal(2, game.Moves.Count);
        Assert.Contains(game.ParseWarnings, w => w.Contains("Qh7", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unusable_fen_tag_is_reported_without_throwing()
    {
        const string pgn = """
            [Result "*"]
            [FEN "not a fen at all"]

            1. e4 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.True(game.HasErrors);
        Assert.Empty(game.Moves);
        Assert.Contains(game.ParseWarnings, w => w.Contains("FEN", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unterminated_comment_is_survived()
    {
        const string pgn = """
            [Result "*"]

            1. e4 {never closed
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.Contains(game.ParseWarnings, w => w.Contains("comment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Escaped_quotes_in_tags_are_unescaped()
    {
        const string pgn = """
            [Event "The \"big\" match"]
            [Result "*"]

            1. e4 *
            """;

        var game = Assert.Single(PgnReader.Read(pgn));

        Assert.Equal("The \"big\" match", game.Event);
    }

    // ---- writing ------------------------------------------------------------------------------

    [Fact]
    public void Write_round_trips_through_the_reader()
    {
        var original = Assert.Single(PgnReader.Read(SimpleGame));
        var written = PgnWriter.Write(original);
        var reread = Assert.Single(PgnReader.Read(written));

        Assert.Equal(original.Event, reread.Event);
        Assert.Equal(original.WhitePlayer, reread.WhitePlayer);
        Assert.Equal(original.Result, reread.Result);
        Assert.Equal(original.Moves.Select(m => m.San), reread.Moves.Select(m => m.San));
    }

    [Fact]
    public void Write_emits_the_seven_tag_roster_first()
    {
        const string pgn = """
            [ECO "C60"]
            [Event "Test"]
            [Site "Internet"]
            [Date "2026.10.13"]
            [Round "1"]
            [White "Atlas"]
            [Black "Rival"]
            [Result "1-0"]

            1. e4 1-0
            """;

        var written = PgnWriter.Write(Assert.Single(PgnReader.Read(pgn)));
        var firstLine = written.Split('\n')[0].Trim();

        Assert.StartsWith("[Event ", firstLine, StringComparison.Ordinal);
        Assert.Contains("[ECO \"C60\"]", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_preserves_comments_variations_and_nags()
    {
        const string pgn = """
            [Result "*"]

            1. e4 {best by test} e5 2. Nf3 (2. Nc3 Nf6) 2... Nc6 *
            """;

        var written = PgnWriter.Write(Assert.Single(PgnReader.Read(pgn)));
        var reread = Assert.Single(PgnReader.Read(written));

        Assert.Equal("best by test", reread.Moves[0].CommentAfter);
        var variation = Assert.Single(reread.Moves[2].Variations);
        Assert.Equal("Nc3", variation[0].San);
        Assert.Equal(4, reread.Moves.Count);
    }

    [Fact]
    public void Write_wraps_long_games_at_the_requested_column()
    {
        const string pgn = """
            [Event "long"]
            [Result "1-0"]

            1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6
            8. c3 O-O 9. h3 Nb8 10. d4 Nbd7 1-0
            """;

        var written = PgnWriter.Write(Assert.Single(PgnReader.Read(pgn)), maxLineLength: 60);
        var separator = written.IndexOf("\n\n", StringComparison.Ordinal);
        Assert.True(separator >= 0, "expected a blank line between the tags and the move text");
        var body = written[(separator + 2)..];

        Assert.All(
            body.Split('\n').Where(l => l.Trim().Length > 0),
            line => Assert.True(line.Length <= 60, $"line too long: '{line}'"));
    }

    [Fact]
    public void Write_uses_black_move_numbers_after_a_comment_or_variation()
    {
        const string pgn = """
            [Result "*"]

            1. e4 {note} e5 *
            """;

        var written = PgnWriter.Write(Assert.Single(PgnReader.Read(pgn)));

        Assert.Contains("1... e5", written, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_result_is_written_as_an_asterisk()
    {
        var game = Assert.Single(PgnReader.Read(SimpleGame));
        game.SetTag("Result", "nonsense");

        Assert.Equal(GameResult.Unknown, game.Result);
        Assert.EndsWith("*\n", PgnWriter.Write(game), StringComparison.Ordinal);
    }
}

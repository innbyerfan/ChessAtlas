using ChessAtlas.Domain.Chess;
using Xunit;

namespace ChessAtlas.Domain.Tests.Chess;

public sealed class SanTests
{
    private static Position At(string fen) => Position.FromFen(fen);

    [Fact]
    public void Pawn_moves_render_without_a_piece_letter()
    {
        var position = Position.StartPosition;

        Assert.Equal("e4", San.ToSan(position, Move.ParseUci("e2e4")));
    }

    [Fact]
    public void Piece_moves_render_with_their_letter()
    {
        var position = Position.StartPosition;

        Assert.Equal("Nf3", San.ToSan(position, Move.ParseUci("g1f3")));
    }

    [Fact]
    public void Captures_use_an_x()
    {
        var position = At("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2");

        Assert.Equal("exd5", San.ToSan(position, Move.ParseUci("e4d5")));
    }

    [Fact]
    public void Pawn_captures_show_the_origin_file()
    {
        var position = At("rnbqkbnr/ppp1pppp/8/3p4/2P5/8/PP1PPPPP/RNBQKBNR w KQkq - 0 2");

        Assert.Equal("cxd5", San.ToSan(position, Move.ParseUci("c4d5")));
    }

    [Fact]
    public void Castling_renders_as_dashes()
    {
        var kingside = At("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4");
        Assert.Equal("O-O", San.ToSan(kingside, Move.ParseUci("e1g1")));

        // b1, c1 and d1 must be empty for O-O-O, so the queen has been developed already.
        var queenside = At("r3k1nr/pppq1ppp/2np4/4p3/2B1P1b1/2NP1N2/PPP2PPP/R3KB1R w KQ - 0 7");
        Assert.Contains(Move.ParseUci("e1c1"), queenside.GenerateLegalMoves());
        Assert.Equal("O-O-O", San.ToSan(queenside, Move.ParseUci("e1c1")));
    }

    [Fact]
    public void Promotion_shows_the_chosen_piece()
    {
        var position = At("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");

        Assert.Equal(
            "a8=Q+",
            San.ToSan(position, new Move(Square.Parse("a7"), Square.Parse("a8"), PieceType.Queen)));
        Assert.Equal(
            "a8=N",
            San.ToSan(position, new Move(Square.Parse("a7"), Square.Parse("a8"), PieceType.Knight)));
    }

    [Fact]
    public void Check_and_mate_get_their_suffixes()
    {
        var check = At("4k3/8/8/8/8/8/4Q3/4K3 w - - 0 1");
        Assert.Equal("Qe6+", San.ToSan(check, Move.ParseUci("e2e6")));

        var mate = Position.StartPosition;
        foreach (var uci in new[] { "f2f3", "e7e5", "g2g4" }) mate.MakeMove(Move.ParseUci(uci));
        Assert.Equal("Qh4#", San.ToSan(mate, Move.ParseUci("d8h4")));
    }

    [Fact]
    public void Ambiguous_knights_are_disambiguated_by_file()
    {
        // Knights on b1 and f3 can both reach d2.
        var position = At("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1");

        Assert.Equal("Nbd2", San.ToSan(position, Move.ParseUci("b1d2")));
        Assert.Equal("Nfd2", San.ToSan(position, Move.ParseUci("f3d2")));
    }

    [Fact]
    public void Ambiguity_that_a_file_cannot_resolve_falls_back_to_the_rank()
    {
        // Knights on a1, a5 and d4 all reach b3. a1 and a5 share the a-file, so neither can be
        // named by file and they fall back to the rank; d4 is the only knight on the d-file.
        var position = At("4k3/N7/8/3N4/8/8/8/N3K3 w - - 0 1");

        Assert.Equal("N1b3", San.ToSan(position, Move.ParseUci("a1b3")));
        Assert.Equal("N5b3", San.ToSan(position, Move.ParseUci("a5b3")));
        Assert.Equal("Ndb3", San.ToSan(position, Move.ParseUci("d4b3")));
    }

    [Theory]
    [InlineData("e2e4", "e4")]
    [InlineData("g1f3", "Nf3")]
    public void Parsing_accepts_canonical_san(string uci, string san)
    {
        var position = Position.StartPosition;

        Assert.True(San.TryParse(position, san, out var parsed));
        Assert.Equal(Move.ParseUci(uci), parsed);
    }

    [Theory]
    [InlineData("e4")]
    [InlineData("E4")]
    [InlineData(" e4 ")]
    public void Parsing_ignores_case_and_padding(string san)
    {
        Assert.True(San.TryParse(Position.StartPosition, san, out var move));
        Assert.Equal(Move.ParseUci("e2e4"), move);
    }

    [Fact]
    public void Parsing_accepts_zero_zero_for_castling()
    {
        var position = At("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4");

        Assert.True(San.TryParse(position, "0-0", out var move));
        Assert.Equal(Move.ParseUci("e1g1"), move);
    }

    [Fact]
    public void Parsing_accepts_a_missing_capture_mark_and_promotion_equals()
    {
        var position = At("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");

        Assert.True(San.TryParse(position, "a8Q", out var withEquals));
        Assert.True(San.TryParse(position, "a8=Q", out var withoutEquals));
        Assert.Equal(withEquals, withoutEquals);
    }

    [Theory]
    [InlineData("e5")]    // pawn cannot reach
    [InlineData("Ke2")]   // not a legal king move from the start
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    public void Illegal_or_nonsense_san_does_not_parse(string san) =>
        Assert.False(San.TryParse(Position.StartPosition, san, out _));

    [Fact]
    public void Parse_throws_with_the_position_in_the_message()
    {
        var ex = Assert.Throws<FormatException>(() => San.Parse(Position.StartPosition, "Qh5"));

        Assert.Contains(Position.StandardFen, ex.Message);
    }

    [Fact]
    public void Every_legal_move_round_trips_through_san()
    {
        foreach (var fen in new[]
                 {
                     Position.StandardFen,
                     "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
                     "4k3/P7/8/8/8/8/8/4K3 w - - 0 1",
                     "8/8/8/8/k2pP2R/8/8/4K3 b - e3 0 1",
                 })
        {
            var position = At(fen);
            foreach (var move in position.GenerateLegalMoves())
            {
                var san = San.ToSan(position, move);
                Assert.True(San.TryParse(position, san, out var parsed), $"{san} did not re-parse in {fen}");
                Assert.Equal(move, parsed);
            }
        }
    }

    [Fact]
    public void Rendering_an_illegal_move_throws() =>
        Assert.Throws<IllegalMoveException>(() => San.ToSan(Position.StartPosition, Move.ParseUci("e2e5")));
}

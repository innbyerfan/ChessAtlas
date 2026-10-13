using ChessAtlas.Domain.Chess;
using Xunit;

namespace ChessAtlas.Domain.Tests.Chess;

public sealed class PositionTests
{
    private static Position At(string fen) => Position.FromFen(fen);

    [Fact]
    public void Start_position_has_the_standard_fen_and_twenty_moves()
    {
        var position = Position.StartPosition;

        Assert.Equal(Position.StandardFen, position.ToFen());
        Assert.Equal(20, position.GenerateLegalMoves().Count);
        Assert.False(position.IsInCheck);
        Assert.Equal(GameStatus.InProgress, position.EvaluateStatus());
    }

    [Fact]
    public void Fen_round_trips_every_field()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3";
        var position = At(fen);

        Assert.Equal(fen, position.ToFen());
        Assert.Equal(Side.Black, position.SideToMove);
        Assert.Equal(CastlingRights.All, position.CastlingRights);
        Assert.Null(position.EnPassantTarget);
        Assert.Equal(3, position.HalfmoveClock);
        Assert.Equal(3, position.FullmoveNumber);
    }

    [Fact]
    public void Fen_defaults_the_clocks_when_omitted()
    {
        var position = At("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -");

        Assert.Equal(0, position.HalfmoveClock);
        Assert.Equal(1, position.FullmoveNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq e9 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBN w KQkq - 0 1")] // 7 files
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNX w KQkq - 0 1")] // bad piece char
    [InlineData("rnbq1bnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")] // no black king
    public void Invalid_fen_is_rejected(string fen) =>
        Assert.Throws<FormatException>(() => Position.FromFen(fen));

    // ---- castling -----------------------------------------------------------------------------

    [Fact]
    public void Castling_kingside_moves_both_king_and_rook()
    {
        var position = At("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        position.PlayMove(Move.ParseUci("e1g1"));

        Assert.Equal(PieceType.King, position.At(Square.Parse("g1"))!.Value.Type);
        Assert.Equal(PieceType.Rook, position.At(Square.Parse("f1"))!.Value.Type);
        Assert.True(position.IsEmpty(Square.Parse("e1")));
        Assert.True(position.IsEmpty(Square.Parse("h1")));
        Assert.Equal(CastlingRights.Black, position.CastlingRights);
    }

    [Fact]
    public void Castling_queenside_moves_both_king_and_rook()
    {
        var position = At("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1");

        position.PlayMove(Move.ParseUci("e8c8"));

        Assert.Equal(PieceType.King, position.At(Square.Parse("c8"))!.Value.Type);
        Assert.Equal(PieceType.Rook, position.At(Square.Parse("d8"))!.Value.Type);
        Assert.Equal(CastlingRights.White, position.CastlingRights);
    }

    [Fact]
    public void Castling_is_blocked_by_a_piece_in_the_way()
    {
        var position = At("4k3/8/8/8/8/8/8/R2QK2R w KQ - 0 1");

        Assert.DoesNotContain(Move.ParseUci("e1g1"), position.GenerateLegalMoves());
        Assert.DoesNotContain(Move.ParseUci("e1c1"), position.GenerateLegalMoves());
    }

    [Fact]
    public void Castling_is_illegal_out_of_through_or_into_check()
    {
        // Black rook on e8 is gone; a black rook on the e-file attacks e1, a bishop attacks f1.
        var inCheck = At("4r3/8/8/8/8/8/8/R3K2R w KQ - 0 1");
        Assert.DoesNotContain(Move.ParseUci("e1g1"), inCheck.GenerateLegalMoves());
        Assert.DoesNotContain(Move.ParseUci("e1c1"), inCheck.GenerateLegalMoves());

        // Squares the king crosses are attacked, but e1 itself is not.
        var through = At("5r2/8/8/8/8/8/8/R3K2R w KQ - 0 1");
        Assert.DoesNotContain(Move.ParseUci("e1g1"), through.GenerateLegalMoves());

        // Only the square the king crosses is attacked, not e1 or g1: still illegal.
        var crossed = At("3r4/8/8/8/8/8/8/R3K2R w KQ - 0 1");
        Assert.DoesNotContain(Move.ParseUci("e1c1"), crossed.GenerateLegalMoves());
    }

    [Fact]
    public void Queenside_castling_is_allowed_with_b1_occupied_by_an_attack_on_b1()
    {
        // b1 attacked is fine: the king never stands there. Only c1 and d1 matter.
        var position = At("r3k3/8/8/8/1r6/8/8/R3K3 w Q - 0 1");

        Assert.Contains(Move.ParseUci("e1c1"), position.GenerateLegalMoves());
    }

    [Fact]
    public void Castling_rights_are_lost_when_the_king_moves()
    {
        var position = At("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1");

        position.PlayMove(Move.ParseUci("e1e2"));

        Assert.Equal(CastlingRights.None, position.CastlingRights);
    }

    [Fact]
    public void Castling_rights_are_lost_when_the_rook_moves()
    {
        var position = At("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1");

        position.PlayMove(Move.ParseUci("h1h5"));

        Assert.Equal(CastlingRights.WhiteQueenSide, position.CastlingRights);
    }

    [Fact]
    public void Castling_rights_are_lost_when_the_rook_is_captured_on_its_home_square()
    {
        // Black knight on b3 can reach a1. White plays a pawn move so no castling right is
        // forfeited for any other reason, then the rook is actually taken.
        var position = At("4k3/8/8/8/8/1n6/5P2/R3K2R w KQk - 0 1");

        position.MakeMove(Move.ParseUci("f2f3"));
        position.MakeMove(Move.ParseUci("b3a1"));

        Assert.Equal(PieceType.Knight, position.At(Square.Parse("a1"))!.Value.Type);
        Assert.False(position.CastlingRights.Has(CastlingRights.WhiteQueenSide));
        Assert.True(position.CastlingRights.Has(CastlingRights.WhiteKingSide));
    }

    // ---- en passant ---------------------------------------------------------------------------

    [Fact]
    public void En_passant_target_is_recorded_after_a_double_push()
    {
        var position = Position.StartPosition;

        position.PlayMove(Move.ParseUci("e2e4"));

        Assert.Equal(Square.Parse("e3"), position.EnPassantTarget);
    }

    [Fact]
    public void En_passant_capture_removes_the_captured_pawn()
    {
        var position = At("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");

        position.PlayMove(Move.ParseUci("e5d6"));

        Assert.True(position.IsEmpty(Square.Parse("d5")));
        Assert.Equal(PieceType.Pawn, position.At(Square.Parse("d6"))!.Value.Type);
        Assert.Equal(0, position.HalfmoveClock);
    }

    [Fact]
    public void En_passant_is_illegal_when_it_would_expose_the_king()
    {
        // Black king a4 and white rook h4 share the 4th rank, separated by the two pawns.
        // d4xe3 en passant lifts BOTH blockers at once, so it would leave the black king in check.
        var position = At("8/8/8/8/k2pP2R/8/8/4K3 b - e3 0 1");

        Assert.False(position.IsInCheck);
        Assert.DoesNotContain(Move.ParseUci("d4e3"), position.GenerateLegalMoves());
        Assert.Contains(Move.ParseUci("a4b5"), position.GenerateLegalMoves());
    }

    [Fact]
    public void A_pawn_cannot_move_diagonally_onto_an_empty_square()
    {
        var position = At("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1");

        Assert.DoesNotContain(Move.ParseUci("e2d3"), position.GenerateLegalMoves());
        Assert.Throws<IllegalMoveException>(() => position.MakeMove(Move.ParseUci("e2d3")));
    }

    // ---- promotion ----------------------------------------------------------------------------

    [Fact]
    public void Promotion_offers_four_choices()
    {
        var position = At("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");
        var promotions = position.GenerateLegalMoves().Where(m => m.From == Square.Parse("a7")).ToList();

        Assert.Equal(
            new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight },
            promotions.Select(p => p.Promotion).OrderByDescending(t => t).ToArray());
    }

    [Fact]
    public void Underpromotion_replaces_the_pawn_with_the_chosen_piece()
    {
        var position = At("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");

        position.PlayMove(new Move(Square.Parse("a7"), Square.Parse("a8"), PieceType.Knight));

        var promoted = position.At(Square.Parse("a8"))!.Value;
        Assert.Equal(PieceType.Knight, promoted.Type);
        Assert.Equal(Side.White, promoted.Side);
    }

    [Fact]
    public void Promotion_by_capture_works()
    {
        var position = At("1r2k3/P7/8/8/8/8/8/4K3 w - - 0 1");

        position.PlayMove(new Move(Square.Parse("a7"), Square.Parse("b8"), PieceType.Queen));

        Assert.Equal(PieceType.Queen, position.At(Square.Parse("b8"))!.Value.Type);
    }

    [Fact]
    public void Promotion_must_be_declared_when_a_pawn_reaches_the_last_rank()
    {
        var position = At("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");

        Assert.Throws<IllegalMoveException>(() => position.PlayMove(Move.ParseUci("a7a8")));
    }

    // ---- check, mate, stalemate ---------------------------------------------------------------

    [Fact]
    public void Foolsmate_is_detected_as_checkmate()
    {
        var position = Position.StartPosition;
        foreach (var uci in new[] { "f2f3", "e7e5", "g2g4", "d8h4" })
            position.MakeMove(Move.ParseUci(uci));

        Assert.True(position.IsInCheck);
        Assert.Empty(position.GenerateLegalMoves());
        Assert.Equal(GameStatus.Checkmate, position.EvaluateStatus());
        Assert.Equal(GameResult.WhiteWins, GameResultExtensions.FromStatus(GameStatus.Checkmate, position.SideToMove));
    }

    [Fact]
    public void Stalemate_is_not_checkmate()
    {
        var position = At("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.False(position.IsInCheck);
        Assert.Empty(position.GenerateLegalMoves());
        Assert.Equal(GameStatus.Stalemate, position.EvaluateStatus());
    }

    [Fact]
    public void A_pinned_piece_cannot_move_off_the_pin()
    {
        // White king e1, white knight e2, black rook e8: the knight may not move at all.
        var position = At("4r3/8/8/8/8/8/4N3/4K3 w - - 0 1");
        var knightMoves = position.GenerateLegalMoves().Where(m => m.From == Square.Parse("e2")).ToList();

        Assert.Empty(knightMoves);
    }

    [Fact]
    public void The_king_may_not_step_next_to_the_opposing_king()
    {
        var position = At("8/8/8/4k3/8/8/8/4K3 w - - 0 1");

        Assert.DoesNotContain(Move.ParseUci("e1e2"), position.GenerateLegalMoves());
        Assert.Contains(Move.ParseUci("e1d1"), position.GenerateLegalMoves());
    }

    // ---- draws --------------------------------------------------------------------------------

    [Fact]
    public void King_versus_king_is_insufficient_material()
    {
        Assert.True(At("4k3/8/8/8/8/8/8/4K3 w - - 0 1").HasInsufficientMaterial());
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4KB2 w - - 0 1", true)]  // K+B v K
    [InlineData("4k3/8/8/8/8/8/4N3/4K3 w - - 0 1", true)]  // K+N v K
    [InlineData("4k3/8/8/8/8/8/8/4KBN1 w - - 0 1", false)] // two minors: not called insufficient
    public void King_and_minors_are_handled(string fen, bool expected) =>
        Assert.Equal(expected, At(fen).HasInsufficientMaterial());

    [Fact]
    public void Same_coloured_bishops_are_insufficient_opposite_coloured_are_not()
    {
        // c1 and c3 are both dark squares.
        Assert.True(At("4k3/8/8/8/8/2B5/8/2B1K3 w - - 0 1").HasInsufficientMaterial());

        // c3 is dark, b5 is light. Not a forced mate, but FIDE does not call it insufficient.
        Assert.False(At("4k3/8/8/1B6/8/2B5/8/4K3 w - - 0 1").HasInsufficientMaterial());
    }

    [Fact]
    public void A_pawn_on_the_board_is_never_insufficient_material()
    {
        Assert.False(At("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1").HasInsufficientMaterial());
    }

    [Fact]
    public void Fifty_move_rule_ends_the_game_at_one_hundred_halfmoves()
    {
        var quiet = At("4k3/8/8/8/8/8/8/R3K3 w Q - 99 80");
        Assert.Equal(GameStatus.InProgress, quiet.EvaluateStatus());

        var drawn = At("4k3/8/8/8/8/8/8/R3K3 w Q - 100 80");
        Assert.Equal(GameStatus.DrawByFiftyMoveRule, drawn.EvaluateStatus());
    }

    [Fact]
    public void Threefold_repetition_is_detected_from_the_zobrist_history()
    {
        var position = Position.StartPosition;
        var history = new List<ulong> { position.ZobristKey };

        foreach (var uci in new[] { "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8" })
        {
            position.MakeMove(Move.ParseUci(uci));
            history.Add(position.ZobristKey);
        }

        Assert.True(position.IsThreefoldRepetition(history));
        Assert.Equal(GameStatus.DrawByThreefoldRepetition, position.EvaluateStatus(history));
    }

    // ---- move counters and hashing -----------------------------------------------------------

    [Fact]
    public void Halfmove_clock_resets_on_pawn_moves_and_captures()
    {
        var position = Position.StartPosition;

        position.MakeMove(Move.ParseUci("e2e4"));
        Assert.Equal(0, position.HalfmoveClock);

        position.MakeMove(Move.ParseUci("g8f6"));
        Assert.Equal(1, position.HalfmoveClock);

        position.MakeMove(Move.ParseUci("e4e5"));
        Assert.Equal(0, position.HalfmoveClock);
    }

    [Fact]
    public void Fullmove_number_increments_after_black_moves()
    {
        var position = Position.StartPosition;
        Assert.Equal(1, position.FullmoveNumber);

        position.MakeMove(Move.ParseUci("e2e4"));
        Assert.Equal(1, position.FullmoveNumber);

        position.MakeMove(Move.ParseUci("e7e5"));
        Assert.Equal(2, position.FullmoveNumber);
    }

    [Fact]
    public void Identical_positions_reached_differently_share_a_zobrist_key()
    {
        var a = Position.StartPosition;
        foreach (var uci in new[] { "e2e4", "e7e5", "g1f3", "b8c6" }) a.MakeMove(Move.ParseUci(uci));

        var b = Position.StartPosition;
        foreach (var uci in new[] { "g1f3", "b8c6", "e2e4", "e7e5" }) b.MakeMove(Move.ParseUci(uci));

        Assert.Equal(a.ZobristKey, b.ZobristKey);
        Assert.Equal(a.ToPositionKey(), b.ToPositionKey());
    }

    [Fact]
    public void Position_key_ignores_the_move_counters()
    {
        var a = At("4k3/8/8/8/8/8/8/4K3 w - - 0 1");
        var b = At("4k3/8/8/8/8/8/8/4K3 w - - 47 92");

        Assert.Equal(a.ToPositionKey(), b.ToPositionKey());
        Assert.NotEqual(a.ToFen(), b.ToFen());
    }

    [Fact]
    public void Position_key_omits_the_en_passant_file_when_no_capture_is_legal()
    {
        // Double push happened, but no enemy pawn can capture: the file is not part of the identity.
        var noCapture = At("4k3/8/8/8/4P3/8/8/4K3 b - e3 0 1");
        var noTarget = At("4k3/8/8/8/4P3/8/8/4K3 b - - 0 1");

        Assert.Equal(noTarget.ToPositionKey(), noCapture.ToPositionKey());

        // Here the black pawn on d4 really can take on e3, so the file is significant.
        var capturable = At("4k3/8/8/8/3pP3/8/8/4K3 b - e3 0 1");
        Assert.Contains(Move.ParseUci("d4e3"), capturable.GenerateLegalMoves());
        Assert.Contains("e3", capturable.ToPositionKey());
    }

    // ---- illegal input ------------------------------------------------------------------------

    [Fact]
    public void Playing_an_illegal_move_throws_and_leaves_the_position_untouched()
    {
        var position = Position.StartPosition;
        var before = position.ToFen();

        var ex = Assert.Throws<IllegalMoveException>(() => position.PlayMove(Move.ParseUci("e2e5")));

        Assert.Equal(before, position.ToFen());
        Assert.Equal("e2e5", ex.Move.ToUci());
        Assert.False(string.IsNullOrWhiteSpace(ex.Reason));
    }

    [Fact]
    public void Moving_the_opponents_piece_throws()
    {
        var position = Position.StartPosition;

        Assert.Throws<IllegalMoveException>(() => position.MakeMove(Move.ParseUci("e7e5")));
    }

    [Fact]
    public void Undo_on_an_empty_history_throws() =>
        Assert.Throws<InvalidOperationException>(() => Position.StartPosition.UndoMove());

    [Fact]
    public void Clone_is_independent_of_the_original()
    {
        var original = Position.StartPosition;
        var clone = original.Clone();

        original.MakeMove(Move.ParseUci("e2e4"));

        Assert.Equal(Position.StandardFen, clone.ToFen());
        Assert.NotEqual(original.ToFen(), clone.ToFen());
    }

    [Fact]
    public void FindKing_throws_when_the_king_is_missing()
    {
        var position = Position.Empty();

        Assert.Throws<InvalidOperationException>(() => position.FindKing(Side.White));
    }
}

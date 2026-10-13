using ChessAtlas.Domain.Chess;
using Xunit;

namespace ChessAtlas.Domain.Tests.Chess;

/// <summary>
/// Perft against the published node counts for the standard test positions.
///
/// <para>
/// The positions are chosen, not sampled. Each one stresses a rule that a naive generator gets
/// wrong in a way that only shows up several plies deep:
/// </para>
/// <list type="bullet">
/// <item><description>start position — the baseline, and the only one deep enough to be brutal.</description></item>
/// <item><description>Kiwipete — castling rights, pins and promotions tangled together.</description></item>
/// <item><description>position 3 — en passant, discovered checks and pinned pawns.</description></item>
/// <item><description>position 4 — promotion and underpromotion with capture.</description></item>
/// <item><description>position 5 — promotion, discovered check and a double-push en-passant square.</description></item>
/// <item><description>position 6 — a quiet middlegame, so ordinary piece movement is covered too.</description></item>
/// </list>
/// </summary>
public sealed class PerftTests
{
    private const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    private const string Position3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    private const string Position4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    private const string Position5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";
    private const string Position6 = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 20)]
    [InlineData(2, 400)]
    [InlineData(3, 8_902)]
    [InlineData(4, 197_281)]
    public void StartPosition_matches_published_counts(int depth, long expected) =>
        Assert.Equal(expected, Perft.Run(Position.FromFen(Position.StandardFen), depth));

    [Fact]
    public void StartPosition_depth5_matches_published_count() =>
        Assert.Equal(4_865_609L, Perft.Run(Position.FromFen(Position.StandardFen), 5));

    [Theory]
    [InlineData(1, 48)]
    [InlineData(2, 2_039)]
    [InlineData(3, 97_862)]
    public void Kiwipete_matches_published_counts(int depth, long expected) =>
        Assert.Equal(expected, Perft.Run(Position.FromFen(Kiwipete), depth));

    [Theory]
    [InlineData(1, 14)]
    [InlineData(2, 191)]
    [InlineData(3, 2_812)]
    [InlineData(4, 43_238)]
    [InlineData(5, 674_624)]
    public void Position3_matches_published_counts(int depth, long expected) =>
        Assert.Equal(expected, Perft.Run(Position.FromFen(Position3), depth));

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 264)]
    [InlineData(3, 9_467)]
    [InlineData(4, 422_333)]
    public void Position4_matches_published_counts(int depth, long expected) =>
        Assert.Equal(expected, Perft.Run(Position.FromFen(Position4), depth));

    [Theory]
    [InlineData(1, 44)]
    [InlineData(2, 1_486)]
    [InlineData(3, 62_379)]
    public void Position5_matches_published_counts(int depth, long expected) =>
        Assert.Equal(expected, Perft.Run(Position.FromFen(Position5), depth));

    [Theory]
    [InlineData(1, 46)]
    [InlineData(2, 2_079)]
    [InlineData(3, 89_890)]
    public void Position6_matches_published_counts(int depth, long expected) =>
        Assert.Equal(expected, Perft.Run(Position.FromFen(Position6), depth));

    [Fact]
    public void Divide_partitions_the_total_exactly()
    {
        var position = Position.FromFen(Kiwipete);
        var parts = Perft.Divide(position, 3);

        Assert.Equal(48, parts.Count);
        Assert.Equal(97_862L, parts.Sum(p => p.Nodes));
    }

    [Fact]
    public void Make_and_undo_leave_the_position_bit_identical()
    {
        var position = Position.FromFen(Kiwipete);
        var originalFen = position.ToFen();
        var originalKey = position.ZobristKey;

        foreach (var move in position.GenerateLegalMoves())
        {
            position.MakeMove(move);
            Assert.NotEqual(originalFen, position.ToFen());
            position.UndoMove();

            Assert.Equal(originalFen, position.ToFen());
            Assert.Equal(originalKey, position.ZobristKey);
        }
    }

    [Fact]
    public void Replaying_a_game_then_undoing_it_returns_to_the_start()
    {
        var position = Position.StartPosition;
        var startFen = position.ToFen();

        foreach (var uci in new[] { "e2e4", "e7e5", "g1f3", "b8c6", "f1b5", "a7a6" })
            position.MakeMove(Move.ParseUci(uci));

        Assert.Equal(6, position.PlyCount);

        for (var i = 0; i < 6; i++) position.UndoMove();

        Assert.Equal(startFen, position.ToFen());
        Assert.Equal(0, position.PlyCount);
    }
}

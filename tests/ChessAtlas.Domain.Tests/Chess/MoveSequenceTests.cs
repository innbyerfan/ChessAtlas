using ChessAtlas.Domain.Chess;
using Xunit;

namespace ChessAtlas.Domain.Tests.Chess;

public sealed class MoveSequenceTests
{
    [Fact]
    public void Building_a_line_replays_it_and_records_the_terminal_position()
    {
        var line = MoveSequence.FromSan("1. e4 c5 2. Nf3 d6");

        Assert.Equal(4, line.PlyCount);
        Assert.Equal("e2e4 c7c5 g1f3 d7d6", line.ToUciText());
        Assert.Equal("1. e4 c5 2. Nf3 d6", line.ToSanText());
        Assert.Equal(
            "rnbqkbnr/pp2pppp/3p4/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3",
            line.TerminalPosition.ToFen());
    }

    [Fact]
    public void An_illegal_continuation_is_rejected_at_the_ply_that_breaks()
    {
        var ex = Assert.Throws<FormatException>(() => MoveSequence.FromSan("1. e4 e5 2. Qh7"));

        Assert.Contains("ply 3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unresolvable_san_is_rejected() =>
        Assert.Throws<FormatException>(() => MoveSequence.FromSan("1. Banana"));

    [Fact]
    public void Uci_and_san_forms_agree()
    {
        var fromSan = MoveSequence.FromSan("1. e4 e5 2. Nf3 Nc6 3. Bb5");
        var fromUci = MoveSequence.FromUci(fromSan.ToUciText());

        Assert.Equal(fromSan, fromUci);
        Assert.Equal(fromSan.TerminalPositionKey, fromUci.TerminalPositionKey);
    }

    [Fact]
    public void Illegal_uci_is_rejected()
    {
        Assert.Throws<FormatException>(() => MoveSequence.FromUci("e2e5"));
        Assert.Throws<FormatException>(() => MoveSequence.FromUci("not a move"));
    }

    [Fact]
    public void Different_move_orders_to_the_same_position_share_a_terminal_key()
    {
        var a = MoveSequence.FromSan("1. e4 e5 2. Nf3 Nc6");
        var b = MoveSequence.FromSan("1. Nf3 Nc6 2. e4 e5");

        Assert.NotEqual(a, b);
        Assert.Equal(a.TerminalPositionKey, b.TerminalPositionKey);
    }

    [Fact]
    public void Take_returns_a_prefix_that_is_itself_a_valid_line()
    {
        var line = MoveSequence.FromSan("1. e4 c5 2. Nf3 d6 3. d4 cxd4");

        Assert.Equal("1. e4 c5", line.Take(2).ToSanText());
        Assert.Equal("e2e4 c7c5", line.Take(2).ToUciText());
        Assert.Equal(line, line.Take(line.PlyCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Take(line.PlyCount + 1));
    }

    [Fact]
    public void StartsWith_recognises_prefixes_and_rejects_divergence()
    {
        var full = MoveSequence.FromSan("1. e4 c5 2. Nf3 d6");
        var prefix = MoveSequence.FromSan("1. e4 c5");
        var other = MoveSequence.FromSan("1. d4 d5");

        Assert.True(full.StartsWith(prefix));
        Assert.True(full.StartsWith(full));
        Assert.False(prefix.StartsWith(full));
        Assert.False(full.StartsWith(other));
    }

    [Fact]
    public void Lines_from_a_non_standard_start_render_black_move_numbers_correctly()
    {
        var line = MoveSequence.FromSan(
            "Nf3",
            "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 2 2");

        Assert.Equal("2. Nf3", line.ToSanText());

        var blackFirst = MoveSequence.FromSan(
            "e5",
            "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");

        Assert.Equal("1... e5", blackFirst.ToSanText());
    }

    [Fact]
    public void An_empty_line_is_a_real_position_not_a_null()
    {
        var empty = MoveSequence.Empty();

        Assert.Equal(0, empty.PlyCount);
        Assert.Equal(string.Empty, empty.ToSanText());
        Assert.Equal(string.Empty, empty.ToUciText());
        Assert.Equal(Position.StandardFen, empty.TerminalPosition.ToFen());
        Assert.True(empty.TerminalPosition.GenerateLegalMoves().Count > 0);
    }

    [Fact]
    public void Equality_covers_both_the_start_and_the_moves()
    {
        var a = MoveSequence.FromSan("1. e4 e5");
        var b = MoveSequence.FromSan("1. e4 e5");
        var c = MoveSequence.FromSan("1. e4 c5");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Move_numbers_results_and_comments_in_the_source_are_ignored()
    {
        var noisy = MoveSequence.FromSan("{opening} 1. e4 {best} 1... c5 2. Nf3 * ");
        var clean = MoveSequence.FromSan("1. e4 c5 2. Nf3");

        Assert.Equal(clean, noisy);
    }
}

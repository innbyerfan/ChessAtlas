using ChessAtlas.Domain.Chess;

namespace ChessAtlas.Domain.Tests.Chess;

/// <summary>
/// Perft: play every legal move, recurse, count the leaf nodes.
///
/// <para>
/// This is the only move-generator test that matters. Hand-written assertions prove the cases you
/// thought of; perft proves the ones you did not, because a single wrong rule — an en-passant pin,
/// a castling right not cleared when a rook is captured, a promotion that forgets to remove the
/// pawn — diverges from the published node count within a few plies.
/// </para>
/// </summary>
internal static class Perft
{
    public static long Run(Position position, int depth)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (depth < 0) throw new ArgumentOutOfRangeException(nameof(depth));
        if (depth == 0) return 1;

        var moves = position.GenerateLegalMoves();
        if (depth == 1) return moves.Count;

        long nodes = 0;
        foreach (var move in moves)
        {
            position.MakeMove(move);
            nodes += Run(position, depth - 1);
            position.UndoMove();
        }

        return nodes;
    }

    /// <summary>Per-move node counts at one depth, used to pin down <em>which</em> move diverges.</summary>
    public static IReadOnlyList<(Move Move, long Nodes)> Divide(Position position, int depth)
    {
        var result = new List<(Move, long)>();
        foreach (var move in position.GenerateLegalMoves())
        {
            position.MakeMove(move);
            var nodes = Run(position, depth - 1);
            position.UndoMove();
            result.Add((move, nodes));
        }

        return result;
    }
}

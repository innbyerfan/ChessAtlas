namespace ChessAtlas.Domain.Chess;

/// <summary>
/// Zobrist hashing tables. The generator is a fixed-seed SplitMix64, so keys are stable across
/// processes and machines — a requirement because position keys are compared between the opening
/// index, the repertoire and the trainer across separate runs.
/// </summary>
internal static class Zobrist
{
    /// <summary>Indexed as <c>((int)type - 1) * 2 + (int)side</c>, giving 12 entries.</summary>
    public static readonly ulong[,] PieceSquare = new ulong[12, 64];

    public static readonly ulong[] Castling = new ulong[16];
    public static readonly ulong[] EnPassantFile = new ulong[8];
    public static readonly ulong SideToMove;

    static Zobrist()
    {
        var rng = new SplitMix64(0x43_48_45_53_53_41_54_4CUL); // "CHESSATL"

        for (var slot = 0; slot < 12; slot++)
            for (var square = 0; square < 64; square++)
                PieceSquare[slot, square] = rng.Next();

        for (var i = 0; i < Castling.Length; i++) Castling[i] = rng.Next();
        for (var i = 0; i < EnPassantFile.Length; i++) EnPassantFile[i] = rng.Next();

        SideToMove = rng.Next();
    }

    public static int SlotFor(Piece piece) => ((int)piece.Type - 1) * 2 + (int)piece.Side;

    /// <summary>Mutable on purpose: every <see cref="Next"/> call must advance the stream.</summary>
    private struct SplitMix64
    {
        private ulong _state;

        internal SplitMix64(ulong seed) => _state = seed;

        internal ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

using System.Diagnostics.CodeAnalysis;

namespace ChessAtlas.Domain.Chess;

/// <summary>
/// A chess board square, addressed as (file 0..7 = a..h, rank 0..7 = 1..8).
/// The linear index is <c>rank * 8 + file</c>, so A1 = 0 and H8 = 63.
/// </summary>
public readonly struct Square : IEquatable<Square>, IComparable<Square>
{
    public const int Count = 64;

    private Square(int file, int rank)
    {
        File = file;
        Rank = rank;
    }

    public int File { get; }
    public int Rank { get; }
    public int Index => (Rank << 3) | File;

    public static Square FromFileRank(int file, int rank)
    {
        if ((uint)file > 7) throw new ArgumentOutOfRangeException(nameof(file), "File must be 0..7.");
        if ((uint)rank > 7) throw new ArgumentOutOfRangeException(nameof(rank), "Rank must be 0..7.");
        return new Square(file, rank);
    }

    public static Square FromIndex(int index)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index), "Index must be 0..63.");
        return new Square(index & 7, index >> 3);
    }

    /// <summary>Parses algebraic coordinates such as <c>e4</c>. Case-insensitive; whitespace is not allowed.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out Square square)
    {
        square = default;
        if (text.Length != 2) return false;

        var file = char.ToLowerInvariant(text[0]) - 'a';
        if (file is < 0 or > 7) return false;

        var rank = text[1] - '1';
        if (rank is < 0 or > 7) return false;

        square = new Square(file, rank);
        return true;
    }

    public static Square Parse(string text) =>
        TryParse(text, out var square)
            ? square
            : throw new FormatException($"'{text}' is not a valid square.");

    public Square WithFileOffset(int delta) => FromFileRank(File + delta, Rank);
    public Square WithRankOffset(int delta) => FromFileRank(File, Rank + delta);
    public Square Offset(int fileDelta, int rankDelta) => FromFileRank(File + fileDelta, Rank + rankDelta);

    public bool IsInside(int fileDelta, int rankDelta) =>
        (uint)(File + fileDelta) <= 7 && (uint)(Rank + rankDelta) <= 7;

    /// <summary>True on the light ("white") squares, e.g. h1 and a8.</summary>
    public bool IsLight => ((File + Rank) & 1) == 1;

    public override string ToString() => $"{(char)('a' + File)}{(char)('1' + Rank)}";

    public bool Equals(Square other) => Index == other.Index;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Square other && Equals(other);
    public override int GetHashCode() => Index;
    public int CompareTo(Square other) => Index.CompareTo(other.Index);

    public static bool operator ==(Square left, Square right) => left.Index == right.Index;
    public static bool operator !=(Square left, Square right) => left.Index != right.Index;
}

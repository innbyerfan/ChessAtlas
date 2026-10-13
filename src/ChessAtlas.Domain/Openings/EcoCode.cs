using System.Diagnostics.CodeAnalysis;

namespace ChessAtlas.Domain.Openings;

/// <summary>
/// An Encyclopaedia of Chess Openings code: a letter <c>A</c>–<c>E</c> and a number <c>00</c>–<c>99</c>.
/// Modelled as a value object so nothing downstream can hold a malformed code.
/// </summary>
public readonly struct EcoCode : IEquatable<EcoCode>, IComparable<EcoCode>
{
    public EcoCode(char letter, int number)
    {
        var upper = char.ToUpperInvariant(letter);
        if (upper is < 'A' or > 'E')
            throw new ArgumentOutOfRangeException(nameof(letter), "An ECO letter must be between A and E.");
        if (number is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(number), "An ECO number must be between 0 and 99.");

        Letter = upper;
        Number = number;
    }

    public char Letter { get; }
    public int Number { get; }

    /// <summary>Canonical three-character form, e.g. <c>B90</c>.</summary>
    public string Value => $"{Letter}{Number:D2}";

    /// <summary>
    /// The standard ECO grouping. These are the published section headings, not a Chess Atlas
    /// invention: A = flank and irregular openings, B = semi-open games other than the French,
    /// C = open games and the French, D = closed and semi-closed games, E = Indian defences.
    /// </summary>
    public string Family => Letter switch
    {
        'A' => "Flank and Irregular Openings",
        'B' => "Semi-Open Games (except the French)",
        'C' => "Open Games and the French Defence",
        'D' => "Closed and Semi-Closed Games",
        'E' => "Indian Defences",
        _ => throw new InvalidOperationException($"Unreachable ECO letter '{Letter}'."),
    };

    public static bool TryParse(string? text, out EcoCode code)
    {
        code = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.Trim();
        if (span.Length != 3) return false;
        if (!char.IsLetter(span[0])) return false;
        if (!char.IsDigit(span[1]) || !char.IsDigit(span[2])) return false;

        try
        {
            code = new EcoCode(span[0], (span[1] - '0') * 10 + (span[2] - '0'));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static EcoCode Parse(string text) =>
        TryParse(text, out var code) ? code : throw new FormatException($"'{text}' is not a valid ECO code.");

    /// <summary>True when the two codes share a letter, i.e. belong to the same ECO section.</summary>
    public bool SameFamilyAs(EcoCode other) => Letter == other.Letter;

    public bool Equals(EcoCode other) => Letter == other.Letter && Number == other.Number;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EcoCode other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Letter, Number);
    public override string ToString() => Value;

    public int CompareTo(EcoCode other) =>
        Letter != other.Letter ? Letter.CompareTo(other.Letter) : Number.CompareTo(other.Number);

    public static bool operator ==(EcoCode left, EcoCode right) => left.Equals(right);
    public static bool operator !=(EcoCode left, EcoCode right) => !left.Equals(right);
}

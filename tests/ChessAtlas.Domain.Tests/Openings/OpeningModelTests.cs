using ChessAtlas.Domain.Chess;
using ChessAtlas.Domain.Openings;
using Xunit;

namespace ChessAtlas.Domain.Tests.Openings;

public sealed class EcoCodeTests
{
    [Theory]
    [InlineData("A00", 'A', 0)]
    [InlineData("b90", 'B', 90)]
    [InlineData(" E99 ", 'E', 99)]
    public void Valid_codes_parse(string text, char letter, int number)
    {
        Assert.True(EcoCode.TryParse(text, out var code));
        Assert.Equal(letter, code.Letter);
        Assert.Equal(number, code.Number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F00")]   // no ECO section F
    [InlineData("B9")]    // too short
    [InlineData("B900")]  // too long
    [InlineData("9B0")]   // letter in the wrong place
    [InlineData("Bx0")]   // not a digit
    public void Invalid_codes_do_not_parse(string? text) =>
        Assert.False(EcoCode.TryParse(text, out _));

    [Fact]
    public void Parse_throws_on_garbage() =>
        Assert.Throws<FormatException>(() => EcoCode.Parse("Z99"));

    [Fact]
    public void The_canonical_form_is_always_three_characters()
    {
        Assert.Equal("A00", new EcoCode('a', 0).Value);
        Assert.Equal("B07", new EcoCode('B', 7).Value);
    }

    [Fact]
    public void The_constructor_rejects_out_of_range_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EcoCode('F', 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EcoCode('A', 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EcoCode('A', -1));
    }

    [Theory]
    [InlineData('A', "Flank and Irregular Openings")]
    [InlineData('B', "Semi-Open Games (except the French)")]
    [InlineData('C', "Open Games and the French Defence")]
    [InlineData('D', "Closed and Semi-Closed Games")]
    [InlineData('E', "Indian Defences")]
    public void Each_letter_maps_to_its_published_section(char letter, string family) =>
        Assert.Equal(family, new EcoCode(letter, 0).Family);

    [Fact]
    public void Codes_sort_by_letter_then_number()
    {
        var codes = new[] { EcoCode.Parse("B90"), EcoCode.Parse("A00"), EcoCode.Parse("B07"), EcoCode.Parse("E99") };

        Array.Sort(codes);

        Assert.Equal(new[] { "A00", "B07", "B90", "E99" }, codes.Select(c => c.Value));
    }

    [Fact]
    public void Equality_ignores_nothing()
    {
        Assert.Equal(EcoCode.Parse("B90"), EcoCode.Parse("b90"));
        Assert.NotEqual(EcoCode.Parse("B90"), EcoCode.Parse("B91"));
        Assert.True(EcoCode.Parse("B90").SameFamilyAs(EcoCode.Parse("B07")));
        Assert.False(EcoCode.Parse("B90").SameFamilyAs(EcoCode.Parse("C07")));
    }
}

public sealed class OpeningTests
{
    [Fact]
    public void A_path_derives_its_family_terminal_name_and_depth()
    {
        var opening = Opening.Create("Sicilian Defense: Najdorf Variation: English Attack");

        Assert.Equal("Sicilian Defense", opening.FamilyKey);
        Assert.Equal("English Attack", opening.TerminalName);
        Assert.Equal(3, opening.Depth);
        Assert.Equal("Sicilian Defense: Najdorf Variation: English Attack", opening.CanonicalName);
    }

    [Fact]
    public void A_single_segment_name_is_its_own_family()
    {
        var opening = Opening.Create("Ruy Lopez");

        Assert.Equal("Ruy Lopez", opening.FamilyKey);
        Assert.Equal("Ruy Lopez", opening.TerminalName);
        Assert.Equal(1, opening.Depth);
    }

    [Fact]
    public void Path_segments_are_trimmed_and_empties_dropped()
    {
        Assert.Equal(
            new[] { "Sicilian Defense", "Najdorf Variation" },
            Opening.SplitPath("  Sicilian Defense ::  Najdorf Variation  "));
    }

    [Fact]
    public void A_name_with_no_usable_segment_is_rejected() =>
        Assert.Throws<ArgumentException>(() => Opening.Create("  :  :  "));

    [Fact]
    public void Aliases_are_deduplicated_per_language()
    {
        var opening = Opening.Create("Ruy Lopez");

        opening.AddAlias("Spanish Game", "en");
        opening.AddAlias("Spanish Game", "en");
        opening.AddAlias("اسپانیایی", "fa");

        Assert.Equal(2, opening.Aliases.Count);
    }

    [Fact]
    public void Editing_updates_the_timestamp()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var opening = Opening.Create("Ruy Lopez", now: created);

        Assert.Equal(created, opening.UpdatedAtUtc);

        opening.SetDescription("The Spanish Opening.");

        Assert.True(opening.UpdatedAtUtc >= created);
        Assert.Equal("The Spanish Opening.", opening.Description);
    }

    [Fact]
    public void Eco_is_stored_as_text_and_read_back_as_a_value_object()
    {
        var opening = Opening.Create("Sicilian Defense: Najdorf Variation", EcoCode.Parse("B90"));

        Assert.Equal("B90", opening.EcoCodeValue);
        Assert.Equal(EcoCode.Parse("B90"), opening.Eco);
    }
}

public sealed class OpeningLineTests
{
    private const int AnyOpeningId = 7;

    [Fact]
    public void Creating_a_line_records_both_notations_and_the_terminal_key()
    {
        var line = OpeningLine.Create(
            AnyOpeningId,
            "Sicilian Defense",
            "1. e4 c5",
            eco: EcoCode.Parse("B20"));

        Assert.Equal("1. e4 c5", line.SanText);
        Assert.Equal("e2e4 c7c5", line.UciText);
        Assert.Equal(2, line.PlyCount);
        Assert.Equal("B20", line.EcoCodeValue);
        Assert.Equal(
            MoveSequence.FromSan("1. e4 c5").TerminalPositionKey,
            line.TerminalPositionKey);
    }

    [Fact]
    public void An_illegal_line_cannot_be_created() =>
        Assert.Throws<FormatException>(() => OpeningLine.Create(AnyOpeningId, "Broken", "1. e4 e5 2. Qh7"));

    [Fact]
    public void Asserting_a_trap_editorially_requires_an_explanation()
    {
        var ex = Assert.Throws<ArgumentException>(() => OpeningLine.Create(
            AnyOpeningId,
            "Noah's Ark Trap",
            "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 b5 5. Bb3 d6 6. d4 Nxd4 7. Nxd4 exd4 8. Qxd4 c5",
            kind: OpeningLineKind.Trap,
            kindOrigin: ClassificationOrigin.Editorial));

        Assert.Contains("rationale", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_trap_named_by_the_source_is_allowed_but_is_not_marked_verified()
    {
        var line = OpeningLine.Create(
            AnyOpeningId,
            "Ruy Lopez: Noah's Ark Trap",
            "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 b5 5. Bb3 d6 6. d4 Nxd4 7. Nxd4 exd4 8. Qxd4 c5",
            kind: OpeningLineKind.Trap,
            kindOrigin: ClassificationOrigin.UpstreamName,
            verificationStatus: VerificationStatus.Unverified,
            notes: "Named as a trap by the imported catalogue.");

        Assert.True(line.IsTrap);
        Assert.True(line.KindIsInferred);
        Assert.Equal(VerificationStatus.Unverified, line.VerificationStatus);
        Assert.Null(line.Rationale);
    }

    [Fact]
    public void A_gambit_taken_from_the_source_name_carries_its_origin()
    {
        var line = OpeningLine.Create(
            AnyOpeningId,
            "Amar Opening: Paris Gambit",
            "1. Nh3 d5 2. g3 e5 3. f4",
            kind: OpeningLineKind.Gambit,
            kindOrigin: ClassificationOrigin.UpstreamName,
            verificationStatus: VerificationStatus.ReferenceVerified);

        Assert.True(line.IsGambit);
        Assert.True(line.KindIsInferred);
    }

    [Fact]
    public void Editorial_reclassification_demands_a_rationale_and_marks_the_line_curated()
    {
        var line = OpeningLine.Create(AnyOpeningId, "Some Line", "1. e4 e5");

        Assert.Throws<ArgumentException>(() => line.Reclassify(OpeningLineKind.Trap));

        line.Reclassify(OpeningLineKind.Trap, "Black loses the queen to a fork after 3... Qe7.", TrapVictim.Black);

        Assert.True(line.IsTrap);
        Assert.Equal(ClassificationOrigin.Editorial, line.KindOrigin);
        Assert.Equal(TrapVictim.Black, line.Victim);
        Assert.Equal(VerificationStatus.UserCurated, line.VerificationStatus);
        Assert.False(line.KindIsInferred);
    }

    [Fact]
    public void Resolve_replays_the_stored_moves()
    {
        var line = OpeningLine.Create(AnyOpeningId, "Sicilian Defense", "1. e4 c5 2. Nf3");
        var sequence = line.Resolve();

        Assert.Equal(3, sequence.PlyCount);
        Assert.Equal(line.UciText, sequence.ToUciText());
        Assert.Same(sequence, line.Resolve());
    }

    [Fact]
    public void A_line_from_a_non_standard_start_keeps_its_fen()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";
        var line = OpeningLine.Create(AnyOpeningId, "Ruy Lopez", "3. Bb5", startingFen: fen);

        Assert.Equal(fen, line.StartingFen);
        Assert.Equal("3. Bb5", line.SanText);
    }
}

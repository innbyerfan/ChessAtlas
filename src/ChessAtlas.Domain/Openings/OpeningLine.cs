using ChessAtlas.Domain.Chess;
using ChessAtlas.Domain.Provenance;

namespace ChessAtlas.Domain.Openings;

/// <summary>
/// One concrete move sequence attached to an <see cref="Opening"/>.
///
/// <para>
/// The moves are stored twice — SAN for people, UCI for machines — plus the terminal position key.
/// All three are written together at build time by <see cref="Create"/>, which replays the line, so
/// a persisted line is one that was legal when it was accepted.
/// </para>
///
/// <para>
/// <see cref="TerminalPositionKey"/> is what makes transpositions work: two lines with different
/// names and different move orders that end on the same board share a key, so "every named line
/// that reaches this position" is an index lookup rather than a search.
/// </para>
/// </summary>
public sealed class OpeningLine
{
    private MoveSequence? _resolved;

    private OpeningLine()
    {
        Name = string.Empty;
        StartingFen = Position.StandardFen;
        SanText = string.Empty;
        UciText = string.Empty;
        TerminalPositionKey = string.Empty;
    }

    public int Id { get; private set; }

    public int OpeningId { get; private set; }

    /// <summary>Optional parent line inside the same opening, for nesting main line → sub-lines.</summary>
    public int? ParentLineId { get; private set; }

    public string Name { get; private set; }

    public string? EcoCodeValue { get; private set; }

    public string StartingFen { get; private set; }

    /// <summary>Move text with move numbers, e.g. <c>1. e4 c5 2. Nf3 d6</c>.</summary>
    public string SanText { get; private set; }

    /// <summary>The same moves in UCI form, e.g. <c>e2e4 c7c5 g1f3 d7d6</c>.</summary>
    public string UciText { get; private set; }

    public int PlyCount { get; private set; }

    /// <summary>Transposition key of the position the line ends in. Indexed.</summary>
    public string TerminalPositionKey { get; private set; }

    public OpeningLineKind Kind { get; private set; }

    /// <summary>Who decided <see cref="Kind"/>. Shown next to the kind in the UI.</summary>
    public ClassificationOrigin KindOrigin { get; private set; }

    public VerificationStatus VerificationStatus { get; private set; }

    public OpeningDifficulty Difficulty { get; private set; }

    /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="OpeningLineKind.Trap"/>.</summary>
    public TrapVictim Victim { get; private set; }

    /// <summary>
    /// Why the trap works, or what the strategic idea is. Required by the editor before a line may
    /// be published as a trap — a trap with no explanation is a guess.
    /// </summary>
    public string? Rationale { get; private set; }

    public string? Notes { get; private set; }

    public int? ProvenanceId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public EcoCode? Eco => EcoCode.TryParse(EcoCodeValue, out var code) ? code : null;

    /// <summary>Replays the stored moves. Cached after the first call.</summary>
    public MoveSequence Resolve() => _resolved ??= MoveSequence.FromUci(UciText, StartingFen);

    /// <summary>
    /// Builds a line from SAN or UCI text. The text is replayed through a real position, so an
    /// illegal move throws here rather than surfacing later in the trainer.
    /// </summary>
    public static OpeningLine Create(
        int openingId,
        string name,
        string sanText,
        OpeningLineKind kind = OpeningLineKind.MainLine,
        ClassificationOrigin kindOrigin = ClassificationOrigin.Editorial,
        VerificationStatus verificationStatus = VerificationStatus.Unverified,
        EcoCode? eco = null,
        string? startingFen = null,
        string? rationale = null,
        string? notes = null,
        OpeningDifficulty difficulty = OpeningDifficulty.Unrated,
        TrapVictim victim = TrapVictim.NotApplicable,
        int? parentLineId = null,
        int? provenanceId = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sanText);

        var fen = string.IsNullOrWhiteSpace(startingFen) ? Position.StandardFen : startingFen.Trim();
        var sequence = MoveSequence.FromSan(sanText, fen);
        var timestamp = now ?? DateTimeOffset.UtcNow;

        // A trap is a claim that a specific move loses. When a person makes that claim they have to
        // say why; recording a bad move as a "known trap" with no explanation is precisely the false
        // certainty this model exists to prevent. A name copied from an upstream catalogue is a
        // different, weaker statement - "this source calls it a trap" - and is allowed through with
        // KindOrigin = UpstreamName, which the UI renders as unverified rather than as fact.
        if (kind == OpeningLineKind.Trap &&
            kindOrigin == ClassificationOrigin.Editorial &&
            string.IsNullOrWhiteSpace(rationale))
        {
            throw new ArgumentException(
                "Asserting a trap editorially requires a rationale explaining why the line loses.",
                nameof(rationale));
        }

        return new OpeningLine
        {
            OpeningId = openingId,
            ParentLineId = parentLineId,
            Name = name.Trim(),
            EcoCodeValue = eco?.Value,
            StartingFen = fen,
            SanText = sequence.ToSanText(),
            UciText = sequence.ToUciText(),
            PlyCount = sequence.PlyCount,
            TerminalPositionKey = sequence.TerminalPositionKey,
            Kind = kind,
            KindOrigin = kindOrigin,
            VerificationStatus = verificationStatus,
            Difficulty = difficulty,
            Victim = victim,
            Rationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            ProvenanceId = provenanceId,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
            _resolved = sequence,
        };
    }

    /// <summary>Editorial change: a person reviewed the kind and can now vouch for it.</summary>
    public void Reclassify(OpeningLineKind kind, string? rationale = null, TrapVictim victim = TrapVictim.NotApplicable)
    {
        if (kind == OpeningLineKind.Trap && string.IsNullOrWhiteSpace(rationale))
            throw new ArgumentException(
                "Reclassifying a line as a trap is an editorial assertion and requires a rationale.",
                nameof(rationale));

        Kind = kind;
        KindOrigin = ClassificationOrigin.Editorial;
        Victim = victim;
        if (!string.IsNullOrWhiteSpace(rationale)) Rationale = rationale.Trim();
        VerificationStatus = VerificationStatus.UserCurated;
        Touch();
    }

    public void MarkVerified(VerificationStatus status)
    {
        VerificationStatus = status;
        Touch();
    }

    public void SetNotes(string? notes)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch();
    }

    public void SetEco(EcoCode? eco)
    {
        EcoCodeValue = eco?.Value;
        Touch();
    }

    public bool IsTrap => Kind == OpeningLineKind.Trap;
    public bool IsGambit => Kind == OpeningLineKind.Gambit;

    /// <summary>
    /// True when the kind was inferred rather than decided — the UI dims these and never states
    /// them as fact.
    /// </summary>
    public bool KindIsInferred => KindOrigin is ClassificationOrigin.UpstreamName or ClassificationOrigin.ExtractedFromGames;

    private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;

    public override string ToString() => $"{Name} [{Kind}] {SanText}";
}

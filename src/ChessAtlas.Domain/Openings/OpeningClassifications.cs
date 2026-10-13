namespace ChessAtlas.Domain.Openings;

/// <summary>What role a line plays inside its opening.</summary>
public enum OpeningLineKind
{
    /// <summary>The primary continuation the opening is known by.</summary>
    MainLine = 0,

    /// <summary>A named branch below the main line.</summary>
    SubVariation = 1,

    /// <summary>A deliberate material sacrifice for compensation.</summary>
    Gambit = 2,

    /// <summary>A documented pitfall: one side can lose quickly to a natural-looking reply.</summary>
    Trap = 3,

    /// <summary>A position the theory considers decisive for understanding the opening.</summary>
    CriticalPosition = 4,

    /// <summary>A secondary option kept alongside the main line.</summary>
    Alternative = 5,

    /// <summary>A different move order that reaches an already-named position.</summary>
    Transposition = 6,
}

/// <summary>
/// How much weight a line deserves. A line harvested from a game database is a statistic, not
/// theory, and the product must never present it as if it were reviewed literature.
/// </summary>
public enum VerificationStatus
{
    /// <summary>Imported and not yet looked at by a human.</summary>
    Unverified = 0,

    /// <summary>Comes from a maintained reference source and was accepted as-is.</summary>
    ReferenceVerified = 1,

    /// <summary>
    /// Derived from played games (frequency, results). Carries no claim that the line is good.
    /// </summary>
    ExtractedFromGames = 2,

    /// <summary>Written or corrected by the user.</summary>
    UserCurated = 3,

    /// <summary>Examined and dismissed; kept for audit rather than deleted.</summary>
    Rejected = 4,
}

/// <summary>
/// Where a line's classification came from. "Gambit" copied out of an upstream name is a different
/// claim from "Gambit" decided by a person, and the UI shows the difference.
/// </summary>
public enum ClassificationOrigin
{
    Unknown = 0,

    /// <summary>A person assigned the kind with a recorded rationale.</summary>
    Editorial = 1,

    /// <summary>The kind was read off the source's own naming, e.g. "…: Paris Gambit".</summary>
    UpstreamName = 2,

    /// <summary>Inferred from game data; never used for traps.</summary>
    ExtractedFromGames = 3,
}

/// <summary>How hard a line is to play, when there is a basis for saying so.</summary>
public enum OpeningDifficulty
{
    /// <summary>Nobody has graded it. Shown as "not graded", never as "easy".</summary>
    Unrated = 0,

    Beginner = 1,
    Intermediate = 2,
    Advanced = 3,
    Expert = 4,
}

/// <summary>Which side a trap is aimed at.</summary>
public enum TrapVictim
{
    NotApplicable = 0,
    White = 1,
    Black = 2,
    EitherSide = 3,
}

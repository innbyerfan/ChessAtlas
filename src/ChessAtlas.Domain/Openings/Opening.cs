namespace ChessAtlas.Domain.Openings;

/// <summary>
/// A named node in the opening hierarchy — "Sicilian Defense", "Sicilian Defense: Najdorf
/// Variation", and so on. The node carries the <em>name</em>; the moves live on
/// <see cref="OpeningLine"/>, because one name can be reached by several move orders and one move
/// order can carry several names.
/// </summary>
public sealed class Opening
{
    private readonly List<OpeningAlias> _aliases = [];
    private readonly List<OpeningLine> _lines = [];

    private Opening()
    {
        CanonicalName = string.Empty;
        TerminalName = string.Empty;
        FamilyKey = string.Empty;
    }

    public int Id { get; private set; }

    /// <summary>
    /// The full colon-separated path, e.g. <c>Sicilian Defense: Najdorf Variation: English Attack</c>.
    /// Unique — it is the node's identity in the catalogue.
    /// </summary>
    public string CanonicalName { get; private set; }

    /// <summary>The last path segment: <c>English Attack</c>.</summary>
    public string TerminalName { get; private set; }

    /// <summary>The first path segment: <c>Sicilian Defense</c>. Denormalised for filtering.</summary>
    public string FamilyKey { get; private set; }

    /// <summary>Segment count in <see cref="CanonicalName"/>; 1 for a family, 2+ for variations.</summary>
    public int Depth { get; private set; }

    /// <summary>Stored as the canonical three-character form, e.g. <c>B90</c>.</summary>
    public string? EcoCodeValue { get; private set; }

    public string? Description { get; private set; }

    public OpeningDifficulty Difficulty { get; private set; }

    /// <summary>Optional parent in the naming tree. Null for top-level families.</summary>
    public int? ParentOpeningId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<OpeningAlias> Aliases => _aliases;
    public IReadOnlyCollection<OpeningLine> Lines => _lines;

    public EcoCode? Eco => EcoCode.TryParse(EcoCodeValue, out var code) ? code : null;

    /// <summary>
    /// Creates a node from a colon-separated path. The family, terminal name and depth are derived
    /// from the path rather than supplied separately, so they cannot drift apart.
    /// </summary>
    public static Opening Create(
        string canonicalName,
        EcoCode? eco = null,
        string? description = null,
        OpeningDifficulty difficulty = OpeningDifficulty.Unrated,
        int? parentOpeningId = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);

        var segments = SplitPath(canonicalName);
        var timestamp = now ?? DateTimeOffset.UtcNow;

        return new Opening
        {
            CanonicalName = string.Join(": ", segments),
            TerminalName = segments[^1],
            FamilyKey = segments[0],
            Depth = segments.Length,
            EcoCodeValue = eco?.Value,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Difficulty = difficulty,
            ParentOpeningId = parentOpeningId,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
        };
    }

    public void SetEco(EcoCode? eco)
    {
        EcoCodeValue = eco?.Value;
        Touch();
    }

    public void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }

    public void SetDifficulty(OpeningDifficulty difficulty)
    {
        Difficulty = difficulty;
        Touch();
    }

    public void AttachToParent(int parentOpeningId)
    {
        ParentOpeningId = parentOpeningId;
        Touch();
    }

    /// <summary>Adds an alternative or localised name. Duplicate aliases for one language are ignored.</summary>
    public OpeningAlias AddAlias(string alias, string? language = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var normalised = alias.Trim();
        var existing = _aliases.FirstOrDefault(a =>
            string.Equals(a.Alias, normalised, StringComparison.Ordinal) &&
            string.Equals(a.Language ?? string.Empty, language ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        if (existing is not null) return existing;

        var created = OpeningAlias.Create(Id, normalised, language);
        _aliases.Add(created);
        Touch();
        return created;
    }

    /// <summary>Splits a colon-separated opening path, trimming each segment and dropping empties.</summary>
    public static string[] SplitPath(string canonicalName)
    {
        var segments = canonicalName
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToArray();

        if (segments.Length == 0)
            throw new ArgumentException($"'{canonicalName}' contains no usable name segment.", nameof(canonicalName));

        return segments;
    }

    private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;
}

/// <summary>An alternative name for an <see cref="Opening"/>, optionally language-tagged.</summary>
public sealed class OpeningAlias
{
    private OpeningAlias()
    {
        Alias = string.Empty;
    }

    public int Id { get; private set; }
    public int OpeningId { get; private set; }

    public string Alias { get; private set; }

    /// <summary>BCP-47 tag such as <c>en</c> or <c>fa</c>. Null means "not language-specific".</summary>
    public string? Language { get; private set; }

    public static OpeningAlias Create(int openingId, string alias, string? language = null) =>
        new()
        {
            OpeningId = openingId,
            Alias = alias.Trim(),
            Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
        };
}

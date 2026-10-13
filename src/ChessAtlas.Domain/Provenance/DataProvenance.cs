namespace ChessAtlas.Domain.Provenance;

/// <summary>
/// Where a piece of data came from, and under what terms it may be used.
///
/// <para>
/// Every imported row — an opening line, a puzzle, a game, an engine binary — carries one of
/// these. Two rules drive the design:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A licence is recorded per record, not per feature. "Free to use" and "free to redistribute" are
/// different claims, and a product that ships data has to be able to answer which one applies.
/// </description></item>
/// <item><description>
/// Provenance is never silently rewritten. Re-importing the same source updates the snapshot
/// fields; it does not change who decided what.
/// </description></item>
/// </list>
/// </summary>
public sealed class DataProvenance
{
    // EF Core materialises through this constructor. Every non-nullable property gets a value so
    // the compiler can prove the type is never half-built, even before Create() fills it in.
    private DataProvenance()
    {
        Provider = string.Empty;
        Uri = string.Empty;
        License = string.Empty;
    }

    public int Id { get; private set; }

    /// <summary>Human-readable owner of the source, e.g. <c>lichess-org/chess-openings</c>.</summary>
    public string Provider { get; private set; }

    /// <summary>Where it was fetched from. Never used as a download endpoint at runtime.</summary>
    public string Uri { get; private set; }

    /// <summary>SPDX licence identifier, or a short note when the terms are not machine-readable.</summary>
    public string License { get; private set; }

    /// <summary>Commit SHA, release tag or file version the snapshot came from.</summary>
    public string? VersionOrCommit { get; private set; }

    /// <summary>Hash of the fetched payload when one was checked.</summary>
    public string? Checksum { get; private set; }

    public DateTimeOffset RetrievedAt { get; private set; }

    /// <summary>
    /// True when the licence permits shipping the data inside the application bundle. False means
    /// the user must fetch it themselves, which is what the UI offers in that case.
    /// </summary>
    public bool RedistributionAllowed { get; private set; }

    public string? Notes { get; private set; }

    public static DataProvenance Create(
        string provider,
        string uri,
        string license,
        bool redistributionAllowed,
        DateTimeOffset? retrievedAt = null,
        string? versionOrCommit = null,
        string? checksum = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(license);

        return new DataProvenance
        {
            Provider = provider.Trim(),
            Uri = (uri ?? string.Empty).Trim(),
            License = license.Trim(),
            VersionOrCommit = versionOrCommit?.Trim(),
            Checksum = checksum?.Trim(),
            RetrievedAt = retrievedAt ?? DateTimeOffset.UtcNow,
            RedistributionAllowed = redistributionAllowed,
            Notes = notes?.Trim(),
        };
    }

    /// <summary>Re-stamps the snapshot identity after a re-import, keeping the licence untouched.</summary>
    public void RecordSnapshot(
        DateTimeOffset retrievedAt,
        string? versionOrCommit = null,
        string? checksum = null)
    {
        RetrievedAt = retrievedAt;
        if (!string.IsNullOrWhiteSpace(versionOrCommit)) VersionOrCommit = versionOrCommit.Trim();
        if (!string.IsNullOrWhiteSpace(checksum)) Checksum = checksum.Trim();
    }

    public override string ToString() => $"{Provider} @ {VersionOrCommit ?? "unknown"} ({License})";
}

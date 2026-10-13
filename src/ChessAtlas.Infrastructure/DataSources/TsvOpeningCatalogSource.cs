using System.Runtime.CompilerServices;
using ChessAtlas.Application.Openings;
using ChessAtlas.Domain.Provenance;
using Microsoft.Extensions.Logging;

namespace ChessAtlas.Infrastructure.DataSources;

/// <summary>
/// Streams an opening catalogue from tab-separated <c>eco / name / pgn</c> files.
///
/// <para>
/// One row at a time, one line of memory. The reader never buffers the file, so the bundled 3.8k-row
/// snapshot and a much larger future catalogue cost the same.
/// </para>
///
/// <para>
/// Malformed rows are yielded with empty fields rather than dropped silently. The import use case is
/// what decides whether a row is usable, and it counts and reports what it rejects — an importer
/// that quietly skips rows makes it impossible to notice a truncated download.
/// </para>
/// </summary>
public sealed class TsvOpeningCatalogSource : IOpeningCatalogSource
{
    private static readonly string[] FileNames = ["a.tsv", "b.tsv", "c.tsv", "d.tsv", "e.tsv"];

    private readonly string _directory;
    private readonly ILogger<TsvOpeningCatalogSource> _logger;

    public TsvOpeningCatalogSource(string directory, DataProvenance provenance, ILogger<TsvOpeningCatalogSource> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Provider = provenance.Provider;
    }

    public string Provider { get; }

    public DataProvenance Provenance { get; }

    /// <summary>True when at least one catalogue file is present on disk.</summary>
    public bool IsAvailable => EnumerateFiles().Any();

    public IEnumerable<string> EnumerateFiles()
    {
        if (!Directory.Exists(_directory)) yield break;

        foreach (var name in FileNames)
        {
            var path = Path.Combine(_directory, name);
            if (File.Exists(path)) yield return path;
        }
    }

    public async IAsyncEnumerable<OpeningCatalogRow> ReadRowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var files = EnumerateFiles().ToList();
        if (files.Count == 0)
        {
            _logger.LogWarning(
                "No opening catalogue files found in {Directory}. Expected any of {Files}.",
                _directory, string.Join(", ", FileNames));
            yield break;
        }

        foreach (var file in files)
        {
            await using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

            var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (header is null) continue;

            if (!IsExpectedHeader(header))
                _logger.LogWarning(
                    "Catalogue file {File} has an unexpected header '{Header}'; reading it as data.",
                    Path.GetFileName(file), header);

            var lineNumber = 1;
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;

                if (line.Length == 0) continue;

                var parts = line.Split('\t');
                if (parts.Length < 3)
                {
                    yield return new OpeningCatalogRow(
                        parts.Length > 0 ? parts[0] : null,
                        parts.Length > 1 ? parts[1] : string.Empty,
                        string.Empty);
                    continue;
                }

                yield return new OpeningCatalogRow(parts[0], parts[1], parts[2]);
            }

            _logger.LogDebug("Read {File} up to line {Line}.", Path.GetFileName(file), lineNumber);
        }
    }

    private static bool IsExpectedHeader(string header)
    {
        var parts = header.Split('\t');
        return parts.Length >= 3 &&
               parts[0].Equals("eco", StringComparison.OrdinalIgnoreCase) &&
               parts[1].Equals("name", StringComparison.OrdinalIgnoreCase) &&
               parts[2].Equals("pgn", StringComparison.OrdinalIgnoreCase);
    }
}

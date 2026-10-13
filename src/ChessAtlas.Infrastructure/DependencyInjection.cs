using ChessAtlas.Application.Openings;
using ChessAtlas.Domain.Provenance;
using ChessAtlas.Infrastructure.DataSources;
using ChessAtlas.Infrastructure.Openings;
using ChessAtlas.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChessAtlas.Infrastructure;

/// <summary>What the host has to tell infrastructure: where things live on this machine.</summary>
public sealed record ChessAtlasInfrastructureOptions
{
    /// <summary>Absolute path of the SQLite database file. Directories are created on demand.</summary>
    public required string DatabasePath { get; init; }

    /// <summary>
    /// Where the bundled opening catalogue lives. Null means "find it" — see
    /// <see cref="LichessChessOpenings.ResolveDirectory"/>.
    /// </summary>
    public string? OpeningCatalogDirectory { get; init; }

    public static string DefaultDatabasePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChessAtlas",
            "chessatlas.db");
}

/// <summary>
/// Composition for the infrastructure layer. The desktop app calls this once; tests call it with a
/// throwaway database path and get exactly the same graph, which is the point.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddChessAtlasInfrastructure(
        this IServiceCollection services,
        ChessAtlasInfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Journal mode and cache size are PRAGMAs, not connection-string keys - see MigrateAsync,
        // which applies them once per database rather than pretending the builder has them.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = options.DatabasePath }.ToString();

        services.AddDbContext<ChessAtlasDbContext>(builder => builder.UseSqlite(connectionString));

        var catalogDirectory = options.OpeningCatalogDirectory ?? LichessChessOpenings.ResolveDirectory();
        var provenance = LichessChessOpenings.CreateProvenance();

        services.AddSingleton<IOpeningCatalogSource>(provider =>
        {
            var logger = provider.GetService<ILoggerFactory>()?.CreateLogger<TsvOpeningCatalogSource>()
                         ?? NullLogger<TsvOpeningCatalogSource>.Instance;

            if (catalogDirectory is null)
                return new MissingOpeningCatalogSource(provenance, logger);

            return new TsvOpeningCatalogSource(catalogDirectory, provenance, logger);
        });

        services.AddScoped<IOpeningCatalogImporter, OpeningCatalogImporter>();
        services.AddScoped<IOpeningCatalogRepository, OpeningCatalogRepository>();
        services.AddScoped<ImportOpeningCatalogUseCase>();

        return services;
    }

    /// <summary>
    /// Applies pending migrations. Called once at startup by the host — never implicitly inside a
    /// repository, because a schema change surfacing as a random query failure is undebuggable.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<ChessAtlasDbContext>();

        // WAL lets a reader stay responsive while the catalogue import writes in batches, and the
        // larger page cache matters once the opening tables are fully populated. Both are per-database
        // settings, so they are applied here rather than on every connection string.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA cache_size=-8000;", cancellationToken).ConfigureAwait(false);

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stands in when the bundled catalogue is not on disk, so the rest of the application still
    /// starts and the opening page can show a real "import the catalogue" empty state instead of
    /// crashing on a missing directory.
    /// </summary>
    private sealed class MissingOpeningCatalogSource : IOpeningCatalogSource
    {
        private readonly ILogger _logger;

        public MissingOpeningCatalogSource(DataProvenance provenance, ILogger logger)
        {
            Provenance = provenance;
            _logger = logger;
            Provider = provenance.Provider;
        }

        public string Provider { get; }
        public DataProvenance Provenance { get; }

        public async IAsyncEnumerable<OpeningCatalogRow> ReadRowsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogWarning(
                "The bundled opening catalogue was not found; the import produced no rows.");
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}

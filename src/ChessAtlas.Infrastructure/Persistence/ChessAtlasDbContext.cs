using ChessAtlas.Domain.Openings;
using ChessAtlas.Domain.Provenance;
using Microsoft.EntityFrameworkCore;

namespace ChessAtlas.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core context.
///
/// <para>
/// This context owns structured application data: the opening catalogue, provenance records, and
/// (in later phases) repertoires, training state, engines and settings. It deliberately does
/// <em>not</em> try to own multi-million game archives — see
/// <c>docs/architecture/storage-strategy.md</c> for why those get their own store.
/// </para>
/// </summary>
public sealed class ChessAtlasDbContext : DbContext
{
    public ChessAtlasDbContext(DbContextOptions<ChessAtlasDbContext> options)
        : base(options)
    {
    }

    public DbSet<Opening> Openings => Set<Opening>();
    public DbSet<OpeningAlias> OpeningAliases => Set<OpeningAlias>();
    public DbSet<OpeningLine> OpeningLines => Set<OpeningLine>();
    public DbSet<DataProvenance> DataProvenances => Set<DataProvenance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChessAtlasDbContext).Assembly);
    }
}

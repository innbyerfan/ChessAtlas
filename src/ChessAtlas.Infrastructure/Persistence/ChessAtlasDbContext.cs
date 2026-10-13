using ChessAtlas.Domain.Openings;
using ChessAtlas.Domain.Provenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    /// <summary>
    /// Stores every <see cref="DateTimeOffset"/> as INTEGER ticks.
    ///
    /// <para>
    /// The SQLite provider refuses to translate <c>ORDER BY</c> over a <c>DateTimeOffset</c> at all
    /// — it throws <see cref="NotSupportedException"/> regardless of how the column is stored,
    /// because the CLR type appears in the expression tree. Sorting openings by
    /// <c>UpdatedAtUtc</c> and finding the most recent provenance snapshot are both ordinary
    /// requirements here, so the values are converted instead of the queries being pulled into
    /// memory.
    /// </para>
    ///
    /// <para>
    /// Ticks are taken in UTC. Every timestamp in the model is created from
    /// <see cref="DateTimeOffset.UtcNow"/>, so nothing is lost; if a non-UTC instant ever enters the
    /// model it is normalised to the same absolute point, which is what ordering and comparison
    /// should mean for it anyway.
    /// </para>
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChessAtlasDbContext).Assembly);
    }

    private sealed class UtcTicksConverter : ValueConverter<DateTimeOffset, long>
    {
        public UtcTicksConverter()
            : base(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero))
        {
        }
    }
}

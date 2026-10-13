using ChessAtlas.Domain.Openings;
using ChessAtlas.Domain.Provenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChessAtlas.Infrastructure.Persistence.Configurations;

public sealed class OpeningConfiguration : IEntityTypeConfiguration<Opening>
{
    public void Configure(EntityTypeBuilder<Opening> builder)
    {
        builder.ToTable("Openings");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.CanonicalName).HasMaxLength(512).IsRequired();
        builder.Property(o => o.TerminalName).HasMaxLength(256).IsRequired();
        builder.Property(o => o.FamilyKey).HasMaxLength(256).IsRequired();
        builder.Property(o => o.EcoCodeValue).HasMaxLength(3);
        builder.Property(o => o.Description).HasMaxLength(4000);
        builder.Property(o => o.Difficulty).HasConversion<int>();
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.Property(o => o.UpdatedAtUtc).IsRequired();

        // The canonical path is the node's identity, so a second import of the same catalogue must
        // find these rows instead of duplicating them.
        builder.HasIndex(o => o.CanonicalName).IsUnique();
        builder.HasIndex(o => o.FamilyKey);
        builder.HasIndex(o => o.EcoCodeValue);
        builder.HasIndex(o => o.Depth);

        // Read-only collection navigations backed by private List fields.
        builder.Metadata.FindNavigation(nameof(Opening.Aliases))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Opening.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Aliases)
            .WithOne()
            .HasForeignKey(a => a.OpeningId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.OpeningId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Opening>()
            .WithMany()
            .HasForeignKey(o => o.ParentOpeningId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OpeningAliasConfiguration : IEntityTypeConfiguration<OpeningAlias>
{
    public void Configure(EntityTypeBuilder<OpeningAlias> builder)
    {
        builder.ToTable("OpeningAliases");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Alias).HasMaxLength(256).IsRequired();

        // SQLite treats NULLs as distinct in unique indexes, which would let the same alias be
        // added repeatedly. Normalising "no language" to an empty string keeps the constraint real.
        builder.Property(a => a.Language)
            .HasMaxLength(16)
            .HasConversion(
                v => v ?? string.Empty,
                v => v.Length == 0 ? null : v);

        builder.HasIndex(a => new { a.OpeningId, a.Alias, a.Language }).IsUnique();
        builder.HasIndex(a => a.Alias);
    }
}

public sealed class OpeningLineConfiguration : IEntityTypeConfiguration<OpeningLine>
{
    public void Configure(EntityTypeBuilder<OpeningLine> builder)
    {
        builder.ToTable("OpeningLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).HasMaxLength(512).IsRequired();
        builder.Property(l => l.EcoCodeValue).HasMaxLength(3);
        builder.Property(l => l.StartingFen).HasMaxLength(128).IsRequired();
        builder.Property(l => l.SanText).HasMaxLength(8000).IsRequired();
        builder.Property(l => l.UciText).HasMaxLength(8000).IsRequired();
        builder.Property(l => l.TerminalPositionKey).HasMaxLength(128).IsRequired();
        builder.Property(l => l.Rationale).HasMaxLength(4000);
        builder.Property(l => l.Notes).HasMaxLength(4000);
        builder.Property(l => l.Kind).HasConversion<int>();
        builder.Property(l => l.KindOrigin).HasConversion<int>();
        builder.Property(l => l.VerificationStatus).HasConversion<int>();
        builder.Property(l => l.Difficulty).HasConversion<int>();
        builder.Property(l => l.Victim).HasConversion<int>();
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc).IsRequired();

        // The whole point of storing the terminal position: "what is named here?" is an index seek,
        // and lines that arrive by different move orders land on the same row set.
        builder.HasIndex(l => l.TerminalPositionKey);

        // One opening may not hold the same move sequence twice. Also what makes an interrupted
        // import safe to restart.
        builder.HasIndex(l => new { l.OpeningId, l.StartingFen, l.UciText }).IsUnique();

        builder.HasIndex(l => l.Kind);
        builder.HasIndex(l => l.VerificationStatus);
        builder.HasIndex(l => l.EcoCodeValue);

        builder.HasOne<OpeningLine>()
            .WithMany()
            .HasForeignKey(l => l.ParentLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DataProvenance>()
            .WithMany()
            .HasForeignKey(l => l.ProvenanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class DataProvenanceConfiguration : IEntityTypeConfiguration<DataProvenance>
{
    public void Configure(EntityTypeBuilder<DataProvenance> builder)
    {
        builder.ToTable("DataProvenances");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Uri).HasMaxLength(1024).IsRequired();
        builder.Property(p => p.License).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Checksum).HasMaxLength(256);
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.Property(p => p.RetrievedAt).IsRequired();

        // Same NULL-distinctness workaround as the alias language.
        builder.Property(p => p.VersionOrCommit)
            .HasMaxLength(256)
            .HasConversion(
                v => v ?? string.Empty,
                v => v.Length == 0 ? null : v);

        builder.HasIndex(p => new { p.Provider, p.VersionOrCommit }).IsUnique();
    }
}

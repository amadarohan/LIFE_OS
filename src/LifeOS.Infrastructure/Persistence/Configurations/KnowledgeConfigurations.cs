using LifeOS.Domain.Entities.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.NoteType).EnumAsString().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.IsPinned, x.UpdatedAtUtc });

        builder.HasMany(x => x.Tags)
            .WithOne(x => x.Note)
            .HasForeignKey(x => x.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.EntryDate).IsUnique();
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(ColumnConventions.ColorLength).IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class NoteTagConfiguration : IEntityTypeConfiguration<NoteTag>
{
    public void Configure(EntityTypeBuilder<NoteTag> builder)
    {
        builder.ToTable("NoteTags");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.NoteId, x.TagId }).IsUnique();

        builder.HasOne(x => x.Note)
            .WithMany(x => x.Tags)
            .HasForeignKey(x => x.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Tag)
            .WithMany()
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class KnowledgeLinkConfiguration : IEntityTypeConfiguration<KnowledgeLink>
{
    public void Configure(EntityTypeBuilder<KnowledgeLink> builder)
    {
        builder.ToTable("KnowledgeLinks");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceEntityType).EnumAsString().IsRequired();
        builder.Property(x => x.TargetEntityType).EnumAsString().IsRequired();
        builder.Property(x => x.RelationshipType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.TargetEntityType, x.TargetEntityId });
        builder.HasIndex(x => new
        {
            x.SourceEntityType,
            x.SourceEntityId,
            x.TargetEntityType,
            x.TargetEntityId,
            x.RelationshipType
        }).IsUnique();
    }
}

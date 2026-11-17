using LifeOS.Domain.Entities.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("AiConversations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.ContextType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.RelatedEntityType).HasMaxLength(ColumnConventions.EntityTypeLength);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.UpdatedAtUtc);
        builder.HasIndex(x => new { x.RelatedEntityType, x.RelatedEntityId });
    }
}

public sealed class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.ToTable("AiMessages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Role).EnumAsString().IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(ColumnConventions.EntityTypeLength);
        builder.Property(x => x.Model).HasMaxLength(ColumnConventions.EntityTypeLength);
        builder.Property(x => x.SourcesJson);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });

        builder.HasOne(x => x.Conversation)
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AiMemoryConfiguration : IEntityTypeConfiguration<AiMemory>
{
    public void Configure(EntityTypeBuilder<AiMemory> builder)
    {
        builder.ToTable("AiMemories");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.MemoryType).EnumAsString().IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.SourceEntityType).HasMaxLength(ColumnConventions.EntityTypeLength);
        builder.Property(x => x.Confidence).Confidence().IsRequired();
        builder.Property(x => x.ValidFromUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.ValidUntilUtc).UtcTimestamp();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.MemoryType, x.IsActive });
        builder.HasIndex(x => new { x.SourceEntityType, x.SourceEntityId });
    }
}

public sealed class AiInsightConfiguration : IEntityTypeConfiguration<AiInsight>
{
    public void Configure(EntityTypeBuilder<AiInsight> builder)
    {
        builder.ToTable("AiInsights");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength).IsRequired();
        builder.Property(x => x.InsightType).EnumAsString().IsRequired();
        builder.Property(x => x.Severity).EnumAsString().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Confidence).Confidence().IsRequired();
        builder.Property(x => x.EvidenceJson);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.ExpiresAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
    }
}

public sealed class AiActionConfiguration : IEntityTypeConfiguration<AiAction>
{
    public void Configure(EntityTypeBuilder<AiAction> builder)
    {
        builder.ToTable("AiActions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActionType).EnumAsString().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(ColumnConventions.KeyLength).IsRequired();
        builder.Property(x => x.ContextHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ApprovedAtUtc).UtcTimestamp();
        builder.Property(x => x.ExecutedAtUtc).UtcTimestamp();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.ExpiresAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });

        builder.HasOne(x => x.Conversation)
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AiExecutionLogConfiguration : IEntityTypeConfiguration<AiExecutionLog>
{
    public void Configure(EntityTypeBuilder<AiExecutionLog> builder)
    {
        builder.ToTable("AiExecutionLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ToolName).HasMaxLength(ColumnConventions.EntityTypeLength).IsRequired();
        builder.Property(x => x.InputJson).IsRequired();
        builder.Property(x => x.OutputJson);
        builder.Property(x => x.ExecutionStatus).EnumAsString().IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.StartedAtUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.CompletedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.AiActionId, x.StartedAtUtc });

        // Bind the declared navigation explicitly; using HasOne<AiAction>() would make EF
        // treat AiAction as a second, unrelated relationship and add a shadow AiActionId1 column.
        builder.HasOne(x => x.AiAction)
            .WithMany()
            .HasForeignKey(x => x.AiActionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

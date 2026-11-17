using LifeOS.Domain.Entities.Calendar;
using LifeOS.Domain.Entities.Decisions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class DecisionConfiguration : IEntityTypeConfiguration<Decision>
{
    public void Configure(EntityTypeBuilder<Decision> builder)
    {
        builder.ToTable("Decisions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Context);
        builder.Property(x => x.ExpectedOutcome);
        builder.Property(x => x.ActualOutcome);
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Importance).EnumAsString().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Status, x.ReviewDate });

        builder.HasMany(x => x.Options)
            .WithOne(x => x.Decision)
            .HasForeignKey(x => x.DecisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class DecisionOptionConfiguration : IEntityTypeConfiguration<DecisionOption>
{
    public void Configure(EntityTypeBuilder<DecisionOption> builder)
    {
        builder.ToTable("DecisionOptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.ExpectedBenefits);
        builder.Property(x => x.PotentialRisks);
        builder.Property(x => x.OpportunityCost);
        builder.Property(x => x.EstimatedCost).Money();
        builder.Property(x => x.EstimatedEffort).Hours();
        builder.Property(x => x.ProbabilityOfSuccess).Percentage();
        builder.Property(x => x.Score).HasPrecision(9, 2);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.DecisionId);
    }
}

public sealed class DecisionReviewConfiguration : IEntityTypeConfiguration<DecisionReview>
{
    public void Configure(EntityTypeBuilder<DecisionReview> builder)
    {
        builder.ToTable("DecisionReviews");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActualOutcome);
        builder.Property(x => x.WhatWentWell);
        builder.Property(x => x.WhatWentWrong);
        builder.Property(x => x.LessonsLearned);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.DecisionId);

        // History is preserved: deleting a decision must not silently erase its reviews.
        // Bind the declared navigation explicitly; HasOne<Decision>() would create a shadow DecisionId1.
        builder.HasOne(x => x.Decision)
            .WithMany()
            .HasForeignKey(x => x.DecisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("CalendarEvents", t =>
        {
            t.HasCheckConstraint("CK_CalendarEvents_ValidRange", "[EndAtUtc] > [StartAtUtc]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Location).HasMaxLength(ColumnConventions.NameLength);
        builder.Property(x => x.EventType).EnumAsString().IsRequired();
        builder.Property(x => x.StartAtUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.EndAtUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.StartAtUtc);

        builder.HasOne<Domain.Entities.Projects.Project>()
            .WithMany()
            .HasForeignKey(x => x.RelatedProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Domain.Entities.Goals.Goal>()
            .WithMany()
            .HasForeignKey(x => x.RelatedGoalId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class CommitmentConfiguration : IEntityTypeConfiguration<Commitment>
{
    public void Configure(EntityTypeBuilder<Commitment> builder)
    {
        builder.ToTable("Commitments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.CommitmentType).EnumAsString().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Importance).EnumAsString().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.DueDate, x.Status });

        builder.HasOne<Domain.Entities.Projects.Project>()
            .WithMany()
            .HasForeignKey(x => x.RelatedProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Domain.Entities.Tasks.TaskItem>()
            .WithMany()
            .HasForeignKey(x => x.RelatedTaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using LifeOS.Domain.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class ProgressSnapshotConfiguration : IEntityTypeConfiguration<ProgressSnapshot>
{
    public void Configure(EntityTypeBuilder<ProgressSnapshot> builder)
    {
        builder.ToTable("ProgressSnapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityType).EnumAsString().IsRequired();
        builder.Property(x => x.ProgressPercentage).Percentage().IsRequired();
        builder.Property(x => x.Status).HasMaxLength(ColumnConventions.EnumLength).IsRequired();
        builder.Property(x => x.MetricsJson);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.SnapshotDate });
    }
}

public sealed class WeeklyReviewConfiguration : IEntityTypeConfiguration<WeeklyReview>
{
    public void Configure(EntityTypeBuilder<WeeklyReview> builder)
    {
        builder.ToTable("WeeklyReviews");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CompletionRate).Percentage().IsRequired();
        builder.Property(x => x.MainAchievements);
        builder.Property(x => x.Obstacles);
        builder.Property(x => x.LessonsLearned);
        builder.Property(x => x.NextWeekFocus);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.WeeklyPlanId).IsUnique();

        // A review is history: deleting the plan must not erase what was learned.
        builder.HasOne(x => x.WeeklyPlan)
            .WithMany()
            .HasForeignKey(x => x.WeeklyPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

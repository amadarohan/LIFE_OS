using LifeOS.Domain.Entities.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class WeeklyPlanConfiguration : IEntityTypeConfiguration<WeeklyPlan>
{
    public void Configure(EntityTypeBuilder<WeeklyPlan> builder)
    {
        builder.ToTable("WeeklyPlans");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Theme).HasMaxLength(ColumnConventions.NameLength);
        builder.Property(x => x.AvailableHours).Hours().IsRequired();
        builder.Property(x => x.PlannedHours).Hours().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Reflection);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.WeekStartDate).IsUnique();

        builder.HasMany(x => x.DailyPlans)
            .WithOne(x => x.WeeklyPlan)
            .HasForeignKey(x => x.WeeklyPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.DailyPlans).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class DailyPlanConfiguration : IEntityTypeConfiguration<DailyPlan>
{
    public void Configure(EntityTypeBuilder<DailyPlan> builder)
    {
        builder.ToTable("DailyPlans");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.MainFocus).HasMaxLength(ColumnConventions.NameLength);
        builder.Property(x => x.Reflection);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.WeeklyPlanId, x.PlanDate }).IsUnique();

        builder.HasMany(x => x.TimeBlocks)
            .WithOne(x => x.DailyPlan)
            .HasForeignKey(x => x.DailyPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.TimeBlocks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TimeBlockConfiguration : IEntityTypeConfiguration<TimeBlock>
{
    public void Configure(EntityTypeBuilder<TimeBlock> builder)
    {
        builder.ToTable("TimeBlocks", t =>
        {
            t.HasCheckConstraint("CK_TimeBlocks_ValidRange", "[EndAtUtc] > [StartAtUtc]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.BlockType).EnumAsString().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.StartAtUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.EndAtUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.StartAtUtc);

        builder.HasOne<Domain.Entities.Tasks.TaskItem>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Domain.Entities.Calendar.CalendarEvent>()
            .WithMany()
            .HasForeignKey(x => x.CalendarEventId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

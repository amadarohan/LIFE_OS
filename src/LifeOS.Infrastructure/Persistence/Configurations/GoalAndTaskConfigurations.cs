using LifeOS.Domain.Entities.Goals;
using LifeOS.Domain.Entities.LifeDomains;
using LifeOS.Domain.Entities.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("Goals", t =>
        {
            t.HasCheckConstraint("CK_Goals_ProgressRange", "[ProgressPercentage] >= 0 AND [ProgressPercentage] <= 100");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.WhyItMatters);
        builder.Property(x => x.SuccessCriteria);
        builder.Property(x => x.GoalType).EnumAsString().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Priority).EnumAsString().IsRequired();
        builder.Property(x => x.ProgressPercentage).Percentage().IsRequired();
        builder.Property(x => x.CompletedAtUtc).UtcTimestamp();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Status, x.TargetDate });
        builder.HasIndex(x => x.LifeDomainId);

        builder.HasOne<LifeDomain>()
            .WithMany()
            .HasForeignKey(x => x.LifeDomainId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.KeyResults)
            .WithOne(x => x.Goal)
            .HasForeignKey(x => x.GoalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.KeyResults).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class GoalKeyResultConfiguration : IEntityTypeConfiguration<GoalKeyResult>
{
    public void Configure(EntityTypeBuilder<GoalKeyResult> builder)
    {
        builder.ToTable("GoalKeyResults", t =>
        {
            t.HasCheckConstraint("CK_GoalKeyResults_Weight", "[Weight] > 0 AND [Weight] <= 100");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        builder.Property(x => x.InitialValue).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.CurrentValue).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.TargetValue).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Weight).Percentage().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.LastUpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.GoalId);
    }
}

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Priority).EnumAsString().IsRequired();
        builder.Property(x => x.Source).EnumAsString().IsRequired();
        builder.Property(x => x.RecurrenceRule).HasMaxLength(ColumnConventions.ShortTextLength);
        builder.Property(x => x.DeferReason).HasMaxLength(ColumnConventions.ShortTextLength);
        builder.Property(x => x.CompletedAtUtc).UtcTimestamp();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Status, x.DueDate });
        builder.HasIndex(x => new { x.Status, x.ScheduledDate });
        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.GoalId);
        builder.HasIndex(x => x.IsNextAction).HasFilter("[IsNextAction] = 1");

        builder.HasOne<Domain.Entities.Projects.Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Goal>()
            .WithMany()
            .HasForeignKey(x => x.GoalId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(x => x.ParentTaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.TimeEntries)
            .WithOne(x => x.Task)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.TimeEntries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("TaskDependencies", t =>
        {
            t.HasCheckConstraint("CK_TaskDependencies_NoSelf", "[TaskId] <> [DependsOnTaskId]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.TaskId, x.DependsOnTaskId }).IsUnique();

        builder.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.DependsOnTask)
            .WithMany()
            .HasForeignKey(x => x.DependsOnTaskId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class TaskTimeEntryConfiguration : IEntityTypeConfiguration<TaskTimeEntry>
{
    public void Configure(EntityTypeBuilder<TaskTimeEntry> builder)
    {
        builder.ToTable("TaskTimeEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.ShortTextLength);
        builder.Property(x => x.StartedAtUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.EndedAtUtc).UtcTimestamp().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.TaskId, x.StartedAtUtc });
    }
}

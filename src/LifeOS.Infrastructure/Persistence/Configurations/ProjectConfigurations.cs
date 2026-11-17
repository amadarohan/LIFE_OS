using LifeOS.Domain.Entities.LifeDomains;
using LifeOS.Domain.Entities.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class LifeDomainConfiguration : IEntityTypeConfiguration<LifeDomain>
{
    public void Configure(EntityTypeBuilder<LifeDomain> builder)
    {
        builder.ToTable("LifeDomains");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Color).HasMaxLength(ColumnConventions.ColorLength).IsRequired();
        builder.Property(x => x.Icon).HasMaxLength(ColumnConventions.IconLength).IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.SortOrder);

        builder.HasMany(x => x.Metrics)
            .WithOne(x => x.LifeDomain)
            .HasForeignKey(x => x.LifeDomainId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Metrics).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class LifeAreaMetricConfiguration : IEntityTypeConfiguration<LifeAreaMetric>
{
    public void Configure(EntityTypeBuilder<LifeAreaMetric> builder)
    {
        builder.ToTable("LifeAreaMetrics");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CurrentValue).HasPrecision(18, 4);
        builder.Property(x => x.TargetValue).HasPrecision(18, 4);
        builder.Property(x => x.MetricType).EnumAsString().IsRequired();
        builder.Property(x => x.MeasurementFrequency).EnumAsString().IsRequired();
        builder.Property(x => x.LastMeasuredAtUtc).UtcTimestamp();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.LifeDomainId, x.Name }).IsUnique();
    }
}

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", t =>
        {
            t.HasCheckConstraint("CK_Projects_ProgressRange", "[ProgressPercentage] >= 0 AND [ProgressPercentage] <= 100");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Notes);
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.Priority).EnumAsString().IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(ColumnConventions.CurrencyLength).IsRequired();
        builder.Property(x => x.EstimatedBudget).Money();
        builder.Property(x => x.ActualBudget).Money();
        builder.Property(x => x.EstimatedEffortHours).Hours();
        builder.Property(x => x.ActualEffortHours).Hours();
        builder.Property(x => x.ProgressPercentage).Percentage().IsRequired();
        builder.Property(x => x.CompletedAtUtc).UtcTimestamp();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Status, x.IsArchived });
        builder.HasIndex(x => x.LifeDomainId);
        builder.HasIndex(x => x.TargetEndDate);

        builder.HasOne<LifeDomain>()
            .WithMany()
            .HasForeignKey(x => x.LifeDomainId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Milestones)
            .WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.GoalLinks)
            .WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Milestones).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.GoalLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ProjectMilestoneConfiguration : IEntityTypeConfiguration<ProjectMilestone>
{
    public void Configure(EntityTypeBuilder<ProjectMilestone> builder)
    {
        builder.ToTable("ProjectMilestones");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.CompletedAtUtc).UtcTimestamp();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.ProjectId, x.SortOrder });
    }
}

public sealed class ProjectDependencyConfiguration : IEntityTypeConfiguration<ProjectDependency>
{
    public void Configure(EntityTypeBuilder<ProjectDependency> builder)
    {
        builder.ToTable("ProjectDependencies", t =>
        {
            t.HasCheckConstraint("CK_ProjectDependencies_NoSelf", "[ProjectId] <> [DependsOnProjectId]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DependencyType).EnumAsString().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.ProjectId, x.DependsOnProjectId }).IsUnique();

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.DependsOnProject)
            .WithMany()
            .HasForeignKey(x => x.DependsOnProjectId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class GoalProjectConfiguration : IEntityTypeConfiguration<GoalProject>
{
    public void Configure(EntityTypeBuilder<GoalProject> builder)
    {
        builder.ToTable("GoalProjects");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContributionWeight).Percentage().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.GoalId, x.ProjectId }).IsUnique();

        builder.HasOne(x => x.Project)
            .WithMany(x => x.GoalLinks)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Domain.Entities.Goals.Goal>()
            .WithMany()
            .HasForeignKey(x => x.GoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using LifeOS.Domain.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(ColumnConventions.KeyLength).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(ColumnConventions.MediumTextLength).IsRequired();
        builder.Property(x => x.ValueType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.ShortTextLength);

        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.Key).IsUnique();
    }
}

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OriginalFileName).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(ColumnConventions.MediumTextLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(ColumnConventions.EntityTypeLength).IsRequired();
        builder.Property(x => x.Checksum).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityType).HasMaxLength(ColumnConventions.EntityTypeLength).IsRequired();
        builder.Property(x => x.ActionType).EnumAsString().IsRequired();
        builder.Property(x => x.Source).EnumAsString().IsRequired();
        builder.Property(x => x.OldValuesJson);
        builder.Property(x => x.NewValuesJson);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

public sealed class BackupHistoryConfiguration : IEntityTypeConfiguration<BackupHistory>
{
    public void Configure(EntityTypeBuilder<BackupHistory> builder)
    {
        builder.ToTable("BackupHistories");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(ColumnConventions.MediumTextLength).IsRequired();
        builder.Property(x => x.BackupType).EnumAsString().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

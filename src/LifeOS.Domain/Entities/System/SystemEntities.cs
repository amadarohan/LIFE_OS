using LifeOS.Domain.Common;

namespace LifeOS.Domain.Entities.System;

public sealed class AppSetting : AuditableEntity
{
    private AppSetting() { }

    public AppSetting(string key, string value, string valueType, string? description, bool isSensitive)
    {
        Key = key;
        Value = value;
        ValueType = valueType;
        Description = description;
        IsSensitive = isSensitive;
    }

    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string ValueType { get; private set; } = "string";
    public string? Description { get; private set; }
    public bool IsSensitive { get; private set; }

    public void SetValue(string value) => Value = value;
    public void SetDescription(string? description) => Description = description;
}

public sealed class Attachment : Entity
{
    private Attachment() { }

    public Attachment(
        string originalFileName,
        string storedFileName,
        string relativePath,
        string contentType,
        long fileSizeBytes,
        string checksum)
    {
        OriginalFileName = originalFileName;
        StoredFileName = storedFileName;
        RelativePath = relativePath;
        ContentType = contentType;
        FileSizeBytes = fileSizeBytes;
        Checksum = checksum;
    }

    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public string RelativePath { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string Checksum { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void StampCreated(DateTimeOffset now) => CreatedAtUtc = now;
}

public sealed class AuditLog : Entity
{
    private AuditLog() { }

    public AuditLog(
        string entityType,
        Guid entityId,
        Enums.AuditActionType actionType,
        string? oldValuesJson,
        string? newValuesJson,
        Enums.AuditSource source,
        DateTimeOffset createdAtUtc)
    {
        EntityType = entityType;
        EntityId = entityId;
        ActionType = actionType;
        OldValuesJson = oldValuesJson;
        NewValuesJson = newValuesJson;
        Source = source;
        CreatedAtUtc = createdAtUtc;
    }

    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public Enums.AuditActionType ActionType { get; private set; }
    public string? OldValuesJson { get; private set; }
    public string? NewValuesJson { get; private set; }
    public Enums.AuditSource Source { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}

public sealed class BackupHistory : Entity
{
    private BackupHistory() { }

    public BackupHistory(string fileName, string relativePath, Enums.BackupType backupType, DateTimeOffset createdAtUtc)
    {
        FileName = fileName;
        RelativePath = relativePath;
        BackupType = backupType;
        Status = Enums.BackupStatus.InProgress;
        CreatedAtUtc = createdAtUtc;
    }

    public string FileName { get; private set; } = string.Empty;
    public string RelativePath { get; private set; } = string.Empty;
    public Enums.BackupType BackupType { get; private set; }
    public long FileSizeBytes { get; private set; }
    public Enums.BackupStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void Complete(long fileSizeBytes)
    {
        FileSizeBytes = fileSizeBytes;
        Status = Enums.BackupStatus.Completed;
        ErrorMessage = null;
    }

    public void Fail(string errorMessage)
    {
        Status = Enums.BackupStatus.Failed;
        ErrorMessage = errorMessage;
    }
}

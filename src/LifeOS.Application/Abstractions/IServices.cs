namespace LifeOS.Application.Abstractions;

/// <summary>Abstracts the system clock so time-dependent behaviour is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly TodayUtc { get; }
}

/// <summary>Resolves the user's configured timezone and calendar preferences.</summary>
public interface IUserTimeZone
{
    TimeZoneInfo TimeZone { get; }
    DateOnly ToLocalDate(DateTimeOffset utcInstant);
    DateTimeOffset ToUtc(DateOnly localDate, TimeOnly localTime);
    DateOnly TodayLocal { get; }
}

/// <summary>Local file storage for attachments and backups, confined to configured roots.</summary>
public interface IFileStorage
{
    Task<string> SaveAttachmentAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenAttachmentAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteAttachmentAsync(string relativePath, CancellationToken cancellationToken = default);

    string GetAttachmentAbsolutePath(string relativePath);
}

/// <summary>Encrypts sensitive setting values at rest.</summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
    bool IsProtected(string value);
}

/// <summary>Writes audit records for important application changes.</summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string entityType,
        Guid entityId,
        Domain.Enums.AuditActionType actionType,
        string? oldValuesJson,
        string? newValuesJson,
        Domain.Enums.AuditSource source,
        CancellationToken cancellationToken = default);
}

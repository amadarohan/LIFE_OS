using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.System;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Backup;

public sealed record BackupView(
    Guid Id,
    string FileName,
    string RelativePath,
    BackupType BackupType,
    long FileSizeBytes,
    BackupStatus Status,
    DateTimeOffset CreatedAtUtc,
    string? ErrorMessage);

/// <summary>
/// Backup orchestration. Restore is deliberately guarded: it requires an explicit confirmation
/// token from the caller and always takes a pre-restore snapshot first, so a bad restore is
/// recoverable.
/// </summary>
public sealed class BackupService
{
    private readonly IApplicationDbContext _db;
    private readonly IDatabaseBackupStore _store;
    private readonly IClock _clock;

    public BackupService(IApplicationDbContext db, IDatabaseBackupStore store, IClock clock)
    {
        _db = db;
        _store = store;
        _clock = clock;
    }

    public async Task<IReadOnlyList<BackupView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var backups = await _db.ToListAsync(
            _db.BackupHistories.OrderByDescending(b => b.CreatedAtUtc),
            cancellationToken);

        return backups
            .Select(b => new BackupView(
                b.Id,
                b.FileName,
                b.RelativePath,
                b.BackupType,
                b.FileSizeBytes,
                b.Status,
                b.CreatedAtUtc,
                b.ErrorMessage))
            .ToList();
    }

    public Task<Result<Guid>> CreateBackupAsync(
        BackupType backupType = BackupType.Manual,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var now = _clock.UtcNow;
            var fileName = $"lifeos-{now:yyyyMMdd-HHmmss}-{backupType.ToString().ToLowerInvariant()}.bak";
            var absolutePath = Path.Combine(_store.BackupDirectory, fileName);

            Directory.CreateDirectory(_store.BackupDirectory);

            var history = new BackupHistory(fileName, fileName, backupType, now);
            _db.Add(history);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var size = await _store.BackupAsync(absolutePath, cancellationToken);
                history.Complete(size);
            }
            catch (Exception ex)
            {
                history.Fail(ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(history.Id);
        });

    /// <summary>
    /// Restores from a recorded backup. <paramref name="confirmation"/> must equal the backup file
    /// name; this prevents an accidental click from replacing the live database.
    /// </summary>
    public Task<Result> RestoreAsync(
        Guid backupId,
        string confirmation,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var backup = await _db.FirstOrDefaultAsync(
                _db.BackupHistories.Where(b => b.Id == backupId),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(BackupHistory), backupId);

            if (backup.Status != BackupStatus.Completed)
            {
                throw new DomainRuleException(
                    "Backup.NotRestorable",
                    "این نسخه پشتیبان با موفقیت تکمیل نشده و قابل بازیابی نیست.");
            }

            if (!string.Equals(confirmation?.Trim(), backup.FileName, StringComparison.Ordinal))
            {
                throw new DomainRuleException(
                    "Backup.ConfirmationMismatch",
                    "برای بازیابی باید نام فایل پشتیبان را دقیقاً تأیید کنید.");
            }

            var absolutePath = Path.Combine(_store.BackupDirectory, backup.RelativePath);
            if (!File.Exists(absolutePath))
            {
                throw new DomainRuleException(
                    "Backup.FileMissing",
                    "فایل پشتیبان روی دیسک یافت نشد.");
            }

            // Safety net: snapshot the current state before overwriting it.
            var preRestoreName = $"lifeos-prerestore-{_clock.UtcNow:yyyyMMdd-HHmmss}.bak";
            var preRestorePath = Path.Combine(_store.BackupDirectory, preRestoreName);
            var preRestore = new BackupHistory(
                preRestoreName,
                preRestoreName,
                BackupType.PreRestore,
                _clock.UtcNow);

            _db.Add(preRestore);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var size = await _store.BackupAsync(preRestorePath, cancellationToken);
                preRestore.Complete(size);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                preRestore.Fail(ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new DomainRuleException(
                    "Backup.PreRestoreFailed",
                    $"تهیه نسخه پشتیبان پیش از بازیابی ناموفق بود: {ex.Message}");
            }

            await _store.RestoreAsync(absolutePath, cancellationToken);
        });

    /// <summary>Deletes backup files beyond the retention count, newest kept.</summary>
    public Task<Result<int>> PruneAsync(int keep, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            if (keep < 1)
            {
                throw new DomainRuleException("Backup.InvalidRetention", "تعداد نسخه‌های نگهداری‌شده باید حداقل ۱ باشد.");
            }

            var backups = await _db.ToListAsync(
                _db.BackupHistories
                    .Where(b => b.Status == BackupStatus.Completed)
                    .OrderByDescending(b => b.CreatedAtUtc),
                cancellationToken);

            var toDelete = backups.Skip(keep).ToList();
            if (toDelete.Count == 0)
            {
                return Result.Success(0);
            }

            foreach (var backup in toDelete)
            {
                var absolutePath = Path.Combine(_store.BackupDirectory, backup.RelativePath);
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                _db.Remove(backup);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(toDelete.Count);
        });
}

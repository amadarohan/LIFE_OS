namespace LifeOS.Application.Abstractions;

/// <summary>
/// Physical database backup and restore. Kept behind an abstraction so the Application layer never
/// references a database driver, and so tests can substitute a fake.
/// </summary>
public interface IDatabaseBackupStore
{
    /// <summary>Writes a full backup to <paramref name="absolutePath"/> and returns the file size.</summary>
    Task<long> BackupAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the database from <paramref name="absolutePath"/>. Destructive: the current
    /// database is replaced.
    /// </summary>
    Task RestoreAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>Directory where backups are written, as an absolute path.</summary>
    string BackupDirectory { get; }
}

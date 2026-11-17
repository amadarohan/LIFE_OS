using LifeOS.Application.Abstractions;
using Microsoft.Data.SqlClient;

namespace LifeOS.Infrastructure.Services;

/// <summary>
/// SQL Server native backup/restore. Uses T-SQL BACKUP/RESTORE rather than copying files so the
/// backup is transactionally consistent and restorable by standard tooling.
/// </summary>
public sealed class SqlServerBackupStore : IDatabaseBackupStore
{
    private readonly string _connectionString;
    private readonly string _databaseName;

    public SqlServerBackupStore(string connectionString, string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;

        if (string.IsNullOrWhiteSpace(_databaseName))
        {
            throw new InvalidOperationException(
                "The connection string must specify a database name for backup and restore.");
        }

        BackupDirectory = Path.GetFullPath(backupDirectory);
    }

    public string BackupDirectory { get; }

    public async Task<long> BackupAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(absolutePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // The path is embedded in T-SQL, so it must be escaped as a literal, not concatenated raw.
        var sql = $"""
            BACKUP DATABASE {Quote(_databaseName)}
            TO DISK = {Literal(fullPath)}
            WITH INIT, FORMAT, CHECKSUM, STATS = 10;
            """;

        await ExecuteNonQueryAsync(sql, cancellationToken);

        var info = new FileInfo(fullPath);
        return info.Exists ? info.Length : 0L;
    }

    public async Task RestoreAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(absolutePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Backup file was not found.", fullPath);
        }

        var quotedDb = Quote(_databaseName);

        // Single-user mode is required because the app's own connections would otherwise block the
        // restore. MULTI_USER is restored in a finally block so a failure cannot strand the database.
        await ExecuteNonQueryAsync(
            $"ALTER DATABASE {quotedDb} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;",
            cancellationToken);

        try
        {
            await ExecuteNonQueryAsync(
                $"RESTORE DATABASE {quotedDb} FROM DISK = {Literal(fullPath)} WITH REPLACE, CHECKSUM;",
                cancellationToken);
        }
        finally
        {
            await ExecuteNonQueryAsync(
                $"ALTER DATABASE {quotedDb} SET MULTI_USER;",
                CancellationToken.None);
        }
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0; // Backups can legitimately run for minutes.

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Quotes an identifier, escaping embedded closing brackets.</summary>
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    /// <summary>Builds a T-SQL string literal, escaping embedded single quotes.</summary>
    private static string Literal(string value) => $"N'{value.Replace("'", "''")}'";
}

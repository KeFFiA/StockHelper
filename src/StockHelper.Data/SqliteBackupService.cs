using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using StockHelper.Core.Abstractions;

namespace StockHelper.Data;

/// <summary>
/// SQLite backups via the online backup API (consistent even while the database is open).
/// File name: <c>stockhelper_yyyyMMdd_HHmmss_reason.db</c>.
/// </summary>
public sealed class SqliteBackupService(DatabaseOptions options, ILogger<SqliteBackupService> logger) : IBackupService
{
    private const string Prefix = "stockhelper_";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";

    private string DatabasePath => new SqliteConnectionStringBuilder(options.ConnectionString).DataSource;

    public bool DatabaseExists => File.Exists(DatabasePath);

    public async Task<string> CreateBackupAsync(string reason, CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.BackupsDirectory);
        var safeReason = new string([.. reason.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
        var path = Path.Combine(options.BackupsDirectory, $"{Prefix}{DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{safeReason}.db");
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(options.BackupsDirectory, $"{Prefix}{DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{safeReason}_{i}.db");
        }

        await Task.Run(() =>
        {
            using var source = new SqliteConnection(options.ConnectionString);
            using var target = new SqliteConnection($"Data Source={path};Pooling=False");
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }, ct);

        logger.LogInformation("Backup created: {Path}", path);
        return path;
    }

    public Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(options.BackupsDirectory))
        {
            return Task.FromResult<IReadOnlyList<BackupInfo>>([]);
        }

        IReadOnlyList<BackupInfo> list = [.. new DirectoryInfo(options.BackupsDirectory)
            .GetFiles($"{Prefix}*.db")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo(f.FullName, f.Name, f.CreationTimeUtc, f.Length))];
        return Task.FromResult(list);
    }

    public async Task RestoreAsync(string backupPath, CancellationToken ct = default)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup not found.", backupPath);
        }

        // Safety net: the current state can always be brought back.
        await CreateBackupAsync("beforerestore", ct);

        SqliteConnection.ClearAllPools();
        await Task.Run(() =>
        {
            using var source = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
            using var target = new SqliteConnection(options.ConnectionString);
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }, ct);
        SqliteConnection.ClearAllPools();

        logger.LogWarning("Database restored from {Path}", backupPath);
    }

    public void Prune(int keep)
    {
        if (keep < 1 || !Directory.Exists(options.BackupsDirectory))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(options.BackupsDirectory).GetFiles($"{Prefix}*.db").OrderByDescending(f => f.CreationTimeUtc).Skip(keep))
        {
            try
            {
                file.Delete();
                logger.LogInformation("Old backup removed: {Name}", file.Name);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not remove old backup {Name}", file.Name);
            }
        }
    }
}

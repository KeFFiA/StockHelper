using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.Data.Tests;

/// <summary>Backup/restore on a real database file in a temp folder.</summary>
public sealed class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "stockhelper-tests", Guid.NewGuid().ToString("N"));
    private readonly DatabaseOptions _options;

    public BackupTests()
    {
        Directory.CreateDirectory(_dir);
        _options = new DatabaseOptions
        {
            ConnectionString = $"Data Source={Path.Combine(_dir, "stockhelper.db")}",
            BackupsDirectory = Path.Combine(_dir, "backups"),
        };
    }

    private StockHelperDbContext Create() =>
        new(new DbContextOptionsBuilder<StockHelperDbContext>().UseSqlite(_options.ConnectionString).Options);

    [Fact]
    public async Task Backup_ThenRestore_BringsDataBack_AndKeepsSafetyCopy()
    {
        await using (var db = Create())
        {
            await db.Database.MigrateAsync();
            db.Categories.Add(new Category { Name = "Before", CreatedBy = "t" });
            await db.SaveChangesAsync();
        }

        var service = new SqliteBackupService(_options, NullLogger<SqliteBackupService>.Instance);
        var backup = await service.CreateBackupAsync("manual");

        await using (var db = Create())
        {
            db.Categories.Add(new Category { Name = "After", CreatedBy = "t" });
            await db.SaveChangesAsync();
        }

        await service.RestoreAsync(backup);

        await using (var db = Create())
        {
            Assert.Equal(["Before"], await db.Categories.Select(c => c.Name).ToListAsync());
        }

        var backups = await service.GetBackupsAsync();
        Assert.Equal(2, backups.Count);
        Assert.Contains(backups, b => b.FileName.Contains("beforerestore"));
    }

    [Fact]
    public async Task Prune_KeepsNewest()
    {
        await using (var db = Create())
        {
            await db.Database.MigrateAsync();
        }

        var service = new SqliteBackupService(_options, NullLogger<SqliteBackupService>.Instance);
        for (var i = 0; i < 4; i++)
        {
            var path = await service.CreateBackupAsync($"b{i}");
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(i));
        }

        service.Prune(2);

        var left = await service.GetBackupsAsync();
        Assert.Equal(["b3", "b2"], left.Select(b => b.FileName.Split('_')[^1].Replace(".db", string.Empty)));
    }

    [Fact]
    public async Task Initializer_BacksUpBeforeMigratingExistingDatabase_NotOnFirstRun()
    {
        var factory = new Factory(_options);
        var backups = new SqliteBackupService(_options, NullLogger<SqliteBackupService>.Instance);
        var initializer = new DatabaseInitializer(factory, backups, NullLogger<DatabaseInitializer>.Instance);
        var seed = new Core.Abstractions.SeedData(["шт"], "Склад");

        await initializer.InitializeAsync(seed);
        Assert.Empty(await backups.GetBackupsAsync());

        // Simulate an older schema: forget the last migration so it is pending again.
        await using (var db = Create())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory VALUES ('00000000000000_Old', '10.0.0')");
            // Drop every application table so the full schema is "pending" again.
            var tables = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'").ToListAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            foreach (var table in tables)
            {
#pragma warning disable EF1002 // Table names come from sqlite_master, not from user input.
                await db.Database.ExecuteSqlRawAsync($"DROP TABLE \"{table}\";");
#pragma warning restore EF1002
            }
        }

        await initializer.InitializeAsync(seed);

        var created = Assert.Single(await backups.GetBackupsAsync());
        Assert.Contains("premigration", created.FileName);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Factory(DatabaseOptions options) : IDbContextFactory<StockHelperDbContext>
    {
        public StockHelperDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<StockHelperDbContext>().UseSqlite(options.ConnectionString).Options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.Data;

public sealed class DatabaseInitializer(
    IDbContextFactory<StockHelperDbContext> factory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(SeedData seed, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync(ct);
        }

        await SeedAsync(db, seed, ct);
    }

    private async Task SeedAsync(StockHelperDbContext db, SeedData seed, CancellationToken ct)
    {
        var changed = false;

        if (!await db.Units.AnyAsync(ct))
        {
            db.Units.AddRange(seed.Units.Select(name => new Unit { Name = name }));
            changed = true;
        }

        if (!await db.Categories.AnyAsync(ct))
        {
            db.Categories.AddRange(seed.Categories.Select(name => new Category { Name = name }));
            changed = true;
        }

        if (!await db.StorageLocations.AnyAsync(ct))
        {
            db.StorageLocations.Add(new StorageLocation { Name = seed.DefaultLocation });
            changed = true;
        }

        if (changed)
        {
            logger.LogInformation("Seeding reference data");
            await db.SaveChangesAsync(ct);
        }
    }
}

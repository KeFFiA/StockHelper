using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StockHelper.Core.Entities;

namespace StockHelper.Data;

public sealed class StockHelperDbContext(DbContextOptions<StockHelperDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<OpenPackage> OpenPackages => Set<OpenPackage>();

    public DbSet<StockTake> StockTakes => Set<StockTake>();

    public DbSet<StockTakeLine> StockTakeLines => Set<StockTakeLine>();

    public DbSet<User> Users => Set<User>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Quantities and money: fixed precision on every provider.
        builder.Properties<decimal>().HavePrecision(18, 4);

        // All dates are stored in UTC; restore DateTimeKind.Utc on read (SQLite does not keep it).
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockHelperDbContext).Assembly);
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}

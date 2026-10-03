using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Entities;
using StockHelper.Core.Security;

namespace StockHelper.Data.Tests;

/// <summary>In-memory SQLite database with all migrations applied; lives as long as the test.</summary>
public sealed class TestDatabase : IDbContextFactory<StockHelperDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<StockHelperDbContext> _options;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<StockHelperDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditInterceptor(CurrentUser, TimeProvider.System))
            .Options;

        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public FakeCurrentUser CurrentUser { get; } = new();

    public StockHelperDbContext CreateDbContext() => new(_options);

    public async Task<(Category Category, Unit Unit, StorageLocation Location)> SeedReferencesAsync()
    {
        await using var db = CreateDbContext();
        var category = new Category { Name = "Канцелярия" };
        var unit = new Unit { Name = "шт" };
        var location = new StorageLocation { Name = "Склад" };
        db.AddRange(category, unit, location);
        await db.SaveChangesAsync();
        return (category, unit, location);
    }

    public void Dispose() => _connection.Dispose();
}

public sealed class FakeCurrentUser : ICurrentUserService
{
    public User? User { get; private set; } = new() { Id = 1, Login = "tester", DisplayName = "Tester", Role = UserRole.Administrator };

    public event EventHandler? Changed;

    public void SignIn(User user)
    {
        User = user;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        User = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

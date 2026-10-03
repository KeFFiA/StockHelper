using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Data.Repositories;

namespace StockHelper.Data.Tests;

public sealed class IssueRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();

    private IssueRepository Repo => new(_db, _db.CurrentUser);

    private async Task<Item> CreateItemAsync()
    {
        var (category, unit, _) = await _db.SeedReferencesAsync();
        return await new ItemRepository(_db).AddAsync(new Item { Name = "Краска белая", CategoryId = category.Id, UnitId = unit.Id });
    }

    [Fact]
    public async Task Issue_Return_AndOnHandFilter()
    {
        var item = await CreateItemAsync();
        var issue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, Date = DateTime.UtcNow, IssuedTo = " Иванов ", ExpectReturn = true });

        Assert.Single(await Repo.GetAsync(new IssueFilter(OnlyOnHand: true)));
        Assert.Equal(["Иванов"], await Repo.GetRecipientsAsync());

        await Repo.ReturnAsync(issue.Id, 2.5m, DateTime.UtcNow);

        Assert.Empty(await Repo.GetAsync(new IssueFilter(OnlyOnHand: true)));
        var stored = Assert.Single(await Repo.GetAsync(new IssueFilter()));
        Assert.Equal(2.5m, stored.NetQuantity);
        Assert.Equal("tester", stored.ReturnedBy);

        var again = await Assert.ThrowsAsync<DomainException>(() => Repo.ReturnAsync(issue.Id, 1, DateTime.UtcNow));
        Assert.Equal(DomainErrorCode.AlreadyReturned, again.Code);

        await Repo.CancelReturnAsync(issue.Id);
        Assert.Single(await Repo.GetAsync(new IssueFilter(OnlyOnHand: true)));
    }

    [Fact]
    public async Task Return_MoreThanIssued_Throws()
    {
        var item = await CreateItemAsync();
        var issue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, Date = DateTime.UtcNow });

        var ex = await Assert.ThrowsAsync<DomainException>(() => Repo.ReturnAsync(issue.Id, 6, DateTime.UtcNow));
        Assert.Equal(DomainErrorCode.ReturnExceedsIssued, ex.Code);
    }

    [Fact]
    public async Task ItemWithIssues_HasHistory()
    {
        var item = await CreateItemAsync();
        await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 1, Date = DateTime.UtcNow });

        Assert.True(await new ItemRepository(_db).HasHistoryAsync(item.Id));
    }

    [Fact]
    public async Task Snapshot_IncludesIssues()
    {
        var item = await CreateItemAsync();
        await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 3, Date = DateTime.UtcNow });

        var snapshot = await new StockDataReader(_db).LoadAsync();

        Assert.Equal(3, Assert.Single(snapshot.Issues).Quantity);
    }

    public void Dispose() => _db.Dispose();
}

public sealed class PackageUnitTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task Package_RefersToBaseUnit_NotToAnotherPackage()
    {
        var units = new UnitRepository(_db);
        var liter = await units.AddAsync(new Unit { Name = "л" });
        var can = await units.AddAsync(new Unit { Name = "Банка 5 л", BaseUnitId = liter.Id, Factor = 5 });

        Assert.Equal(5, (await units.GetAllAsync(false)).Single(u => u.Id == can.Id).Factor);

        var nested = await Assert.ThrowsAsync<DomainException>(() =>
            units.AddAsync(new Unit { Name = "Ящик банок", BaseUnitId = can.Id, Factor = 4 }));
        Assert.Equal(DomainErrorCode.PackageOfPackage, nested.Code);

        var zero = await Assert.ThrowsAsync<DomainException>(() =>
            units.AddAsync(new Unit { Name = "Пустая", BaseUnitId = liter.Id, Factor = 0 }));
        Assert.Equal(DomainErrorCode.InvalidUnitFactor, zero.Code);

        var inUse = await Assert.ThrowsAsync<DomainException>(() => units.DeleteAsync(liter.Id));
        Assert.Equal(DomainErrorCode.InUse, inUse.Code);
    }

    [Fact]
    public async Task BaseUnit_KeepsFactorOne()
    {
        var unit = await new UnitRepository(_db).AddAsync(new Unit { Name = "кг", Factor = 7 });

        Assert.Equal(1, unit.Factor);
    }

    public void Dispose() => _db.Dispose();
}

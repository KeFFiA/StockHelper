using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Data.Repositories;

namespace StockHelper.Data.Tests;

public sealed class OpenPackageTests : IDisposable
{
    private readonly TestDatabase _db = new();

    private IssueRepository Repo => new(_db, _db.CurrentUser);

    private async Task<(Item Item, Unit Can)> CreatePaintAsync()
    {
        var (category, liter, _) = await _db.SeedReferencesAsync();
        var can = await new UnitRepository(_db).AddAsync(new Unit { Name = "Банка 5", BaseUnitId = liter.Id, Factor = 5 });
        var item = await new ItemRepository(_db).AddAsync(new Item { Name = "Краска", CategoryId = category.Id, UnitId = liter.Id });
        return (item, can);
    }

    [Fact]
    public async Task ReturnWithRemainder_CreatesOpenPackage_WhichIsClosedWhenIssuedAgain()
    {
        var (item, can) = await CreatePaintAsync();
        var first = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, UnitId = can.Id, UnitQuantity = 1, Date = DateTime.UtcNow.AddDays(-2), ExpectReturn = true });

        await Repo.ReturnAsync(first.Id, 2, DateTime.UtcNow.AddDays(-1));

        var package = Assert.Single(await Repo.GetOpenPackagesAsync(item.Id));
        Assert.Equal(2, package.Quantity);
        Assert.Equal(can.Id, package.UnitId);

        await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 2, Date = DateTime.UtcNow, OpenPackageId = package.Id });

        Assert.Empty(await Repo.GetOpenPackagesAsync(item.Id));
    }

    [Fact]
    public async Task ReturnOfNothing_DoesNotCreatePackage()
    {
        var (item, _) = await CreatePaintAsync();
        var issue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, Date = DateTime.UtcNow });

        await Repo.ReturnAsync(issue.Id, 0, DateTime.UtcNow);

        Assert.Empty(await Repo.GetOpenPackagesAsync());
    }

    [Fact]
    public async Task CancelReturn_RemovesPackage_UnlessAlreadyReissued()
    {
        var (item, _) = await CreatePaintAsync();
        var issue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, Date = DateTime.UtcNow.AddHours(-3) });
        await Repo.ReturnAsync(issue.Id, 1, DateTime.UtcNow.AddHours(-2));

        await Repo.CancelReturnAsync(issue.Id);
        Assert.Empty(await Repo.GetOpenPackagesAsync());

        await Repo.ReturnAsync(issue.Id, 1, DateTime.UtcNow.AddHours(-2));
        var package = Assert.Single(await Repo.GetOpenPackagesAsync());
        await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 1, Date = DateTime.UtcNow, OpenPackageId = package.Id });

        var ex = await Assert.ThrowsAsync<DomainException>(() => Repo.CancelReturnAsync(issue.Id));
        Assert.Equal(DomainErrorCode.OpenPackageAlreadyIssued, ex.Code);
    }

    [Fact]
    public async Task DeletingReissue_PutsPackageBackOnShelf()
    {
        var (item, _) = await CreatePaintAsync();
        var issue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, Date = DateTime.UtcNow.AddHours(-3) });
        await Repo.ReturnAsync(issue.Id, 3, DateTime.UtcNow.AddHours(-2));
        var package = Assert.Single(await Repo.GetOpenPackagesAsync());
        var reissue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 3, Date = DateTime.UtcNow, OpenPackageId = package.Id });

        await Repo.DeleteAsync(reissue.Id);

        Assert.Single(await Repo.GetOpenPackagesAsync());
    }

    [Fact]
    public async Task ClosedPackage_CannotBeIssuedTwice()
    {
        var (item, _) = await CreatePaintAsync();
        var issue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, Date = DateTime.UtcNow.AddHours(-3) });
        await Repo.ReturnAsync(issue.Id, 3, DateTime.UtcNow.AddHours(-2));
        var package = Assert.Single(await Repo.GetOpenPackagesAsync());
        await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 3, Date = DateTime.UtcNow, OpenPackageId = package.Id });

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 3, Date = DateTime.UtcNow, OpenPackageId = package.Id }));
        Assert.Equal(DomainErrorCode.OpenPackageUnavailable, ex.Code);
    }

    [Fact]
    public async Task WriteOff_ConsumesTheRemainder()
    {
        var (item, _) = await CreatePaintAsync();
        var issue = await Repo.AddAsync(new Issue { ItemId = item.Id, Quantity = 5, Date = DateTime.UtcNow.AddHours(-3) });
        await Repo.ReturnAsync(issue.Id, 1.5m, DateTime.UtcNow.AddHours(-2));
        var package = Assert.Single(await Repo.GetOpenPackagesAsync());

        await Repo.WriteOffAsync(package.Id, "Засохла");

        Assert.Empty(await Repo.GetOpenPackagesAsync());
        var writeOff = (await Repo.GetAsync(new IssueFilter())).Single(i => i.OpenPackageId == package.Id);
        Assert.Equal(1.5m, writeOff.Quantity);
        Assert.Equal("Засохла", writeOff.Note);
    }

    public void Dispose() => _db.Dispose();
}

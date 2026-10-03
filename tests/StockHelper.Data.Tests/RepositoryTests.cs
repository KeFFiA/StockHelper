using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Data.Repositories;

namespace StockHelper.Data.Tests;

public sealed class LookupRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task Add_FillsAuditFields()
    {
        var repo = new CategoryRepository(_db);

        var category = await repo.AddAsync(new Category { Name = "  Бумага  " });

        Assert.Equal("Бумага", category.Name);
        Assert.Equal("tester", category.CreatedBy);
        Assert.True(category.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(DateTimeKind.Utc, (await repo.GetAllAsync(false))[0].CreatedAt.Kind);
    }

    [Fact]
    public async Task Add_DuplicateNameIgnoringCase_Throws()
    {
        var repo = new UnitRepository(_db);
        await repo.AddAsync(new Unit { Name = "Шт" });

        var ex = await Assert.ThrowsAsync<DomainException>(() => repo.AddAsync(new Unit { Name = "шт" }));
        Assert.Equal(DomainErrorCode.NameNotUnique, ex.Code);
    }

    [Fact]
    public async Task Update_WithStaleStamp_ThrowsConcurrencyConflict()
    {
        var repo = new CategoryRepository(_db);
        var created = await repo.AddAsync(new Category { Name = "A" });

        var first = (await repo.GetAllAsync(true)).Single();
        var second = (await repo.GetAllAsync(true)).Single();

        first.Name = "B";
        await repo.UpdateAsync(first);

        second.Name = "C";
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repo.UpdateAsync(second));
        Assert.Equal("B", (await repo.GetAllAsync(true)).Single().Name);
        Assert.NotEqual(created.ConcurrencyStamp, (await repo.GetAllAsync(true)).Single().ConcurrencyStamp);
    }

    [Fact]
    public async Task Delete_UsedCategory_ThrowsInUse()
    {
        var (category, unit, _) = await _db.SeedReferencesAsync();
        await new ItemRepository(_db).AddAsync(new Item { Name = "Ручка", CategoryId = category.Id, UnitId = unit.Id });

        var ex = await Assert.ThrowsAsync<DomainException>(() => new CategoryRepository(_db).DeleteAsync(category.Id));
        Assert.Equal(DomainErrorCode.InUse, ex.Code);
    }

    [Fact]
    public async Task GetAll_ExcludesArchivedUnlessRequested()
    {
        var repo = new StorageLocationRepository(_db);
        var location = await repo.AddAsync(new StorageLocation { Name = "Полка" });
        await repo.SetArchivedAsync(location.Id, true);

        Assert.Empty(await repo.GetAllAsync(false));
        Assert.Single(await repo.GetAllAsync(true));
    }

    public void Dispose() => _db.Dispose();
}

public sealed class ItemRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task Delete_ItemWithHistory_ThrowsInUse_ButCanBeArchived()
    {
        var (category, unit, _) = await _db.SeedReferencesAsync();
        var items = new ItemRepository(_db);
        var item = await items.AddAsync(new Item { Name = "Бумага А4", CategoryId = category.Id, UnitId = unit.Id, Price = 300m });
        await new ReceiptRepository(_db).AddAsync(new Receipt { ItemId = item.Id, Quantity = 5, Price = 300m, Date = DateTime.UtcNow });

        Assert.True(await items.HasHistoryAsync(item.Id));
        var ex = await Assert.ThrowsAsync<DomainException>(() => items.DeleteAsync(item.Id));
        Assert.Equal(DomainErrorCode.InUse, ex.Code);

        await items.SetArchivedAsync(item.Id, true);
        Assert.Empty(await items.GetAllAsync(false));
    }

    [Fact]
    public async Task Delete_ItemWithoutHistory_Removes()
    {
        var (category, unit, _) = await _db.SeedReferencesAsync();
        var items = new ItemRepository(_db);
        var item = await items.AddAsync(new Item { Name = "Скрепки", CategoryId = category.Id, UnitId = unit.Id });

        await items.DeleteAsync(item.Id);

        Assert.Empty(await items.GetAllAsync(true));
    }

    [Fact]
    public async Task Add_NegativeMinStock_Throws()
    {
        var (category, unit, _) = await _db.SeedReferencesAsync();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ItemRepository(_db).AddAsync(new Item { Name = "X", CategoryId = category.Id, UnitId = unit.Id, MinStock = -1 }));
        Assert.Equal(DomainErrorCode.QuantityCannotBeNegative, ex.Code);
    }

    [Fact]
    public async Task Decimals_RoundTripExactly()
    {
        var (category, unit, _) = await _db.SeedReferencesAsync();
        var items = new ItemRepository(_db);
        var item = await items.AddAsync(new Item { Name = "Краска", CategoryId = category.Id, UnitId = unit.Id, MinStock = 1.25m, Price = 1234.5678m });

        var loaded = await items.GetAsync(item.Id);

        Assert.Equal(1.25m, loaded!.MinStock);
        Assert.Equal(1234.5678m, loaded.Price);
        Assert.Equal("Канцелярия", loaded.Category!.Name);
    }

    public void Dispose() => _db.Dispose();
}

public sealed class StockTakeRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();

    private StockTakeRepository Repo => new(_db, _db.CurrentUser, TimeProvider.System);

    [Fact]
    public async Task OnlyOneDraftAllowed()
    {
        await Repo.CreateDraftAsync(DateTime.UtcNow, null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Repo.CreateDraftAsync(DateTime.UtcNow.AddDays(1), null));
        Assert.Equal(DomainErrorCode.DraftStockTakeExists, ex.Code);
    }

    [Fact]
    public async Task SetLine_UpsertsAndRemoves_AndCompletedCannotBeEdited()
    {
        var (category, unit, location) = await _db.SeedReferencesAsync();
        var item = await new ItemRepository(_db).AddAsync(new Item { Name = "Тонер", CategoryId = category.Id, UnitId = unit.Id });
        var draft = await Repo.CreateDraftAsync(DateTime.UtcNow, null);

        await Repo.SetLineAsync(draft.Id, item.Id, location.Id, 3);
        await Repo.SetLineAsync(draft.Id, item.Id, location.Id, 5);
        Assert.Equal(5, (await Repo.GetWithLinesAsync(draft.Id))!.Lines.Single().CountedQuantity);

        await Repo.SetLineAsync(draft.Id, item.Id, location.Id, null);
        Assert.Empty((await Repo.GetWithLinesAsync(draft.Id))!.Lines);

        await Repo.SetLineAsync(draft.Id, item.Id, location.Id, 7);
        await Repo.CompleteAsync(draft.Id);

        var completed = await Repo.GetWithLinesAsync(draft.Id);
        Assert.Equal(StockTakeStatus.Completed, completed!.Status);
        Assert.Equal("tester", completed.CompletedBy);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Repo.SetLineAsync(draft.Id, item.Id, location.Id, 1));
        Assert.Equal(DomainErrorCode.StockTakeCompleted, ex.Code);

        await Repo.ReopenAsync(draft.Id);
        await Repo.SetLineAsync(draft.Id, item.Id, location.Id, 1);
    }

    [Fact]
    public async Task Snapshot_SumsCountsAcrossLocations_ForCompletedOnly()
    {
        var (category, unit, location) = await _db.SeedReferencesAsync();
        var shelf = await new StorageLocationRepository(_db).AddAsync(new StorageLocation { Name = "Полка у принтера" });
        var item = await new ItemRepository(_db).AddAsync(new Item { Name = "Краска белая", CategoryId = category.Id, UnitId = unit.Id });

        var first = await Repo.CreateDraftAsync(DateTime.UtcNow.AddDays(-10), null);
        await Repo.SetLineAsync(first.Id, item.Id, location.Id, 4);
        await Repo.SetLineAsync(first.Id, item.Id, shelf.Id, 1.5m);
        await Repo.CompleteAsync(first.Id);

        var draft = await Repo.CreateDraftAsync(DateTime.UtcNow, null);
        await Repo.SetLineAsync(draft.Id, item.Id, location.Id, 100);

        var snapshot = await new StockDataReader(_db).LoadAsync();

        var completed = Assert.Single(snapshot.StockTakes);
        Assert.Equal(5.5m, completed.CountedByItem[item.Id]);
    }

    [Fact]
    public async Task Summaries_CountDistinctItems()
    {
        var (category, unit, location) = await _db.SeedReferencesAsync();
        var shelf = await new StorageLocationRepository(_db).AddAsync(new StorageLocation { Name = "Шкаф" });
        var item = await new ItemRepository(_db).AddAsync(new Item { Name = "Клей", CategoryId = category.Id, UnitId = unit.Id });
        var draft = await Repo.CreateDraftAsync(DateTime.UtcNow, "Плановая");
        await Repo.SetLineAsync(draft.Id, item.Id, location.Id, 1);
        await Repo.SetLineAsync(draft.Id, item.Id, shelf.Id, 2);

        var summary = Assert.Single(await Repo.GetSummariesAsync());

        Assert.Equal(1, summary.CountedItems);
        Assert.Equal("Плановая", summary.Note);
    }

    public void Dispose() => _db.Dispose();
}

public sealed class UserRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task FindByLogin_IsCaseInsensitive_AndLoginIsUnique()
    {
        var repo = new UserRepository(_db);
        await repo.AddAsync(new User { Login = "Admin", DisplayName = "A", PasswordHash = "x", Role = UserRole.Administrator });

        Assert.NotNull(await repo.FindByLoginAsync("admin"));
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            repo.AddAsync(new User { Login = "ADMIN", DisplayName = "B", PasswordHash = "x" }));
        Assert.Equal(DomainErrorCode.LoginNotUnique, ex.Code);
    }

    public void Dispose() => _db.Dispose();
}

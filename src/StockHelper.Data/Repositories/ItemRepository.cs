using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Services;

namespace StockHelper.Data.Repositories;

public sealed class ItemRepository(IDbContextFactory<StockHelperDbContext> factory) : RepositoryBase(factory), IItemRepository
{
    public async Task<IReadOnlyList<Item>> GetAllAsync(bool includeArchived, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var query = db.Items.AsNoTracking().Include(i => i.Category).Include(i => i.Unit).AsQueryable();
        if (!includeArchived)
        {
            query = query.Where(i => !i.IsArchived);
        }

        var list = await query.ToListAsync(ct);
        return [.. list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<Item?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await db.Items.AsNoTracking().Include(i => i.Category).Include(i => i.Unit).FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<Item> AddAsync(Item item, CancellationToken ct = default)
    {
        Normalize(item);
        Rules.Validate(item);
        await using var db = await CreateAsync(ct);
        await EnsureValidReferencesAsync(db, item, ct);

        var entity = new Item();
        Copy(item, entity);
        db.Items.Add(entity);
        await SaveAsync(db, ct);
        return entity;
    }

    public async Task<Item> UpdateAsync(Item item, CancellationToken ct = default)
    {
        Normalize(item);
        Rules.Validate(item);
        await using var db = await CreateAsync(ct);
        await EnsureValidReferencesAsync(db, item, ct);

        var tracked = Required(await db.Items.FindAsync([item.Id], ct));
        ExpectStamp(db, tracked, item.ConcurrencyStamp);
        Copy(item, tracked);
        await SaveAsync(db, ct);
        return tracked;
    }

    public async Task SetArchivedAsync(int id, bool archived, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var tracked = Required(await db.Items.FindAsync([id], ct));
        tracked.IsArchived = archived;
        await SaveAsync(db, ct);
    }

    public async Task<bool> HasHistoryAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await HasHistoryAsync(db, id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        if (await HasHistoryAsync(db, id, ct))
        {
            throw new DomainException(DomainErrorCode.InUse);
        }

        db.Items.Remove(Required(await db.Items.FindAsync([id], ct)));
        await SaveAsync(db, ct);
    }

    private static async Task<bool> HasHistoryAsync(StockHelperDbContext db, int id, CancellationToken ct) =>
        await db.Receipts.AnyAsync(r => r.ItemId == id, ct)
        || await db.StockTakeLines.AnyAsync(l => l.ItemId == id, ct)
        || await db.Issues.AnyAsync(i => i.ItemId == id, ct);

    private static void Normalize(Item item)
    {
        item.Name = item.Name.Trim();
        item.Code = Clean(item.Code);
        item.Note = Clean(item.Note);
    }

    private static void Copy(Item source, Item target)
    {
        target.Name = source.Name;
        target.Code = source.Code;
        target.CategoryId = source.CategoryId;
        target.UnitId = source.UnitId;
        target.MinStock = source.MinStock;
        target.Price = source.Price;
        target.Note = source.Note;
        target.IsArchived = source.IsArchived;
    }

    private static async Task EnsureValidReferencesAsync(StockHelperDbContext db, Item item, CancellationToken ct)
    {
        if (await db.Items.AnyAsync(i => i.NormalizedName == item.NormalizedName && i.Id != item.Id, ct))
        {
            throw new DomainException(DomainErrorCode.NameNotUnique, item.Name);
        }

        if (!await db.Categories.AnyAsync(c => c.Id == item.CategoryId, ct) || !await db.Units.AnyAsync(u => u.Id == item.UnitId, ct))
        {
            throw new DomainException(DomainErrorCode.NotFound, "Category or unit");
        }
    }
}

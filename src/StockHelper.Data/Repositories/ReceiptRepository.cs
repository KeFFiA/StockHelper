using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Services;

namespace StockHelper.Data.Repositories;

public sealed class ReceiptRepository(IDbContextFactory<StockHelperDbContext> factory) : RepositoryBase(factory), IReceiptRepository
{
    public async Task<IReadOnlyList<Receipt>> GetAsync(ReceiptFilter filter, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var query = db.Receipts.AsNoTracking()
            .Include(r => r.Item).ThenInclude(i => i!.Unit)
            .Include(r => r.StorageLocation)
            .Include(r => r.Unit)
            .AsQueryable();

        if (filter.FromUtc is { } from)
        {
            query = query.Where(r => r.Date >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(r => r.Date <= to);
        }

        if (filter.ItemId is { } itemId)
        {
            query = query.Where(r => r.ItemId == itemId);
        }

        return await query.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToListAsync(ct);
    }

    public async Task<Receipt> AddAsync(Receipt receipt, CancellationToken ct = default)
    {
        Rules.Validate(receipt);
        await using var db = await CreateAsync(ct);
        await EnsureReferencesAsync(db, receipt, ct);

        var entity = new Receipt();
        Copy(receipt, entity);
        db.Receipts.Add(entity);
        await SaveAsync(db, ct);
        return entity;
    }

    public async Task<Receipt> UpdateAsync(Receipt receipt, CancellationToken ct = default)
    {
        Rules.Validate(receipt);
        await using var db = await CreateAsync(ct);
        await EnsureReferencesAsync(db, receipt, ct);

        var tracked = Required(await db.Receipts.FindAsync([receipt.Id], ct));
        ExpectStamp(db, tracked, receipt.ConcurrencyStamp);
        Copy(receipt, tracked);
        await SaveAsync(db, ct);
        return tracked;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        db.Receipts.Remove(Required(await db.Receipts.FindAsync([id], ct)));
        await SaveAsync(db, ct);
    }

    private static void Copy(Receipt source, Receipt target)
    {
        target.ItemId = source.ItemId;
        target.Quantity = source.Quantity;
        target.Price = source.Price;
        target.Date = source.Date.Kind == DateTimeKind.Utc ? source.Date : source.Date.ToUniversalTime();
        target.StorageLocationId = source.StorageLocationId;
        target.UnitId = source.UnitId;
        target.UnitQuantity = source.UnitId is null ? null : source.UnitQuantity;
        target.Note = Clean(source.Note);
    }

    private static async Task EnsureReferencesAsync(StockHelperDbContext db, Receipt receipt, CancellationToken ct)
    {
        if (!await db.Items.AnyAsync(i => i.Id == receipt.ItemId, ct))
        {
            throw new DomainException(DomainErrorCode.NotFound, "Item");
        }

        if (receipt.StorageLocationId is { } locationId && !await db.StorageLocations.AnyAsync(l => l.Id == locationId, ct))
        {
            throw new DomainException(DomainErrorCode.NotFound, "Storage location");
        }
    }
}

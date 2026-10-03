using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Services;

namespace StockHelper.Data.Repositories;

public abstract class LookupRepository<T>(IDbContextFactory<StockHelperDbContext> factory)
    : RepositoryBase(factory), ILookupRepository<T> where T : LookupEntity
{
    public async Task<IReadOnlyList<T>> GetAllAsync(bool includeArchived, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var query = db.Set<T>().AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(e => !e.IsArchived);
        }

        var list = await query.ToListAsync(ct);
        return [.. list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        entity.Name = entity.Name.Trim();
        Rules.Validate(entity);
        await using var db = await CreateAsync(ct);
        await EnsureUniqueAsync(db, entity, ct);
        db.Set<T>().Add(entity);
        await SaveAsync(db, ct);
        return entity;
    }

    public async Task<T> UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.Name = entity.Name.Trim();
        Rules.Validate(entity);
        await using var db = await CreateAsync(ct);
        await EnsureUniqueAsync(db, entity, ct);
        var tracked = Required(await db.Set<T>().FindAsync([entity.Id], ct));
        ExpectStamp(db, tracked, entity.ConcurrencyStamp);
        tracked.Name = entity.Name;
        tracked.IsArchived = entity.IsArchived;
        CopyExtra(entity, tracked);
        await SaveAsync(db, ct);
        return tracked;
    }

    public async Task SetArchivedAsync(int id, bool archived, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var tracked = Required(await db.Set<T>().FindAsync([id], ct));
        tracked.IsArchived = archived;
        await SaveAsync(db, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        if (await IsInUseAsync(db, id, ct))
        {
            throw new DomainException(DomainErrorCode.InUse);
        }

        var tracked = Required(await db.Set<T>().FindAsync([id], ct));
        db.Remove(tracked);
        await SaveAsync(db, ct);
    }

    public async Task<bool> IsInUseAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await IsInUseAsync(db, id, ct);
    }

    protected abstract Task<bool> IsInUseAsync(StockHelperDbContext db, int id, CancellationToken ct);

    /// <summary>Copies type-specific fields on update.</summary>
    protected virtual void CopyExtra(T source, T target)
    {
    }

    private static async Task EnsureUniqueAsync(StockHelperDbContext db, T entity, CancellationToken ct)
    {
        var normalized = LookupEntity.Normalize(entity.Name);
        if (await db.Set<T>().AnyAsync(e => e.NormalizedName == normalized && e.Id != entity.Id, ct))
        {
            throw new DomainException(DomainErrorCode.NameNotUnique, entity.Name);
        }
    }
}

public sealed class CategoryRepository(IDbContextFactory<StockHelperDbContext> factory) : LookupRepository<Category>(factory)
{
    protected override Task<bool> IsInUseAsync(StockHelperDbContext db, int id, CancellationToken ct) =>
        db.Items.AnyAsync(i => i.CategoryId == id, ct);
}

public sealed class UnitRepository(IDbContextFactory<StockHelperDbContext> factory) : LookupRepository<Unit>(factory)
{
    protected override Task<bool> IsInUseAsync(StockHelperDbContext db, int id, CancellationToken ct) =>
        db.Items.AnyAsync(i => i.UnitId == id, ct);
}

public sealed class StorageLocationRepository(IDbContextFactory<StockHelperDbContext> factory) : LookupRepository<StorageLocation>(factory)
{
    protected override async Task<bool> IsInUseAsync(StockHelperDbContext db, int id, CancellationToken ct) =>
        await db.StockTakeLines.AnyAsync(l => l.StorageLocationId == id, ct)
        || await db.Receipts.AnyAsync(r => r.StorageLocationId == id, ct);

    protected override void CopyExtra(StorageLocation source, StorageLocation target) =>
        target.Description = Clean(source.Description);
}

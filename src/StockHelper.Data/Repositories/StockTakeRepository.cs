using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.Data.Repositories;

public sealed class StockTakeRepository(
    IDbContextFactory<StockHelperDbContext> factory,
    ICurrentUserService currentUser,
    TimeProvider clock) : RepositoryBase(factory), IStockTakeRepository
{
    public async Task<IReadOnlyList<StockTakeSummary>> GetSummariesAsync(CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await db.StockTakes.AsNoTracking()
            .OrderByDescending(s => s.Date)
            .Select(s => new StockTakeSummary(
                s.Id,
                s.Date,
                s.Status,
                s.Note,
                s.Lines.Select(l => l.ItemId).Distinct().Count(),
                s.CompletedAt,
                s.CompletedBy,
                s.CreatedBy,
                s.ConcurrencyStamp))
            .ToListAsync(ct);
    }

    public async Task<StockTake?> GetWithLinesAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await db.StockTakes.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<StockTake?> GetDraftAsync(CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        return await db.StockTakes.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Status == StockTakeStatus.Draft, ct);
    }

    public async Task<StockTake> CreateDraftAsync(DateTime dateUtc, string? note, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        if (await db.StockTakes.AnyAsync(s => s.Status == StockTakeStatus.Draft, ct))
        {
            throw new DomainException(DomainErrorCode.DraftStockTakeExists);
        }

        dateUtc = ToUtc(dateUtc);
        await EnsureDateIsFreeAsync(db, 0, dateUtc, ct);

        var stockTake = new StockTake { Date = dateUtc, Note = Clean(note), Status = StockTakeStatus.Draft };
        db.StockTakes.Add(stockTake);
        await SaveAsync(db, ct);
        return stockTake;
    }

    public async Task UpdateHeaderAsync(int id, DateTime dateUtc, string? note, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var stockTake = await GetDraftForEditAsync(db, id, ct);
        dateUtc = ToUtc(dateUtc);
        await EnsureDateIsFreeAsync(db, id, dateUtc, ct);
        stockTake.Date = dateUtc;
        stockTake.Note = Clean(note);
        await SaveAsync(db, ct);
    }

    public async Task SetLineAsync(int stockTakeId, int itemId, int storageLocationId, decimal? quantity, CancellationToken ct = default)
    {
        if (quantity is { } value)
        {
            Rules.ValidateCount(value);
        }

        await using var db = await CreateAsync(ct);
        await GetDraftForEditAsync(db, stockTakeId, ct);

        var line = await db.StockTakeLines.FirstOrDefaultAsync(
            l => l.StockTakeId == stockTakeId && l.ItemId == itemId && l.StorageLocationId == storageLocationId, ct);

        if (quantity is null)
        {
            if (line is not null)
            {
                db.StockTakeLines.Remove(line);
            }
        }
        else if (line is null)
        {
            db.StockTakeLines.Add(new StockTakeLine
            {
                StockTakeId = stockTakeId,
                ItemId = itemId,
                StorageLocationId = storageLocationId,
                CountedQuantity = quantity.Value,
            });
        }
        else if (line.CountedQuantity != quantity.Value)
        {
            line.CountedQuantity = quantity.Value;
        }

        await SaveAsync(db, ct);
    }

    public async Task CompleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var stockTake = await GetDraftForEditAsync(db, id, ct);
        stockTake.Status = StockTakeStatus.Completed;
        stockTake.CompletedAt = clock.GetUtcNow().UtcDateTime;
        stockTake.CompletedBy = currentUser.AuditName;
        await SaveAsync(db, ct);
    }

    public async Task ReopenAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var stockTake = Required(await db.StockTakes.FindAsync([id], ct));
        if (stockTake.Status != StockTakeStatus.Completed)
        {
            throw new DomainException(DomainErrorCode.StockTakeNotCompleted);
        }

        if (await db.StockTakes.AnyAsync(s => s.Status == StockTakeStatus.Draft, ct))
        {
            throw new DomainException(DomainErrorCode.DraftStockTakeExists);
        }

        stockTake.Status = StockTakeStatus.Draft;
        stockTake.CompletedAt = null;
        stockTake.CompletedBy = null;
        await SaveAsync(db, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);
        var stockTake = await GetDraftForEditAsync(db, id, ct);
        db.StockTakes.Remove(stockTake);
        await SaveAsync(db, ct);
    }

    private static async Task<StockTake> GetDraftForEditAsync(StockHelperDbContext db, int id, CancellationToken ct)
    {
        var stockTake = Required(await db.StockTakes.FindAsync([id], ct));
        if (stockTake.Status != StockTakeStatus.Draft)
        {
            throw new DomainException(DomainErrorCode.StockTakeCompleted);
        }

        return stockTake;
    }

    private static async Task EnsureDateIsFreeAsync(StockHelperDbContext db, int id, DateTime dateUtc, CancellationToken ct)
    {
        if (await db.StockTakes.AnyAsync(s => s.Id != id && s.Date == dateUtc, ct))
        {
            throw new DomainException(DomainErrorCode.StockTakeDateConflict);
        }
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

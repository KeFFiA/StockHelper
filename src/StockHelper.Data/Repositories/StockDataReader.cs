using Microsoft.EntityFrameworkCore;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.Data.Repositories;

public sealed class StockDataReader(IDbContextFactory<StockHelperDbContext> factory) : RepositoryBase(factory), IStockDataReader
{
    public async Task<StockSnapshot> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await CreateAsync(ct);

        var items = await db.Items.AsNoTracking().Include(i => i.Category).Include(i => i.Unit).ToListAsync(ct);

        var headers = await db.StockTakes.AsNoTracking()
            .Where(s => s.Status == StockTakeStatus.Completed)
            .Select(s => new { s.Id, s.Date })
            .ToListAsync(ct);

        // Decimal aggregation is done in memory: SQLite cannot SUM decimals natively.
        var lines = await db.StockTakeLines.AsNoTracking()
            .Where(l => l.StockTake!.Status == StockTakeStatus.Completed)
            .Select(l => new { l.StockTakeId, l.ItemId, l.CountedQuantity })
            .ToListAsync(ct);

        var linesByStockTake = lines.GroupBy(l => l.StockTakeId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<int, decimal>)g.GroupBy(l => l.ItemId).ToDictionary(x => x.Key, x => x.Sum(l => l.CountedQuantity)));

        var stockTakes = headers
            .OrderBy(h => h.Date)
            .Select(h => new CompletedStockTake(h.Id, h.Date,
                linesByStockTake.TryGetValue(h.Id, out var counted) ? counted : new Dictionary<int, decimal>()))
            .ToList();

        var receipts = await db.Receipts.AsNoTracking().ToListAsync(ct);

        return new StockSnapshot(items, stockTakes, receipts);
    }
}

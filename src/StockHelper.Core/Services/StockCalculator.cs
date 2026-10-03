using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.Core.Services;

/// <summary>
/// Consumption of one item between two consecutive completed stock-takes in which the item was counted:
/// <c>consumption = opening + received in (from, to] − closing</c>.
/// </summary>
public sealed record ConsumptionPeriod(
    int ItemId,
    int FromStockTakeId,
    DateTime From,
    int ToStockTakeId,
    DateTime To,
    decimal OpeningStock,
    decimal Received,
    decimal ClosingStock)
{
    public decimal Consumption => OpeningStock + Received - ClosingStock;

    /// <summary>Counted more than expected: shown as a discrepancy, never hidden.</summary>
    public bool IsDiscrepancy => Consumption < 0;

    public decimal Days => (decimal)(To - From).TotalDays;
}

/// <summary>Current position of an item: estimated stock, usage rate and forecast.</summary>
public sealed record ItemStockStatus(
    Item Item,
    decimal? LastCountedStock,
    DateTime? LastCountDate,
    decimal ReceivedSinceCount,
    decimal EstimatedStock,
    bool HasBaseline,
    decimal? AverageDailyConsumption,
    decimal? DaysLeft,
    DateTime? RunOutDate,
    bool IsBelowMinimum,
    bool IsRunningOut,
    decimal SuggestedQuantity)
{
    public bool NeedsPurchase => IsBelowMinimum || IsRunningOut;
}

/// <summary>Consumption totals of one item over a reporting range.</summary>
public sealed record ItemConsumption(
    Item Item,
    decimal Consumption,
    decimal Cost,
    decimal Days,
    decimal? AverageDaily,
    bool HasDiscrepancy,
    IReadOnlyList<ConsumptionPeriod> Periods);

public sealed record ForecastOptions(int PurchaseHorizonDays, int ForecastWindowDays);

/// <summary>
/// Pure calculations over a <see cref="StockSnapshot"/>: no database or UI access, fully unit-tested.
/// All dates are UTC.
/// </summary>
public static class StockCalculator
{
    /// <summary>Consumption periods of an item, oldest first. The first count of an item is only a baseline.</summary>
    public static IReadOnlyList<ConsumptionPeriod> GetPeriods(StockSnapshot snapshot, int itemId)
    {
        var counts = CountsOf(snapshot, itemId);
        var receipts = ReceiptsOf(snapshot, itemId);
        var periods = new List<ConsumptionPeriod>(Math.Max(0, counts.Count - 1));

        for (var i = 1; i < counts.Count; i++)
        {
            var (prevTake, prevQty) = counts[i - 1];
            var (currTake, currQty) = counts[i];
            var received = SumReceipts(receipts, prevTake.Date, currTake.Date);
            periods.Add(new ConsumptionPeriod(itemId, prevTake.Id, prevTake.Date, currTake.Id, currTake.Date, prevQty, received, currQty));
        }

        return periods;
    }

    /// <summary>Average consumption per day over the periods that end inside the window; falls back to the latest period.</summary>
    public static decimal? AverageDailyConsumption(IReadOnlyList<ConsumptionPeriod> periods, DateTime windowStartUtc)
    {
        if (periods.Count == 0)
        {
            return null;
        }

        var inWindow = periods.Where(p => p.To > windowStartUtc).ToList();
        if (inWindow.Count == 0)
        {
            inWindow = [periods[^1]];
        }

        var days = inWindow.Sum(p => p.Days);
        return days <= 0 ? null : inWindow.Sum(p => p.Consumption) / days;
    }

    /// <summary>
    /// Estimated stock at a moment: the latest completed count before it plus receipts after that count.
    /// Without any count the result is receipts only and <c>HasBaseline</c> is false.
    /// </summary>
    public static (decimal Stock, decimal? Counted, DateTime? CountDate, decimal Received, bool HasBaseline) EstimateStock(
        StockSnapshot snapshot, int itemId, DateTime atUtc, int? excludeStockTakeId = null)
    {
        var receipts = ReceiptsOf(snapshot, itemId);
        var last = CountsOf(snapshot, itemId)
            .Where(c => c.Take.Date <= atUtc && c.Take.Id != excludeStockTakeId)
            .Select(c => ((CompletedStockTake Take, decimal Qty)?)c)
            .LastOrDefault();

        if (last is not { } baseline)
        {
            var receivedTotal = receipts.Where(r => r.Date <= atUtc).Sum(r => r.Quantity);
            return (receivedTotal, null, null, receivedTotal, false);
        }

        var received = SumReceipts(receipts, baseline.Take.Date, atUtc);
        return (baseline.Qty + received, baseline.Qty, baseline.Take.Date, received, true);
    }

    /// <summary>Expected stock of every item that has a baseline before <paramref name="atUtc"/>.</summary>
    public static IReadOnlyDictionary<int, decimal> ExpectedStockAt(StockSnapshot snapshot, DateTime atUtc, int? excludeStockTakeId = null)
    {
        var result = new Dictionary<int, decimal>();
        foreach (var item in snapshot.Items)
        {
            var estimate = EstimateStock(snapshot, item.Id, atUtc, excludeStockTakeId);
            if (estimate.HasBaseline && estimate.CountDate < atUtc)
            {
                result[item.Id] = estimate.Stock;
            }
        }

        return result;
    }

    /// <summary>Stock position, forecast and purchase need of every active item.</summary>
    public static IReadOnlyList<ItemStockStatus> GetStatuses(StockSnapshot snapshot, DateTime nowUtc, ForecastOptions options)
    {
        var windowStart = nowUtc.AddDays(-options.ForecastWindowDays);
        var result = new List<ItemStockStatus>();

        foreach (var item in snapshot.Items.Where(i => !i.IsArchived))
        {
            var estimate = EstimateStock(snapshot, item.Id, nowUtc);
            var average = AverageDailyConsumption(GetPeriods(snapshot, item.Id), windowStart);

            decimal? daysLeft = null;
            DateTime? runOut = null;
            if (average is > 0)
            {
                daysLeft = Math.Max(0, estimate.Stock / average.Value);
                runOut = nowUtc.AddDays((double)Math.Min(daysLeft.Value, 36500));
            }

            var belowMinimum = estimate.Stock < item.MinStock;
            var runningOut = daysLeft is { } d && d < options.PurchaseHorizonDays;

            var target = item.MinStock + (average is > 0 ? average.Value * options.PurchaseHorizonDays : 0);
            var suggested = belowMinimum || runningOut ? Math.Max(0, Math.Ceiling(target - estimate.Stock)) : 0;

            result.Add(new ItemStockStatus(
                item,
                estimate.Counted,
                estimate.CountDate,
                estimate.Received,
                estimate.Stock,
                estimate.HasBaseline,
                average,
                daysLeft,
                runOut,
                belowMinimum,
                runningOut,
                suggested));
        }

        return result;
    }

    /// <summary>Items that are below the minimum or will run out within the purchase horizon, most urgent first.</summary>
    public static IReadOnlyList<ItemStockStatus> GetPurchaseList(StockSnapshot snapshot, DateTime nowUtc, ForecastOptions options) =>
        [.. GetStatuses(snapshot, nowUtc, options)
            .Where(s => s.NeedsPurchase)
            .OrderBy(s => s.DaysLeft ?? (s.IsBelowMinimum ? 0 : decimal.MaxValue))
            .ThenBy(s => s.Item.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// Consumption per item for periods that end inside (<paramref name="fromUtc"/>, <paramref name="toUtc"/>].
    /// Cost uses the current item price.
    /// </summary>
    public static IReadOnlyList<ItemConsumption> GetConsumption(StockSnapshot snapshot, DateTime fromUtc, DateTime toUtc)
    {
        var result = new List<ItemConsumption>();
        foreach (var item in snapshot.Items)
        {
            var periods = GetPeriods(snapshot, item.Id).Where(p => p.To > fromUtc && p.To <= toUtc).ToList();
            if (periods.Count == 0)
            {
                continue;
            }

            var consumption = periods.Sum(p => p.Consumption);
            var days = periods.Sum(p => p.Days);
            result.Add(new ItemConsumption(
                item,
                consumption,
                consumption * item.Price,
                days,
                days > 0 ? consumption / days : null,
                periods.Any(p => p.IsDiscrepancy),
                periods));
        }

        return [.. result.OrderBy(r => r.Item.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static List<(CompletedStockTake Take, decimal Qty)> CountsOf(StockSnapshot snapshot, int itemId) =>
        [.. snapshot.StockTakes
            .Where(s => s.CountedByItem.ContainsKey(itemId))
            .OrderBy(s => s.Date)
            .Select(s => (s, s.CountedByItem[itemId]))];

    private static List<Receipt> ReceiptsOf(StockSnapshot snapshot, int itemId) =>
        [.. snapshot.Receipts.Where(r => r.ItemId == itemId)];

    /// <summary>Receipts in the half-open interval (from, to].</summary>
    private static decimal SumReceipts(IEnumerable<Receipt> receipts, DateTime fromUtc, DateTime toUtc) =>
        receipts.Where(r => r.Date > fromUtc && r.Date <= toUtc).Sum(r => r.Quantity);
}

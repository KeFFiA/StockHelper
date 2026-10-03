using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.Core.Services;

/// <summary>
/// One item between two consecutive completed stock-takes in which it was counted.
/// <c>Expected = Opening + Received − Issued</c> (issued net of returns); <c>Difference = Closing − Expected</c>.
/// </summary>
public sealed record ConsumptionPeriod(
    int ItemId,
    int FromStockTakeId,
    DateTime From,
    int ToStockTakeId,
    DateTime To,
    decimal OpeningStock,
    decimal Received,
    decimal ClosingStock,
    decimal Issued = 0,
    bool UsesIssues = false)
{
    public decimal Expected => OpeningStock + Received - Issued;

    /// <summary>Counted minus expected: negative = shortage (unrecorded consumption), positive = surplus.</summary>
    public decimal Difference => ClosingStock - Expected;

    /// <summary>Issue-based items: what was issued; others: what disappeared between the counts.</summary>
    public decimal Consumption => UsesIssues ? Issued : OpeningStock + Received - ClosingStock;

    /// <summary>Shown as a discrepancy, never hidden.</summary>
    public bool IsDiscrepancy => UsesIssues ? Difference != 0 : Consumption < 0;

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
    decimal SuggestedQuantity,
    decimal IssuedSinceCount = 0,
    decimal OnHand = 0)
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
    IReadOnlyList<ConsumptionPeriod> Periods,
    bool UsesIssues = false,
    decimal Issued = 0,
    decimal Returned = 0,
    decimal Unaccounted = 0);

public sealed record ForecastOptions(int PurchaseHorizonDays, int ForecastWindowDays);

/// <summary>
/// Pure calculations over a <see cref="StockSnapshot"/>: no database or UI access, fully unit-tested.
/// Issues (net of returns) are the main consumption source. Items that were never issued fall back to
/// consumption between stock-takes. Stock-takes always correct the stock. All dates are UTC.
/// </summary>
public static class StockCalculator
{
    /// <summary>
    /// Unit price of the item at a moment: the price of the latest receipt at or before it. Before the first receipt —
    /// the price of the earliest receipt; without receipts — the current price from the catalog.
    /// Receipts without a price (0) are ignored. Historical costs use it so that past consumption keeps the prices of its time.
    /// </summary>
    public static decimal PriceAt(StockSnapshot snapshot, Item item, DateTime atUtc)
    {
        Receipt? latest = null;
        Receipt? earliest = null;
        foreach (var receipt in snapshot.Receipts)
        {
            if (receipt.ItemId != item.Id || receipt.Price <= 0)
            {
                continue;
            }

            if (receipt.Date <= atUtc && (latest is null || receipt.Date > latest.Date || (receipt.Date == latest.Date && receipt.Id > latest.Id)))
            {
                latest = receipt;
            }

            if (earliest is null || receipt.Date < earliest.Date)
            {
                earliest = receipt;
            }
        }

        return (latest ?? earliest)?.Price ?? item.Price;
    }

    /// <summary>Cost of net issues in (from, to]: each issue at the price of its date; a return gives back the price of its issue.</summary>
    public static decimal NetIssuedCost(StockSnapshot snapshot, Item item, DateTime fromUtc, DateTime toUtc)
    {
        var cost = 0m;
        foreach (var issue in snapshot.Issues.Where(i => i.ItemId == item.Id))
        {
            var inRange = issue.Date > fromUtc && issue.Date <= toUtc;
            var returnedInRange = issue.ReturnedAt is { } returnedAt && returnedAt > fromUtc && returnedAt <= toUtc;
            if (!inRange && !returnedInRange)
            {
                continue;
            }

            var price = PriceAt(snapshot, item, issue.Date);
            if (inRange)
            {
                cost += issue.Quantity * price;
            }

            if (returnedInRange)
            {
                cost -= (issue.ReturnedQuantity ?? 0) * price;
            }
        }

        return cost;
    }

    /// <summary>Cost of the consumption between two counts: at the price in the middle of the period.</summary>
    public static decimal PeriodCost(StockSnapshot snapshot, Item item, ConsumptionPeriod period) =>
        period.Consumption * PriceAt(snapshot, item, period.From + (period.To - period.From) / 2);

    /// <summary>True when the item has any issues: consumption then comes from issues, not from counts.</summary>
    public static bool UsesIssues(StockSnapshot snapshot, int itemId) => snapshot.Issues.Any(i => i.ItemId == itemId);

    /// <summary>Periods between consecutive counts of an item, oldest first. The first count is only a baseline.</summary>
    public static IReadOnlyList<ConsumptionPeriod> GetPeriods(StockSnapshot snapshot, int itemId)
    {
        var counts = CountsOf(snapshot, itemId);
        var usesIssues = UsesIssues(snapshot, itemId);
        var periods = new List<ConsumptionPeriod>(Math.Max(0, counts.Count - 1));

        for (var i = 1; i < counts.Count; i++)
        {
            var (prevTake, prevQty) = counts[i - 1];
            var (currTake, currQty) = counts[i];
            periods.Add(new ConsumptionPeriod(
                itemId, prevTake.Id, prevTake.Date, currTake.Id, currTake.Date, prevQty,
                Received(snapshot, itemId, prevTake.Date, currTake.Date),
                currQty,
                NetIssued(snapshot, itemId, prevTake.Date, currTake.Date),
                usesIssues));
        }

        return periods;
    }

    /// <summary>Count-based average over the periods that end inside the window; falls back to the latest period.</summary>
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

    /// <summary>Average daily consumption of an item: net issues over the window, or count-based for items never issued.</summary>
    public static decimal? AverageDailyConsumption(StockSnapshot snapshot, int itemId, DateTime nowUtc, int windowDays)
    {
        var windowStart = nowUtc.AddDays(-windowDays);
        if (!UsesIssues(snapshot, itemId))
        {
            return AverageDailyConsumption(GetPeriods(snapshot, itemId), windowStart);
        }

        // Do not dilute the average with days before the item was tracked at all.
        var start = Max(windowStart, FirstActivity(snapshot, itemId) ?? windowStart);
        var days = Math.Max(1m, (decimal)(nowUtc - start).TotalDays);
        return NetIssued(snapshot, itemId, start, nowUtc) / days;
    }

    /// <summary>
    /// Estimated stock at a moment: the latest completed count before it plus receipts and returns minus issues after it.
    /// Without any count the result is movements only and <c>HasBaseline</c> is false.
    /// </summary>
    public static (decimal Stock, decimal? Counted, DateTime? CountDate, decimal Received, bool HasBaseline) EstimateStock(
        StockSnapshot snapshot, int itemId, DateTime atUtc, int? excludeStockTakeId = null)
    {
        var last = CountsOf(snapshot, itemId)
            .Where(c => c.Take.Date <= atUtc && c.Take.Id != excludeStockTakeId)
            .Select(c => ((CompletedStockTake Take, decimal Qty)?)c)
            .LastOrDefault();

        var from = last?.Take.Date ?? DateTime.MinValue;
        var received = Received(snapshot, itemId, from, atUtc);
        var movement = received - NetIssued(snapshot, itemId, from, atUtc);

        return last is { } baseline
            ? (baseline.Qty + movement, baseline.Qty, baseline.Take.Date, received, true)
            : (movement, null, null, received, false);
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
        var result = new List<ItemStockStatus>();

        foreach (var item in snapshot.Items.Where(i => !i.IsArchived))
        {
            var estimate = EstimateStock(snapshot, item.Id, nowUtc);
            var average = AverageDailyConsumption(snapshot, item.Id, nowUtc, options.ForecastWindowDays);

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

            var issuedSinceCount = NetIssued(snapshot, item.Id, estimate.CountDate ?? DateTime.MinValue, nowUtc);
            var onHand = snapshot.Issues.Where(i => i.ItemId == item.Id && i.IsOnHand).Sum(i => i.Quantity);

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
                suggested,
                issuedSinceCount,
                onHand));
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
    /// Consumption per item in (<paramref name="fromUtc"/>, <paramref name="toUtc"/>]. Issue-based items: issued − returned in the range,
    /// plus the unaccounted shortage found by stock-takes in the range. Others: periods between counts that end in the range.
    /// Cost uses the prices of the time (<see cref="PriceAt"/>), not the current price.
    /// </summary>
    public static IReadOnlyList<ItemConsumption> GetConsumption(StockSnapshot snapshot, DateTime fromUtc, DateTime toUtc)
    {
        var result = new List<ItemConsumption>();
        foreach (var item in snapshot.Items)
        {
            var periods = GetPeriods(snapshot, item.Id).Where(p => p.To > fromUtc && p.To <= toUtc).ToList();

            if (UsesIssues(snapshot, item.Id))
            {
                var issued = snapshot.Issues.Where(i => i.ItemId == item.Id && i.Date > fromUtc && i.Date <= toUtc).Sum(i => i.Quantity);
                var returned = snapshot.Issues.Where(i => i.ItemId == item.Id && i.ReturnedAt > fromUtc && i.ReturnedAt <= toUtc)
                    .Sum(i => i.ReturnedQuantity ?? 0);
                if (issued == 0 && returned == 0 && periods.Count == 0)
                {
                    continue;
                }

                var consumption = issued - returned;
                var start = Max(fromUtc, FirstActivity(snapshot, item.Id) ?? fromUtc);
                var days = Math.Max(1m, (decimal)(toUtc - start).TotalDays);
                var unaccounted = -periods.Sum(p => p.Difference);
                result.Add(new ItemConsumption(
                    item, consumption, NetIssuedCost(snapshot, item, fromUtc, toUtc), days, consumption / days,
                    periods.Any(p => p.IsDiscrepancy), periods, true, issued, returned, unaccounted));
            }
            else
            {
                if (periods.Count == 0)
                {
                    continue;
                }

                var consumption = periods.Sum(p => p.Consumption);
                var days = periods.Sum(p => p.Days);
                result.Add(new ItemConsumption(
                    item, consumption, periods.Sum(p => PeriodCost(snapshot, item, p)), days, days > 0 ? consumption / days : null,
                    periods.Any(p => p.IsDiscrepancy), periods));
            }
        }

        return [.. result.OrderBy(r => r.Item.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static List<(CompletedStockTake Take, decimal Qty)> CountsOf(StockSnapshot snapshot, int itemId) =>
        [.. snapshot.StockTakes
            .Where(s => s.CountedByItem.ContainsKey(itemId))
            .OrderBy(s => s.Date)
            .Select(s => (s, s.CountedByItem[itemId]))];

    /// <summary>Receipts in the half-open interval (from, to].</summary>
    private static decimal Received(StockSnapshot snapshot, int itemId, DateTime fromUtc, DateTime toUtc) =>
        snapshot.Receipts.Where(r => r.ItemId == itemId && r.Date > fromUtc && r.Date <= toUtc).Sum(r => r.Quantity);

    /// <summary>Issues minus returns in (from, to]; each movement counts at its own moment.</summary>
    private static decimal NetIssued(StockSnapshot snapshot, int itemId, DateTime fromUtc, DateTime toUtc)
    {
        var issued = 0m;
        foreach (var issue in snapshot.Issues.Where(i => i.ItemId == itemId))
        {
            if (issue.Date > fromUtc && issue.Date <= toUtc)
            {
                issued += issue.Quantity;
            }

            if (issue.ReturnedAt is { } returnedAt && returnedAt > fromUtc && returnedAt <= toUtc)
            {
                issued -= issue.ReturnedQuantity ?? 0;
            }
        }

        return issued;
    }

    private static DateTime? FirstActivity(StockSnapshot snapshot, int itemId)
    {
        var dates = snapshot.StockTakes.Where(s => s.CountedByItem.ContainsKey(itemId)).Select(s => s.Date)
            .Concat(snapshot.Receipts.Where(r => r.ItemId == itemId).Select(r => r.Date))
            .Concat(snapshot.Issues.Where(i => i.ItemId == itemId).Select(i => i.Date))
            .ToList();
        return dates.Count == 0 ? null : dates.Min();
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}

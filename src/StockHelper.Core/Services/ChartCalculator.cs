using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;

namespace StockHelper.Core.Services;

public enum StockPointKind
{
    Start,
    Count,
    Receipt,
    Issue,
    Return,
    End,
}

/// <summary>Stock of an item right after an event.</summary>
public sealed record StockPoint(DateTime AtUtc, decimal Stock, StockPointKind Kind);

/// <summary>Series for charts. Pure functions over a snapshot, like <see cref="StockCalculator"/>.</summary>
public static class ChartCalculator
{
    /// <summary>
    /// Cost of consumption in (<paramref name="fromUtc"/>, <paramref name="toUtc"/>] at the prices of the time.
    /// Issue-based items: issues − returns in the range. Others: the overlapping share of each period between counts.
    /// </summary>
    public static decimal ConsumptionCost(StockSnapshot snapshot, DateTime fromUtc, DateTime toUtc) =>
        snapshot.Items.Sum(item => StockCalculator.UsesIssues(snapshot, item.Id)
            ? StockCalculator.NetIssuedCost(snapshot, item, fromUtc, toUtc)
            : StockCalculator.GetPeriods(snapshot, item.Id).Sum(p => OverlapShare(p, fromUtc, toUtc) * StockCalculator.PeriodCost(snapshot, item, p)));

    /// <summary>
    /// Consumed quantity of one item in (<paramref name="fromUtc"/>, <paramref name="toUtc"/>]: net issues for issue-based items,
    /// otherwise the overlapping share of each period between counts.
    /// </summary>
    public static decimal ConsumptionQuantity(StockSnapshot snapshot, int itemId, DateTime fromUtc, DateTime toUtc)
    {
        if (StockCalculator.UsesIssues(snapshot, itemId))
        {
            return snapshot.Issues.Where(i => i.ItemId == itemId && i.Date > fromUtc && i.Date <= toUtc).Sum(i => i.Quantity)
                - snapshot.Issues.Where(i => i.ItemId == itemId && i.ReturnedAt > fromUtc && i.ReturnedAt <= toUtc).Sum(i => i.ReturnedQuantity ?? 0);
        }

        return StockCalculator.GetPeriods(snapshot, itemId).Sum(p => p.Consumption * OverlapShare(p, fromUtc, toUtc));
    }

    /// <summary>Share of the period between counts that falls into (from, to].</summary>
    private static decimal OverlapShare(ConsumptionPeriod period, DateTime fromUtc, DateTime toUtc)
    {
        var start = period.From > fromUtc ? period.From : fromUtc;
        var end = period.To < toUtc ? period.To : toUtc;
        return end <= start || period.Days <= 0 ? 0 : (decimal)(end - start).TotalDays / period.Days;
    }

    /// <summary>Stock of one item over time: a point after every count, receipt, issue and return in the range.</summary>
    public static IReadOnlyList<StockPoint> GetStockHistory(StockSnapshot snapshot, int itemId, DateTime fromUtc, DateTime toUtc)
    {
        var events = new List<(DateTime At, StockPointKind Kind, decimal Value)>();
        events.AddRange(snapshot.StockTakes
            .Where(s => s.CountedByItem.ContainsKey(itemId) && s.Date > fromUtc && s.Date <= toUtc)
            .Select(s => (s.Date, StockPointKind.Count, s.CountedByItem[itemId])));
        events.AddRange(snapshot.Receipts
            .Where(r => r.ItemId == itemId && r.Date > fromUtc && r.Date <= toUtc)
            .Select(r => (r.Date, StockPointKind.Receipt, r.Quantity)));
        events.AddRange(snapshot.Issues
            .Where(i => i.ItemId == itemId && i.Date > fromUtc && i.Date <= toUtc)
            .Select(i => (i.Date, StockPointKind.Issue, -i.Quantity)));
        events.AddRange(snapshot.Issues
            .Where(i => i.ItemId == itemId && i.ReturnedAt > fromUtc && i.ReturnedAt <= toUtc)
            .Select(i => (i.ReturnedAt!.Value, StockPointKind.Return, i.ReturnedQuantity ?? 0)));

        var stock = StockCalculator.EstimateStock(snapshot, itemId, fromUtc).Stock;
        var points = new List<StockPoint> { new(fromUtc, stock, StockPointKind.Start) };

        // A count at the same moment as a movement already includes it (receipts in (prev, curr] are "before" the count).
        foreach (var e in events.OrderBy(e => e.At).ThenBy(e => e.Kind == StockPointKind.Count ? 1 : 0))
        {
            stock = e.Kind == StockPointKind.Count ? e.Value : stock + e.Value;
            points.Add(new StockPoint(e.At, stock, e.Kind));
        }

        points.Add(new StockPoint(toUtc, stock, StockPointKind.End));
        return points;
    }
}

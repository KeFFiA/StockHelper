using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Services;

namespace StockHelper.Core.Tests;

public sealed class ChartCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ConsumptionCost_SplitsCountPeriodsByDays_AndUsesNetIssues()
    {
        var counted = new Item { Id = 1, Name = "Counted", Price = 10 };
        var issued = new Item { Id = 2, Name = "Issued", Price = 100 };
        var snapshot = new StockSnapshot(
            [counted, issued],
            [
                new CompletedStockTake(1, T0, new Dictionary<int, decimal> { [1] = 20 }),
                new CompletedStockTake(2, T0.AddDays(10), new Dictionary<int, decimal> { [1] = 10 }),
            ],
            [],
            [new Issue { ItemId = 2, Quantity = 5, Date = T0.AddDays(2), ReturnedQuantity = 2, ReturnedAt = T0.AddDays(8) }]);

        // First half of the 10-day period: 5 units of item 1 (50) + issue 5 of item 2 (500); the return is outside.
        Assert.Equal(550, ChartCalculator.ConsumptionCost(snapshot, T0, T0.AddDays(5)));
        // Second half: 50 for item 1, −2 × 100 for the return.
        Assert.Equal(-150, ChartCalculator.ConsumptionCost(snapshot, T0.AddDays(5), T0.AddDays(10)));
    }

    [Fact]
    public void StockHistory_FollowsMovements_AndResetsAtCounts()
    {
        var snapshot = new StockSnapshot(
            [new Item { Id = 1, Name = "Paint" }],
            [
                new CompletedStockTake(1, T0, new Dictionary<int, decimal> { [1] = 20 }),
                new CompletedStockTake(2, T0.AddDays(10), new Dictionary<int, decimal> { [1] = 9 }),
            ],
            [new Receipt { ItemId = 1, Quantity = 10, Date = T0.AddDays(1) }],
            [new Issue { ItemId = 1, Quantity = 5, Date = T0.AddDays(2), ReturnedQuantity = 2, ReturnedAt = T0.AddDays(3) }]);

        var points = ChartCalculator.GetStockHistory(snapshot, 1, T0.AddHours(-1), T0.AddDays(12));

        Assert.Equal(
            [StockPointKind.Start, StockPointKind.Count, StockPointKind.Receipt, StockPointKind.Issue, StockPointKind.Return, StockPointKind.Count, StockPointKind.End],
            points.Select(p => p.Kind));
        Assert.Equal([0m, 20m, 30m, 25m, 27m, 9m, 9m], points.Select(p => p.Stock));
    }
}

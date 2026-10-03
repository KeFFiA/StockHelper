using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Services;

namespace StockHelper.Core.Tests;

/// <summary>Costs of past consumption use the receipt prices of their time, not the current catalog price.</summary>
public sealed class HistoricalPriceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly Item _item = new() { Id = 1, Name = "Paint", Price = 200 };
    private readonly List<CompletedStockTake> _takes = [];
    private readonly List<Receipt> _receipts = [];
    private readonly List<Issue> _issues = [];
    private int _nextId = 1;

    private StockSnapshot Snapshot => new([_item], _takes, _receipts, _issues);

    private void Receive(double day, decimal price, decimal qty = 10) =>
        _receipts.Add(new Receipt { Id = _nextId++, ItemId = 1, Date = T0.AddDays(day), Quantity = qty, Price = price });

    private void Give(double day, decimal qty, double? returnDay = null, decimal? returned = null) =>
        _issues.Add(new Issue
        {
            Id = _nextId++,
            ItemId = 1,
            Date = T0.AddDays(day),
            Quantity = qty,
            ReturnedAt = returnDay is null ? null : T0.AddDays(returnDay.Value),
            ReturnedQuantity = returned,
        });

    private void Count(int takeId, int day, decimal qty) =>
        _takes.Add(new CompletedStockTake(takeId, T0.AddDays(day), new Dictionary<int, decimal> { [1] = qty }));

    [Fact]
    public void PriceAt_IsTheLatestReceiptPriceAtThatMoment()
    {
        Receive(10, 100);
        Receive(40, 150);

        Assert.Equal(100, StockCalculator.PriceAt(Snapshot, _item, T0.AddDays(10)));
        Assert.Equal(100, StockCalculator.PriceAt(Snapshot, _item, T0.AddDays(39)));
        Assert.Equal(150, StockCalculator.PriceAt(Snapshot, _item, T0.AddDays(40)));
    }

    [Fact]
    public void PriceAt_BeforeFirstReceipt_UsesEarliestReceipt_WithoutReceipts_UsesCatalogPrice()
    {
        Assert.Equal(200, StockCalculator.PriceAt(Snapshot, _item, T0));

        Receive(10, 100);
        Receive(20, 0); // price not entered: ignored
        Assert.Equal(100, StockCalculator.PriceAt(Snapshot, _item, T0));
        Assert.Equal(100, StockCalculator.PriceAt(Snapshot, _item, T0.AddDays(30)));
    }

    [Fact]
    public void IssueCost_UsesPriceOfIssueDate_AndReturnGivesBackItsIssuePrice()
    {
        Receive(0, 100);
        Give(5, 4, returnDay: 50, returned: 1); // 4 × 100, later −1 × 100
        Receive(30, 150);
        Give(35, 2);                             // 2 × 150

        Assert.Equal(400, ChartCalculator.ConsumptionCost(Snapshot, T0, T0.AddDays(31)));
        Assert.Equal(300 - 100, ChartCalculator.ConsumptionCost(Snapshot, T0.AddDays(31), T0.AddDays(60)));

        var row = Assert.Single(StockCalculator.GetConsumption(Snapshot, T0, T0.AddDays(60)));
        Assert.Equal(5, row.Consumption);
        Assert.Equal(600, row.Cost);
    }

    [Fact]
    public void CountPeriodCost_UsesPriceInTheMiddleOfThePeriod()
    {
        Receive(-1, 100, qty: 0);
        Count(1, 0, 20);
        Receive(4, 150, qty: 0);  // middle of the first period (day 5) already has the new price
        Count(2, 10, 10);         // 10 consumed
        Receive(16, 300, qty: 0); // after the middle of the second period (day 15)
        Count(3, 20, 5);          // 5 consumed

        var row = Assert.Single(StockCalculator.GetConsumption(Snapshot, T0, T0.AddDays(20)));
        Assert.Equal(15, row.Consumption);
        Assert.Equal(10 * 150 + 5 * 150, row.Cost);

        // Monthly charts split periods by days but keep each period's price, so the totals match the report.
        Assert.Equal(row.Cost, ChartCalculator.ConsumptionCost(Snapshot, T0, T0.AddDays(7)) + ChartCalculator.ConsumptionCost(Snapshot, T0.AddDays(7), T0.AddDays(20)));
    }
}

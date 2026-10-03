using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Services;

namespace StockHelper.Core.Tests;

public sealed class StockCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly List<Item> _items = [];
    private readonly List<CompletedStockTake> _takes = [];
    private readonly List<Receipt> _receipts = [];

    private StockSnapshot Snapshot => new(_items, _takes, _receipts);

    private Item AddItem(int id, decimal minStock = 0, decimal price = 0, bool archived = false)
    {
        var item = new Item { Id = id, Name = $"Item {id}", MinStock = minStock, Price = price, IsArchived = archived };
        _items.Add(item);
        return item;
    }

    private void Count(int takeId, int day, params (int ItemId, decimal Qty)[] counts) =>
        _takes.Add(new CompletedStockTake(takeId, T0.AddDays(day), counts.ToDictionary(c => c.ItemId, c => c.Qty)));

    private void Receive(int itemId, double day, decimal qty, decimal price = 0) =>
        _receipts.Add(new Receipt { ItemId = itemId, Date = T0.AddDays(day), Quantity = qty, Price = price });

    [Fact]
    public void FirstCount_IsBaselineOnly()
    {
        AddItem(1);
        Count(1, 0, (1, 10));

        Assert.Empty(StockCalculator.GetPeriods(Snapshot, 1));
    }

    [Fact]
    public void Consumption_IsPreviousPlusReceiptsMinusCurrent()
    {
        AddItem(1);
        Count(1, 0, (1, 10));
        Receive(1, 3, 20);
        Count(2, 10, (1, 12));

        var period = Assert.Single(StockCalculator.GetPeriods(Snapshot, 1));

        Assert.Equal(10, period.OpeningStock);
        Assert.Equal(20, period.Received);
        Assert.Equal(12, period.ClosingStock);
        Assert.Equal(18, period.Consumption);
        Assert.Equal(10, period.Days);
        Assert.False(period.IsDiscrepancy);
    }

    [Fact]
    public void ReceiptInterval_ExcludesPreviousCountMoment_IncludesCurrentCountMoment()
    {
        AddItem(1);
        Count(1, 0, (1, 10));
        Receive(1, 0, 100);   // exactly at the previous count: already included in that count
        Receive(1, 10, 5);    // exactly at the current count: counted as received before it
        Count(2, 10, (1, 10));

        var period = Assert.Single(StockCalculator.GetPeriods(Snapshot, 1));

        Assert.Equal(5, period.Received);
        Assert.Equal(5, period.Consumption);
    }

    [Fact]
    public void NegativeConsumption_IsReportedAsDiscrepancy()
    {
        AddItem(1);
        Count(1, 0, (1, 10));
        Count(2, 7, (1, 13));

        var period = Assert.Single(StockCalculator.GetPeriods(Snapshot, 1));

        Assert.Equal(-3, period.Consumption);
        Assert.True(period.IsDiscrepancy);
    }

    [Fact]
    public void StockTakeWhereItemWasNotCounted_IsSkippedForThatItem()
    {
        AddItem(1);
        AddItem(2);
        Count(1, 0, (1, 10), (2, 5));
        Count(2, 5, (2, 4));          // item 1 not counted here
        Count(3, 10, (1, 6), (2, 1));

        var period = Assert.Single(StockCalculator.GetPeriods(Snapshot, 1));
        Assert.Equal(1, period.FromStockTakeId);
        Assert.Equal(3, period.ToStockTakeId);
        Assert.Equal(2, StockCalculator.GetPeriods(Snapshot, 2).Count);
    }

    [Fact]
    public void AverageDaily_UsesPeriodsInWindow_FallsBackToLatest()
    {
        AddItem(1);
        Count(1, 0, (1, 100));
        Count(2, 10, (1, 80));   // 2/day
        Count(3, 30, (1, 20));   // 3/day

        var periods = StockCalculator.GetPeriods(Snapshot, 1);

        Assert.Equal(80m / 30m, StockCalculator.AverageDailyConsumption(periods, T0.AddDays(-1)));
        Assert.Equal(3m, StockCalculator.AverageDailyConsumption(periods, T0.AddDays(15)));
        Assert.Equal(3m, StockCalculator.AverageDailyConsumption(periods, T0.AddDays(100)));
        Assert.Null(StockCalculator.AverageDailyConsumption([], T0));
    }

    [Fact]
    public void EstimatedStock_IsLastCountPlusLaterReceipts()
    {
        AddItem(1);
        Count(1, 0, (1, 10));
        Receive(1, -1, 50);   // before the count: ignored
        Receive(1, 2, 7);
        Receive(1, 4, 3);

        var estimate = StockCalculator.EstimateStock(Snapshot, 1, T0.AddDays(3));

        Assert.True(estimate.HasBaseline);
        Assert.Equal(17, estimate.Stock);
        Assert.Equal(10, estimate.Counted);
        Assert.Equal(7, estimate.Received);
    }

    [Fact]
    public void EstimatedStock_WithoutCount_HasNoBaseline()
    {
        AddItem(1);
        Receive(1, 1, 5);

        var estimate = StockCalculator.EstimateStock(Snapshot, 1, T0.AddDays(2));

        Assert.False(estimate.HasBaseline);
        Assert.Equal(5, estimate.Stock);
    }

    [Fact]
    public void ExpectedStockAt_ExcludesTheStockTakeBeingEdited()
    {
        AddItem(1);
        Count(1, 0, (1, 10));
        Receive(1, 2, 5);
        Count(2, 5, (1, 12));

        var expected = StockCalculator.ExpectedStockAt(Snapshot, T0.AddDays(5), excludeStockTakeId: 2);

        Assert.Equal(15, expected[1]);
    }

    [Fact]
    public void Forecast_DaysLeft_AndPurchaseList()
    {
        AddItem(1, minStock: 5, price: 10);   // 2/day, 20 left → 10 days
        AddItem(2, minStock: 50);             // below minimum, no consumption data
        AddItem(3, minStock: 1);              // plenty
        AddItem(4, archived: true);
        Count(1, 0, (1, 40), (2, 10), (3, 100), (4, 0));
        Count(2, 10, (1, 20), (2, 10), (3, 99), (4, 0));
        var now = T0.AddDays(10);

        var statuses = StockCalculator.GetStatuses(Snapshot, now, new ForecastOptions(PurchaseHorizonDays: 14, ForecastWindowDays: 90));

        Assert.DoesNotContain(statuses, s => s.Item.Id == 4);
        var first = statuses.Single(s => s.Item.Id == 1);
        Assert.Equal(2, first.AverageDailyConsumption);
        Assert.Equal(10, first.DaysLeft);
        Assert.Equal(now.AddDays(10), first.RunOutDate);
        Assert.True(first.IsRunningOut);
        Assert.False(first.IsBelowMinimum);
        Assert.Equal(13, first.SuggestedQuantity);   // 5 + 2*14 − 20

        var second = statuses.Single(s => s.Item.Id == 2);
        Assert.True(second.IsBelowMinimum);
        Assert.Null(second.DaysLeft);
        Assert.Equal(40, second.SuggestedQuantity);

        var purchase = StockCalculator.GetPurchaseList(Snapshot, now, new ForecastOptions(14, 90));
        Assert.Equal([2, 1], purchase.Select(p => p.Item.Id));
    }

    [Fact]
    public void ZeroOrNegativeAverage_MeansNoForecast()
    {
        AddItem(1);
        Count(1, 0, (1, 10));
        Count(2, 10, (1, 12));

        var status = Assert.Single(StockCalculator.GetStatuses(Snapshot, T0.AddDays(10), new ForecastOptions(14, 90)));

        Assert.Equal(-0.2m, status.AverageDailyConsumption);
        Assert.Null(status.DaysLeft);
        Assert.False(status.NeedsPurchase);
    }

    [Fact]
    public void ConsumptionReport_SumsPeriodsEndingInRange_WithCost()
    {
        AddItem(1, price: 2.5m);
        Count(1, 0, (1, 10));
        Count(2, 10, (1, 6));       // 4
        Receive(1, 12, 10);
        Count(3, 20, (1, 7));       // 9
        Count(4, 40, (1, 8));       // -1 (discrepancy), outside range below

        var report = StockCalculator.GetConsumption(Snapshot, T0, T0.AddDays(20));

        var row = Assert.Single(report);
        Assert.Equal(13, row.Consumption);
        Assert.Equal(32.5m, row.Cost);
        Assert.Equal(20, row.Days);
        Assert.Equal(0.65m, row.AverageDaily);
        Assert.False(row.HasDiscrepancy);

        var all = Assert.Single(StockCalculator.GetConsumption(Snapshot, T0.AddDays(-1), T0.AddDays(100)));
        Assert.True(all.HasDiscrepancy);
        Assert.Equal(12, all.Consumption);
    }
}

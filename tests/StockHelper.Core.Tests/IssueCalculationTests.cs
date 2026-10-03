using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Services;

namespace StockHelper.Core.Tests;

public sealed class IssueCalculationTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly List<Item> _items = [];
    private readonly List<CompletedStockTake> _takes = [];
    private readonly List<Receipt> _receipts = [];
    private readonly List<Issue> _issues = [];

    private StockSnapshot Snapshot => new(_items, _takes, _receipts, _issues);

    private void AddItem(int id, decimal minStock = 0, decimal price = 0) =>
        _items.Add(new Item { Id = id, Name = $"Item {id}", MinStock = minStock, Price = price });

    private void Count(int takeId, int day, decimal qty) =>
        _takes.Add(new CompletedStockTake(takeId, T0.AddDays(day), new Dictionary<int, decimal> { [1] = qty }));

    private Issue Give(double day, decimal qty, double? returnDay = null, decimal? returned = null)
    {
        var issue = new Issue
        {
            ItemId = 1,
            Date = T0.AddDays(day),
            Quantity = qty,
            ReturnedAt = returnDay is null ? null : T0.AddDays(returnDay.Value),
            ReturnedQuantity = returned,
        };
        _issues.Add(issue);
        return issue;
    }

    [Fact]
    public void EstimatedStock_SubtractsIssues_AddsReturnsAtReturnMoment()
    {
        AddItem(1);
        Count(1, 0, 20);
        Give(1, 5, returnDay: 3, returned: 2);   // a 5 l can, 2 l came back

        Assert.Equal(15, StockCalculator.EstimateStock(Snapshot, 1, T0.AddDays(2)).Stock);
        Assert.Equal(17, StockCalculator.EstimateStock(Snapshot, 1, T0.AddDays(4)).Stock);
    }

    [Fact]
    public void Period_ShowsIssued_Expected_AndShortage()
    {
        AddItem(1);
        Count(1, 0, 20);
        Give(2, 5, returnDay: 4, returned: 2);   // net 3
        _receipts.Add(new Receipt { ItemId = 1, Date = T0.AddDays(5), Quantity = 10 });
        Count(2, 10, 25);                        // expected 27 → 2 unaccounted

        var period = Assert.Single(StockCalculator.GetPeriods(Snapshot, 1));

        Assert.True(period.UsesIssues);
        Assert.Equal(3, period.Issued);
        Assert.Equal(27, period.Expected);
        Assert.Equal(-2, period.Difference);
        Assert.Equal(3, period.Consumption);
        Assert.True(period.IsDiscrepancy);
    }

    [Fact]
    public void AverageDaily_IsNetIssuesOverWindow_FromFirstActivity()
    {
        AddItem(1);
        Count(1, 0, 100);
        Give(2, 10);
        Give(5, 10, returnDay: 6, returned: 4);   // net 16 over 10 days

        var average = StockCalculator.AverageDailyConsumption(Snapshot, 1, T0.AddDays(10), windowDays: 90);

        Assert.Equal(1.6m, average);
    }

    [Fact]
    public void Statuses_UseIssueRate_AndReportOnHand()
    {
        AddItem(1, minStock: 5);
        Count(1, 0, 30);
        Give(1, 10);
        var onHand = Give(9, 5);
        onHand.ExpectReturn = true;

        var status = Assert.Single(StockCalculator.GetStatuses(Snapshot, T0.AddDays(10), new ForecastOptions(14, 90)));

        Assert.Equal(15, status.EstimatedStock);
        Assert.Equal(1.5m, status.AverageDailyConsumption);
        Assert.Equal(10, status.DaysLeft);
        Assert.True(status.IsRunningOut);
        Assert.Equal(15, status.IssuedSinceCount);
        Assert.Equal(5, status.OnHand);
    }

    [Fact]
    public void ConsumptionReport_IssueBased_SplitsIssuedReturnedAndUnaccounted()
    {
        AddItem(1, price: 100);
        Count(1, 0, 20);
        Give(1, 5, returnDay: 2, returned: 1);
        Count(2, 10, 14);   // expected 16 → 2 unaccounted

        var row = Assert.Single(StockCalculator.GetConsumption(Snapshot, T0.AddDays(-1), T0.AddDays(10)));

        Assert.True(row.UsesIssues);
        Assert.Equal(5, row.Issued);
        Assert.Equal(1, row.Returned);
        Assert.Equal(4, row.Consumption);
        Assert.Equal(400, row.Cost);
        Assert.Equal(2, row.Unaccounted);
        Assert.True(row.HasDiscrepancy);
    }

    [Fact]
    public void ItemsWithoutIssues_KeepCountBasedConsumption()
    {
        AddItem(1);
        Count(1, 0, 10);
        Count(2, 10, 4);

        var period = Assert.Single(StockCalculator.GetPeriods(Snapshot, 1));

        Assert.False(period.UsesIssues);
        Assert.Equal(6, period.Consumption);
        Assert.Equal(0.6m, StockCalculator.AverageDailyConsumption(Snapshot, 1, T0.AddDays(10), 90));
    }

    [Fact]
    public void Rules_ReturnCannotExceedIssued()
    {
        var ex = Assert.Throws<DomainException>(() => Rules.Validate(new Issue { Quantity = 5, ReturnedQuantity = 6 }));
        Assert.Equal(DomainErrorCode.ReturnExceedsIssued, ex.Code);
    }
}

public sealed class UnitConverterTests
{
    private static readonly Unit Liter = new() { Id = 1, Name = "л" };
    private static readonly Unit Can5 = new() { Id = 2, Name = "Банка 5 л", BaseUnitId = 1, Factor = 5 };
    private static readonly Unit Can10 = new() { Id = 3, Name = "Банка 10 л", BaseUnitId = 1, Factor = 10 };
    private static readonly Unit Piece = new() { Id = 4, Name = "шт" };

    [Fact]
    public void Converts_PackagesToItemUnit_AndBack()
    {
        Assert.Equal(20, UnitConverter.ToItemUnits(2, Can10, Liter));
        Assert.Equal(10, UnitConverter.ToItemUnits(10, Can5, Can5));
        Assert.Equal(2, UnitConverter.ToItemUnits(1, Can10, Can5));     // item measured in 5 l cans
        Assert.Equal(1.5m, UnitConverter.FromItemUnits(7.5m, Can5, Liter));
    }

    [Fact]
    public void CompatibleUnits_ShareTheRoot_ItemUnitFirst()
    {
        var compatible = UnitConverter.CompatibleUnits(Liter, [Piece, Can10, Liter, Can5]);

        Assert.Equal(["л", "Банка 5 л", "Банка 10 л"], compatible.Select(u => u.Name));
    }

    [Fact]
    public void IncompatibleUnits_Throw()
    {
        Assert.Throws<InvalidOperationException>(() => UnitConverter.ToItemUnits(1, Piece, Liter));
    }
}

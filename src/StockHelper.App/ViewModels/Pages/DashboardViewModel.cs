using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels.Pages;

public sealed partial class DashboardViewModel(
    IAnalyticsService analytics,
    INavigationService navigation,
    ICurrentUserService currentUser) : PageViewModel
{
    private const int ListSize = 8;
    private const int StaleStockTakeDays = 31;

    public override string Title => Strings.Nav_Dashboard;

    public override System.Windows.Input.ICommand? RefreshShortcut => LoadCommand;

    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var greeting = hour switch
            {
                < 6 => Strings.Dashboard_GreetingNight,
                < 12 => Strings.Dashboard_GreetingMorning,
                < 18 => Strings.Dashboard_GreetingDay,
                _ => Strings.Dashboard_GreetingEvening,
            };
            var name = currentUser.User?.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return name is null ? greeting : $"{greeting}, {name}";
        }
    }

    public string Today => DateTime.Now.ToString("D");

    public bool CanStartStockTake => currentUser.Has(Permission.EditStockTakes);

    public bool CanAddReceipt => currentUser.Has(Permission.ManageReceipts);

    [ObservableProperty]
    public partial int ActiveItems { get; private set; }

    [ObservableProperty]
    public partial int BelowMinimumCount { get; private set; }

    [ObservableProperty]
    public partial int RunningOutCount { get; private set; }

    [ObservableProperty]
    public partial decimal PurchaseTotal { get; private set; }

    [ObservableProperty]
    public partial DateTime? LastStockTakeDate { get; private set; }

    [ObservableProperty]
    public partial string LastStockTakeHint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStockTakeStale { get; private set; }

    [ObservableProperty]
    public partial bool HasNoBaseline { get; private set; }

    [ObservableProperty]
    public partial int HorizonDays { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ItemStockStatus> BelowMinimum { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ItemStockStatus> RunningOut { get; private set; } = [];

    /// <summary>Consumption cost per calendar month (last six months, current month highlighted).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Controls.BarPoint> MonthlyCost { get; private set; } = [];

    [ObservableProperty]
    public partial string MonthlySummary { get; private set; } = string.Empty;

    public string RunningOutTitle => string.Format(Strings.Dashboard_RunningOutTitle, HorizonDays);

    public override Task OnNavigatedToAsync() => LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var result = await analytics.LoadAsync();
        var statuses = result.Statuses;

        HorizonDays = result.Options.PurchaseHorizonDays;
        ActiveItems = statuses.Count;
        BelowMinimum = [.. statuses.Where(s => s.IsBelowMinimum).OrderBy(s => s.EstimatedStock - s.Item.MinStock).Take(ListSize)];
        BelowMinimumCount = statuses.Count(s => s.IsBelowMinimum);
        RunningOut = [.. statuses.Where(s => s.DaysLeft is not null).OrderBy(s => s.DaysLeft).Take(ListSize)];
        RunningOutCount = statuses.Count(s => s.IsRunningOut);
        PurchaseTotal = result.PurchaseList.Sum(s => s.SuggestedQuantity * s.Item.Price);
        HasNoBaseline = result.Snapshot.StockTakes.Count == 0;
        BuildMonthlyCost(result);

        var last = result.LastStockTake;
        LastStockTakeDate = last?.Date;
        if (last is null)
        {
            LastStockTakeHint = Strings.Dashboard_NoStockTake;
            IsStockTakeStale = true;
        }
        else
        {
            var days = (int)(result.NowUtc - last.Date).TotalDays;
            LastStockTakeHint = days == 0 ? Strings.Dashboard_Today : string.Format(Strings.Dashboard_DaysAgo, days);
            IsStockTakeStale = days > StaleStockTakeDays;
        }

        OnPropertyChanged(nameof(RunningOutTitle));
    });

    private void BuildMonthlyCost(AnalyticsResult result)
    {
        const int months = 6;
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var values = Enumerable.Range(0, months)
            .Select(i => thisMonth.AddMonths(i - months + 1))
            .Select(start =>
            {
                var end = start.AddMonths(1) > DateTime.Now ? DateTime.Now : start.AddMonths(1);
                var cost = Core.Services.ChartCalculator.ConsumptionCost(result.Snapshot, start.ToUniversalTime(), end.ToUniversalTime());
                return (Start: start, Cost: Math.Max(0, cost));
            })
            .ToList();

        // Months before anything was tracked are noise, not "zero consumption".
        while (values.Count > 2 && values[0].Cost == 0)
        {
            values.RemoveAt(0);
        }

        var max = values.Max(v => v.Cost);
        MonthlyCost = [.. values.Select(v => new Controls.BarPoint(
            v.Start.ToString("MMM", System.Globalization.CultureInfo.CurrentCulture).TrimEnd('.'),
            Infrastructure.Compact.Money(v.Cost),
            max <= 0 ? 0 : (double)(v.Cost / max),
            string.Format(Strings.Chart_MonthTooltip, v.Start, v.Cost),
            v.Start == thisMonth))];

        var previous = values[^2].Cost;
        var current = values[^1].Cost;
        MonthlySummary = string.Format(Strings.Chart_MonthlySummary, current, previous);
    }

    [RelayCommand]
    private Task GoToReportsAsync() => navigation.NavigateToAsync<ReportsViewModel>();

    [RelayCommand]
    private Task GoToStockTakesAsync() => navigation.NavigateToAsync<StockTakesViewModel>();

    [RelayCommand]
    private Task GoToReceiptsAsync() => navigation.NavigateToAsync<ReceiptsViewModel>();

    [RelayCommand]
    private Task GoToItemsAsync() => navigation.NavigateToAsync<ItemsViewModel>();
}

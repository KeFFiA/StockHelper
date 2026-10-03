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

    [RelayCommand]
    private Task GoToReportsAsync() => navigation.NavigateToAsync<ReportsViewModel>();

    [RelayCommand]
    private Task GoToStockTakesAsync() => navigation.NavigateToAsync<StockTakesViewModel>();

    [RelayCommand]
    private Task GoToReceiptsAsync() => navigation.NavigateToAsync<ReceiptsViewModel>();

    [RelayCommand]
    private Task GoToItemsAsync() => navigation.NavigateToAsync<ItemsViewModel>();
}

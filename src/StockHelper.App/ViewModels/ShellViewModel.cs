using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.App.ViewModels.Pages;

namespace StockHelper.App.ViewModels;

public sealed partial class NavItem(string title, string iconKey, Type pageType) : ObservableObject
{
    public string Title { get; } = title;

    public string IconKey { get; } = iconKey;

    public Type PageType { get; } = pageType;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    public ShellViewModel(INavigationService navigation, INotificationService notifications)
    {
        _navigation = navigation;
        Notifications = notifications;
        _navigation.Navigated += (_, _) => OnNavigated();

        NavItems =
        [
            new NavItem(Strings.Nav_Dashboard, "Icon.Home", typeof(DashboardViewModel)),
        ];
    }

    public string AppTitle => Strings.AppTitle;

    public string Version => AppInfo.Version;

    public INotificationService Notifications { get; }

    public ObservableCollection<NavItem> NavItems { get; }

    public ObservableCollection<NavItem> FooterItems { get; } = [];

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    [RelayCommand]
    private async Task NavigateAsync(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!await _navigation.NavigateToAsync(item.PageType))
        {
            // Navigation was cancelled: restore the selection of the current page.
            OnNavigated();
        }
    }

    public Task StartAsync() => NavigateAsync(NavItems[0]);

    private void OnNavigated()
    {
        OnPropertyChanged(nameof(CurrentPage));
        var type = _navigation.CurrentPage?.GetType();
        foreach (var item in NavItems.Concat(FooterItems))
        {
            item.IsSelected = item.PageType == type;
        }
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.App.ViewModels.Pages;
using StockHelper.Core.Entities;
using StockHelper.Core.Security;

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
    private readonly ICurrentUserService _currentUser;
    private readonly IServiceProvider _services;

    public ShellViewModel(
        INavigationService navigation,
        INotificationService notifications,
        ICurrentUserService currentUser,
        IServiceProvider services)
    {
        _navigation = navigation;
        _currentUser = currentUser;
        _services = services;
        Notifications = notifications;
        _navigation.Navigated += (_, _) => OnNavigated();
        _currentUser.Changed += (_, _) => OnUserChanged();
    }

    public string AppTitle => Strings.AppTitle;

    public string Version => AppInfo.Version;

    public INotificationService Notifications { get; }

    public ObservableCollection<NavItem> NavItems { get; } = [];

    public ObservableCollection<NavItem> FooterItems { get; } = [];

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    public string? UserName => _currentUser.User?.DisplayName;

    public UserRole? UserRole => _currentUser.User?.Role;

    public string UserInitials
    {
        get
        {
            var parts = (UserName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
        }
    }

    /// <summary>Builds the menu for the signed-in user and opens the first page.</summary>
    public async Task StartAsync()
    {
        BuildNavigation();
        await NavigateAsync(NavItems[0]);
    }

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

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (_navigation.CurrentPage is { } page && !await page.OnNavigatingFromAsync())
        {
            return;
        }

        await _services.GetRequiredService<SessionCoordinator>().SignOutAsync();
    }

    private void BuildNavigation()
    {
        NavItems.Clear();
        FooterItems.Clear();

        NavItems.Add(new NavItem(Strings.Nav_Dashboard, "Icon.Home", typeof(DashboardViewModel)));
        NavItems.Add(new NavItem(Strings.Nav_Items, "Icon.Items", typeof(ItemsViewModel)));
        NavItems.Add(new NavItem(Strings.Nav_Receipts, "Icon.Receipts", typeof(ReceiptsViewModel)));
        NavItems.Add(new NavItem(Strings.Nav_StockTakes, "Icon.StockTakes", typeof(StockTakesViewModel)));

        if (_currentUser.Has(Permission.ViewReports))
        {
            NavItems.Add(new NavItem(Strings.Nav_Reports, "Icon.Reports", typeof(ReportsViewModel)));
        }

        NavItems.Add(new NavItem(Strings.Nav_Catalogs, "Icon.Catalogs", typeof(CatalogsViewModel)));

        if (_currentUser.Has(Permission.ManageUsers))
        {
            FooterItems.Add(new NavItem(Strings.Nav_Users, "Icon.Users", typeof(UsersViewModel)));
        }
    }

    private void OnNavigated()
    {
        OnPropertyChanged(nameof(CurrentPage));
        var type = _navigation.CurrentPage?.GetType();
        foreach (var item in NavItems.Concat(FooterItems))
        {
            item.IsSelected = item.PageType == type;
        }
    }

    private void OnUserChanged()
    {
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserRole));
        OnPropertyChanged(nameof(UserInitials));
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using StockHelper.App.ViewModels;

namespace StockHelper.App.Services;

public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    event EventHandler? Navigated;

    Task<bool> NavigateToAsync<TPage>() where TPage : PageViewModel;

    Task<bool> NavigateToAsync(Type pageType);
}

public sealed class NavigationService(IServiceProvider services) : ObservableObject, INavigationService
{
    public PageViewModel? CurrentPage { get; private set; }

    public event EventHandler? Navigated;

    public Task<bool> NavigateToAsync<TPage>() where TPage : PageViewModel => NavigateToAsync(typeof(TPage));

    public async Task<bool> NavigateToAsync(Type pageType)
    {
        if (CurrentPage is not null)
        {
            if (CurrentPage.GetType() == pageType)
            {
                await CurrentPage.OnNavigatedToAsync();
                return true;
            }

            if (!await CurrentPage.OnNavigatingFromAsync())
            {
                return false;
            }
        }

        // Pages are transient: every visit starts from fresh data.
        var page = (PageViewModel)services.GetRequiredService(pageType);
        CurrentPage = page;
        OnPropertyChanged(nameof(CurrentPage));
        Navigated?.Invoke(this, EventArgs.Empty);
        await page.OnNavigatedToAsync();
        return true;
    }
}

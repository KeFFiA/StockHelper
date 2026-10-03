using CommunityToolkit.Mvvm.ComponentModel;

namespace StockHelper.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Runs an operation while <see cref="IsBusy"/> is set. Exceptions bubble to the global handler.</summary>
    protected async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A top-level section shown in the shell content area.</summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    /// <summary>Called every time the page becomes active.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    /// <summary>Called before leaving the page; return false to cancel navigation.</summary>
    public virtual Task<bool> OnNavigatingFromAsync() => Task.FromResult(true);
}

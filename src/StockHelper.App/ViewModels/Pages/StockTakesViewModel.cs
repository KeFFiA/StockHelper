using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Security;

namespace StockHelper.App.ViewModels.Pages;

/// <summary>Stock-take history; hosts the counting session as a sub-view.</summary>
public sealed partial class StockTakesViewModel(
    IStockTakeRepository stockTakes,
    StockTakeSessionFactory sessionFactory,
    IExportService export,
    ICurrentUserService currentUser) : PageViewModel
{
    public override string Title => Strings.Nav_StockTakes;

    public bool CanEdit => currentUser.Has(Permission.EditStockTakes);

    public ObservableCollection<StockTakeSummary> StockTakes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDraft), nameof(CanStart))]
    public partial StockTakeSummary? Draft { get; set; }

    [ObservableProperty]
    public partial StockTakeSummary? SelectedStockTake { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    public partial StockTakeSessionViewModel? Session { get; set; }

    public bool HasDraft => Draft is not null;

    public bool CanStart => CanEdit && Draft is null;

    public bool IsListVisible => Session is null;

    public string Summary
    {
        get
        {
            var last = StockTakes.FirstOrDefault(s => s.Status == StockTakeStatus.Completed);
            return last is null
                ? Strings.StockTakes_NoneCompleted
                : string.Format(Strings.StockTakes_LastCompleted, last.Date.ToLocalTime());
        }
    }

    public override async Task OnNavigatedToAsync()
    {
        await LoadAsync();

        // Jump straight into an unfinished count: that is almost always why the user opened the page.
        if (Session is null && Draft is not null && CanEdit)
        {
            await OpenAsync(Draft.Id);
        }
    }

    public override Task<bool> OnNavigatingFromAsync() => Task.FromResult(true);

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var list = await stockTakes.GetSummariesAsync();
        StockTakes.Clear();
        foreach (var item in list)
        {
            StockTakes.Add(item);
        }

        Draft = list.FirstOrDefault(s => s.Status == StockTakeStatus.Draft);
        OnPropertyChanged(nameof(Summary));
    });

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!CanStart)
        {
            return;
        }

        var draft = await stockTakes.CreateDraftAsync(DateTime.UtcNow, null);
        await LoadAsync();
        await OpenAsync(draft.Id);
    }

    [RelayCommand]
    private Task ContinueAsync() => Draft is null ? Task.CompletedTask : OpenAsync(Draft.Id);

    [RelayCommand]
    private Task OpenSelectedAsync() => SelectedStockTake is null ? Task.CompletedTask : OpenAsync(SelectedStockTake.Id);

    [RelayCommand]
    private Task ExportAsync() => export.ExportAsync(
        Strings.StockTakes_History,
        StockTakes.ToList(),
        [
            new ExportColumn<StockTakeSummary>(Strings.StockTakes_Date, s => s.Date, ExportFormat.DateTime),
            new ExportColumn<StockTakeSummary>(Strings.Common_Status, s => Converters.EnumDisplay.Get(s.Status)),
            new ExportColumn<StockTakeSummary>(Strings.StockTakes_Counted, s => s.CountedItems, ExportFormat.Integer),
            new ExportColumn<StockTakeSummary>(Strings.Items_Note, s => s.Note),
            new ExportColumn<StockTakeSummary>(Strings.Common_CreatedBy, s => s.CreatedBy),
            new ExportColumn<StockTakeSummary>(Strings.StockTakes_CompletedAt, s => s.CompletedAt, ExportFormat.DateTime),
            new ExportColumn<StockTakeSummary>(Strings.StockTakes_CompletedBy, s => s.CompletedBy),
        ]);

    private async Task OpenAsync(int id)
    {
        var session = sessionFactory.Create(id);
        session.Closed += async (_, _) =>
        {
            Session = null;
            await LoadAsync();
        };
        await session.LoadAsync();
        Session = session;
    }
}

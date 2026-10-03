using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels.Pages;

/// <summary>Analytics and reports. Every section works on one shared data snapshot.</summary>
public sealed partial class ReportsViewModel : PageViewModel
{
    private readonly IAnalyticsService _analytics;
    private readonly IExportService _export;

    private readonly IDialogService _dialogs;

    public ReportsViewModel(IAnalyticsService analytics, IExportService export, IDialogService dialogs)
    {
        _analytics = analytics;
        _export = export;
        _dialogs = dialogs;
        Stock = new StockReportSection();
        Purchase = new PurchaseReportSection();
        Consumption = new ConsumptionReportSection();
        History = new HistoryReportSection();
        Sections = [Stock, Purchase, Consumption, History];
        SelectedSection = Sections[0];
        Consumption.PeriodChanged += (_, _) => Consumption.Recalculate(Data);
    }

    public override string Title => Strings.Nav_Reports;

    public override System.Windows.Input.ICommand? RefreshShortcut => LoadCommand;

    public override System.Windows.Input.ICommand? ExportShortcut => ExportCommand;

    public IReadOnlyList<ReportSection> Sections { get; }

    public StockReportSection Stock { get; }

    public PurchaseReportSection Purchase { get; }

    public ConsumptionReportSection Consumption { get; }

    public HistoryReportSection History { get; }

    [ObservableProperty]
    public partial ReportSection SelectedSection { get; set; }

    [ObservableProperty]
    public partial AnalyticsResult? Data { get; private set; }

    public string Subtitle => Data is null
        ? string.Empty
        : string.Format(Strings.Reports_Subtitle, Data.Options.ForecastWindowDays, Data.Options.PurchaseHorizonDays);

    public override Task OnNavigatedToAsync() => LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        Data = await _analytics.LoadAsync();
        foreach (var section in Sections)
        {
            section.Recalculate(Data);
        }

        OnPropertyChanged(nameof(Subtitle));
    });

    /// <summary>One export button: asks whether to export the open report or all of them.</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Data is null)
        {
            return;
        }

        var choice = _dialogs.Choose(Strings.Reports_ExportTitle, Strings.Reports_ExportMessage,
        [
            new DialogOption(string.Format(Strings.Reports_ExportCurrent, SelectedSection.Title), Strings.Reports_ExportCurrentHint, SelectedSection.IconKey),
            new DialogOption(Strings.Reports_ExportAll, Strings.Reports_ExportAllHint, "Icon.Excel"),
        ]);

        switch (choice)
        {
            case 0:
                await _export.ExportAsync(SelectedSection.Title, [SelectedSection.BuildSheet()]);
                break;
            case 1:
                await _export.ExportAsync(Strings.Reports_AllFileName, [.. Sections.Select(s => s.BuildSheet())]);
                break;
        }
    }
}

public abstract partial class ReportSection : ObservableObject
{
    protected ReportSection(string title, string iconKey, string hint)
    {
        Title = title;
        IconKey = iconKey;
        Hint = hint;
    }

    public string Title { get; }

    public string IconKey { get; }

    public string Hint { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; protected set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => OnFilterChanged();

    public abstract void Recalculate(AnalyticsResult? data);

    public abstract ExportSheet BuildSheet();

    protected abstract void OnFilterChanged();
}

public sealed class StockReportSection : ReportSection
{
    public StockReportSection()
        : base(Strings.Reports_Stock, "Icon.Items", Strings.Reports_StockHint)
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is ItemStockStatus s && TextSearch.Matches(SearchText, s.Item.Name, s.Item.Code, s.Item.Category?.Name);
    }

    public ObservableCollection<ItemStockStatus> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public override void Recalculate(AnalyticsResult? data)
    {
        Rows.Clear();
        foreach (var status in (data?.Statuses ?? []).OrderBy(s => s.Item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Rows.Add(status);
        }

        OnFilterChanged();
    }

    protected override void OnFilterChanged()
    {
        RowsView.Refresh();
        var visible = RowsView.Cast<ItemStockStatus>().ToList();
        Summary = string.Format(Strings.Reports_StockSummary, visible.Count, visible.Count(s => s.IsBelowMinimum), visible.Count(s => s.IsRunningOut));
    }

    public override ExportSheet BuildSheet() => ExportSheet.From(
        Title, Title, Strings.Reports_EstimatedNote, RowsView.Cast<ItemStockStatus>(),
        [
            new ExportColumn<ItemStockStatus>(Strings.Items_Name, s => s.Item.Name),
            new ExportColumn<ItemStockStatus>(Strings.Items_Category, s => s.Item.Category?.Name),
            new ExportColumn<ItemStockStatus>(Strings.Items_Unit, s => s.Item.Unit?.Name),
            new ExportColumn<ItemStockStatus>(Strings.Reports_LastCount, s => s.LastCountedStock, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Reports_LastCountDate, s => s.LastCountDate, ExportFormat.Date),
            new ExportColumn<ItemStockStatus>(Strings.Reports_ReceivedSince, s => s.ReceivedSinceCount, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Reports_EstimatedStock, s => s.EstimatedStock, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Items_MinStock, s => s.Item.MinStock, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Reports_AvgDaily, s => s.AverageDailyConsumption, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Reports_DaysLeft, s => s.DaysLeft is { } d ? Math.Floor(d) : null, ExportFormat.Integer),
            new ExportColumn<ItemStockStatus>(Strings.Reports_RunOutDate, s => s.RunOutDate, ExportFormat.Date),
        ]);
}

public sealed class PurchaseReportSection : ReportSection
{
    public PurchaseReportSection()
        : base(Strings.Reports_Purchase, "Icon.Cart", Strings.Reports_PurchaseHint)
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is ItemStockStatus s && TextSearch.Matches(SearchText, s.Item.Name, s.Item.Code, s.Item.Category?.Name);
    }

    public ObservableCollection<ItemStockStatus> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public override void Recalculate(AnalyticsResult? data)
    {
        Rows.Clear();
        foreach (var status in data?.PurchaseList ?? [])
        {
            Rows.Add(status);
        }

        OnFilterChanged();
    }

    protected override void OnFilterChanged()
    {
        RowsView.Refresh();
        var visible = RowsView.Cast<ItemStockStatus>().ToList();
        Summary = string.Format(Strings.Reports_PurchaseSummary, visible.Count, visible.Sum(s => s.SuggestedQuantity * s.Item.Price));
    }

    public override ExportSheet BuildSheet() => ExportSheet.From(
        Title, Title, Summary, RowsView.Cast<ItemStockStatus>(),
        [
            new ExportColumn<ItemStockStatus>(Strings.Items_Name, s => s.Item.Name),
            new ExportColumn<ItemStockStatus>(Strings.Items_Code, s => s.Item.Code),
            new ExportColumn<ItemStockStatus>(Strings.Items_Category, s => s.Item.Category?.Name),
            new ExportColumn<ItemStockStatus>(Strings.Items_Unit, s => s.Item.Unit?.Name),
            new ExportColumn<ItemStockStatus>(Strings.Reports_EstimatedStock, s => s.EstimatedStock, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Items_MinStock, s => s.Item.MinStock, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Reports_DaysLeft, s => s.DaysLeft is { } d ? Math.Floor(d) : null, ExportFormat.Integer),
            new ExportColumn<ItemStockStatus>(Strings.Reports_Suggested, s => s.SuggestedQuantity, ExportFormat.Quantity),
            new ExportColumn<ItemStockStatus>(Strings.Items_Price, s => s.Item.Price, ExportFormat.Money),
            new ExportColumn<ItemStockStatus>(Strings.Receipts_Amount, s => s.SuggestedQuantity * s.Item.Price, ExportFormat.Money),
        ]);
}

public sealed partial class ConsumptionReportSection : ReportSection
{
    public ConsumptionReportSection()
        : base(Strings.Reports_Consumption, "Icon.Reports", Strings.Reports_ConsumptionHint)
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is ItemConsumption c && TextSearch.Matches(SearchText, c.Item.Name, c.Item.Code, c.Item.Category?.Name);
        FromDate = DateTime.Today.AddDays(-89);
        ToDate = DateTime.Today;
    }

    public event EventHandler? PeriodChanged;

    public ObservableCollection<ItemConsumption> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    partial void OnFromDateChanged(DateTime? value) => PeriodChanged?.Invoke(this, EventArgs.Empty);

    partial void OnToDateChanged(DateTime? value) => PeriodChanged?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SetPeriod(string? days)
    {
        var today = DateTime.Today;
        FromDate = int.TryParse(days, out var n) && n > 0 ? today.AddDays(-n + 1) : null;
        ToDate = today;
    }

    public override void Recalculate(AnalyticsResult? data)
    {
        Rows.Clear();
        if (data is not null)
        {
            var from = FromDate?.Date.ToUniversalTime() ?? DateTime.MinValue;
            var to = (ToDate ?? DateTime.Today).Date.AddDays(1).AddTicks(-1).ToUniversalTime();
            foreach (var row in data.Consumption(from, to))
            {
                Rows.Add(row);
            }
        }

        OnFilterChanged();
    }

    protected override void OnFilterChanged()
    {
        RowsView.Refresh();
        var visible = RowsView.Cast<ItemConsumption>().ToList();
        Summary = string.Format(Strings.Reports_ConsumptionSummary, visible.Count, visible.Sum(c => c.Cost), visible.Count(c => c.HasDiscrepancy));
    }

    public override ExportSheet BuildSheet() => ExportSheet.From(
        Title, Title,
        string.Format(Strings.Common_PeriodFormat, FromDate?.ToString("d") ?? "…", ToDate?.ToString("d") ?? "…"),
        RowsView.Cast<ItemConsumption>(),
        [
            new ExportColumn<ItemConsumption>(Strings.Items_Name, c => c.Item.Name),
            new ExportColumn<ItemConsumption>(Strings.Items_Category, c => c.Item.Category?.Name),
            new ExportColumn<ItemConsumption>(Strings.Items_Unit, c => c.Item.Unit?.Name),
            new ExportColumn<ItemConsumption>(Strings.Issues_Issued, c => c.UsesIssues ? c.Issued : null, ExportFormat.Quantity),
            new ExportColumn<ItemConsumption>(Strings.Issues_Returned, c => c.UsesIssues ? c.Returned : null, ExportFormat.Quantity),
            new ExportColumn<ItemConsumption>(Strings.Reports_ConsumptionQty, c => c.Consumption, ExportFormat.Quantity),
            new ExportColumn<ItemConsumption>(Strings.Reports_Unaccounted, c => c.UsesIssues ? c.Unaccounted : null, ExportFormat.Quantity),
            new ExportColumn<ItemConsumption>(Strings.Reports_Method, c => c.UsesIssues ? Strings.Reports_MethodIssues : Strings.Reports_MethodCounts),
            new ExportColumn<ItemConsumption>(Strings.Reports_Cost, c => c.Cost, ExportFormat.Money),
            new ExportColumn<ItemConsumption>(Strings.Reports_Days, c => c.Days, ExportFormat.Integer),
            new ExportColumn<ItemConsumption>(Strings.Reports_AvgDaily, c => c.AverageDaily, ExportFormat.Quantity),
            new ExportColumn<ItemConsumption>(Strings.Reports_Discrepancy, c => c.HasDiscrepancy ? Strings.Common_Yes : null),
        ]);
}

/// <summary>Per-item history of consumption periods between stock-takes.</summary>
public sealed class HistoryReportSection : ReportSection
{
    public HistoryReportSection()
        : base(Strings.Reports_History, "Icon.History", Strings.Reports_HistoryHint)
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is HistoryRow r && TextSearch.Matches(SearchText, r.ItemName, r.CategoryName);
    }

    public ObservableCollection<HistoryRow> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    private AnalyticsResult? _data;

    public override void Recalculate(AnalyticsResult? data)
    {
        _data = data;
        Rows.Clear();
        if (data is not null)
        {
            foreach (var item in data.Snapshot.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                foreach (var period in StockCalculator.GetPeriods(data.Snapshot, item.Id).Reverse())
                {
                    Rows.Add(new HistoryRow(item.Name, item.Category?.Name, item.Unit?.Name, period));
                }
            }
        }

        OnFilterChanged();
    }

    protected override void OnFilterChanged()
    {
        RowsView.Refresh();
        var visible = RowsView.Cast<HistoryRow>().ToList();
        Summary = string.Format(Strings.Reports_HistorySummary, _data?.Snapshot.StockTakes.Count ?? 0, visible.Count(r => r.Period.IsDiscrepancy));
    }

    /// <summary>Matrix: items × completed stock-takes with the counted totals.</summary>
    public override ExportSheet BuildSheet()
    {
        var takes = _data?.Snapshot.StockTakes.OrderBy(s => s.Date).ToList() ?? [];
        var headers = new List<string> { Strings.Items_Name, Strings.Items_Category, Strings.Items_Unit };
        headers.AddRange(takes.Select(t => t.Date.ToLocalTime().ToString("g")));
        var formats = new List<ExportFormat> { ExportFormat.Text, ExportFormat.Text, ExportFormat.Text };
        formats.AddRange(takes.Select(_ => ExportFormat.Quantity));

        var rows = (_data?.Snapshot.Items ?? [])
            .Where(i => takes.Any(t => t.CountedByItem.ContainsKey(i.Id)))
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(i => new object?[] { i.Name, i.Category?.Name, i.Unit?.Name }
                .Concat(takes.Select(t => t.CountedByItem.TryGetValue(i.Id, out var q) ? (object?)q : null))
                .ToArray())
            .ToList();

        return new ExportSheet(Title, Strings.StockTakes_History, Strings.Reports_HistoryExportNote, headers, formats, rows);
    }
}

public sealed record HistoryRow(string ItemName, string? CategoryName, string? UnitName, ConsumptionPeriod Period);

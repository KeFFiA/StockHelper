using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels.Pages;

/// <summary>Analytics and reports. Every section works on one shared data snapshot.</summary>
public sealed partial class ReportsViewModel : PageViewModel
{
    private readonly IAnalyticsService _analytics;
    private readonly IExportService _export;

    private readonly IDialogService _dialogs;

    /// <summary>Set before navigating here to open the charts of one item.</summary>
    public static int? RequestedChartItemId { get; set; }

    public ReportsViewModel(IAnalyticsService analytics, IExportService export, IDialogService dialogs, ICurrentUserService currentUser)
    {
        _analytics = analytics;
        _export = export;
        _dialogs = dialogs;
        Charts = new ChartsReportSection(currentUser.Has(Permission.ViewCosts));
        Stock = new StockReportSection();
        Purchase = new PurchaseReportSection();
        Consumption = new ConsumptionReportSection();
        History = new HistoryReportSection();
        Sections = [Stock, Purchase, Consumption, History, Charts];
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

    public ChartsReportSection Charts { get; }

    [ObservableProperty]
    public partial ReportSection SelectedSection { get; set; }

    [ObservableProperty]
    public partial AnalyticsResult? Data { get; private set; }

    public string Subtitle => Data is null
        ? string.Empty
        : string.Format(Strings.Reports_Subtitle, Data.Options.ForecastWindowDays, Data.Options.PurchaseHorizonDays);

    public override async Task OnNavigatedToAsync()
    {
        await LoadAsync();
        if (RequestedChartItemId is { } itemId)
        {
            RequestedChartItemId = null;
            SelectedSection = Charts;
            Charts.SelectItem(itemId);
        }
    }

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

public enum ChartPeriod
{
    Month,
    Quarter,
    HalfYear,
    Year,
}

public sealed record ChartPeriodOption(ChartPeriod Period, string Title);

public sealed record ChartItemOption(Item? Item, string Title);

/// <summary>Small stock chart of one item in the "all items" view.</summary>
public sealed record ItemChart(int ItemId, string Name, string? UnitName, decimal MinStock, IReadOnlyList<StockPoint> Points, string ConsumptionText);

/// <summary>Consumption bars and stock history for a chosen period (month, 3 / 6 months, year).</summary>
public sealed partial class ChartsReportSection : ReportSection
{
    private readonly bool _canViewCosts;
    private AnalyticsResult? _data;
    private IReadOnlyList<(string Label, decimal Value, string Tooltip)> _buckets = [];

    public ChartsReportSection(bool canViewCosts)
        : base(Strings.Reports_Charts, "Icon.Trend", Strings.Reports_ChartsHint)
    {
        _canViewCosts = canViewCosts;
        Periods =
        [
            new ChartPeriodOption(ChartPeriod.Month, Strings.ChartPeriod_Month),
            new ChartPeriodOption(ChartPeriod.Quarter, Strings.ChartPeriod_Quarter),
            new ChartPeriodOption(ChartPeriod.HalfYear, Strings.ChartPeriod_HalfYear),
            new ChartPeriodOption(ChartPeriod.Year, Strings.ChartPeriod_Year),
        ];
        SelectedPeriod = Periods[1];
    }

    public IReadOnlyList<ChartPeriodOption> Periods { get; }

    public ObservableCollection<ChartItemOption> ItemOptions { get; } = [];

    [ObservableProperty]
    public partial ChartPeriodOption SelectedPeriod { get; set; }

    [ObservableProperty]
    public partial ChartItemOption? SelectedItem { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Controls.BarPoint> Bars { get; private set; } = [];

    [ObservableProperty]
    public partial string BarsTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<StockPoint>? StockPoints { get; private set; }

    [ObservableProperty]
    public partial decimal MinStock { get; private set; }

    [ObservableProperty]
    public partial string? UnitName { get; private set; }

    /// <summary>"All items": a stock chart per item (stock is per item, units differ, so there is no common chart).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ItemChart> ItemCharts { get; private set; } = [];

    public bool HasItem => SelectedItem?.Item is not null;

    public bool IsAllItems => SelectedItem is not null && SelectedItem.Item is null;

    /// <summary>Consumption bars: per item in its unit, or for all items in rubles (only for roles that see costs).</summary>
    public bool ShowBars => HasItem || (IsAllItems && _canViewCosts);

    partial void OnSelectedPeriodChanged(ChartPeriodOption value) => Rebuild();

    partial void OnSelectedItemChanged(ChartItemOption? value)
    {
        OnPropertyChanged(nameof(HasItem));
        OnPropertyChanged(nameof(IsAllItems));
        OnPropertyChanged(nameof(ShowBars));
        Rebuild();
    }

    [RelayCommand]
    private void OpenItem(ItemChart? chart)
    {
        if (chart is not null)
        {
            SelectItem(chart.ItemId);
        }
    }

    /// <summary>Opens the charts of one item (from the item panel).</summary>
    public void SelectItem(int itemId) =>
        SelectedItem = ItemOptions.FirstOrDefault(o => o.Item?.Id == itemId) ?? SelectedItem;

    public override void Recalculate(AnalyticsResult? data)
    {
        _data = data;
        var selectedId = SelectedItem?.Item?.Id;
        ItemOptions.Clear();
        ItemOptions.Add(new ChartItemOption(null, Strings.Reports_ChartsAllItems));

        foreach (var item in (data?.Snapshot.Items ?? []).Where(i => !i.IsArchived).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ItemOptions.Add(new ChartItemOption(item, item.Name));
        }

        SelectedItem = ItemOptions.FirstOrDefault(o => o.Item?.Id == selectedId) ?? ItemOptions.FirstOrDefault();
        Rebuild();
    }

    protected override void OnFilterChanged()
    {
    }

    public override ExportSheet BuildSheet() => IsAllItems && !_canViewCosts
        ? new(
            Title,
            $"{Title}: {SelectedItem?.Title}",
            SelectedPeriod.Title,
            [Strings.Items_Name, Strings.Reports_ConsumptionQty],
            [ExportFormat.Text, ExportFormat.Text],
            [.. ItemCharts.Select(c => new object?[] { c.Name, c.ConsumptionText })])
        : new(
        Title,
        $"{Title}: {SelectedItem?.Title}",
        $"{SelectedPeriod.Title} · {BarsTitle}",
        [Strings.Reports_ChartsBucket, Strings.Reports_ChartsValue],
        [ExportFormat.Text, SelectedItem?.Item is null ? ExportFormat.Money : ExportFormat.Quantity],
        [.. _buckets.Select(b => new object?[] { b.Label, b.Value })]);

    private void Rebuild()
    {
        if (_data is null || SelectedItem is null)
        {
            Bars = [];
            StockPoints = null;
            ItemCharts = [];
            return;
        }

        var item = SelectedItem.Item;
        var buckets = BuildBuckets(SelectedPeriod.Period, DateTime.Now);
        _buckets = [.. buckets.Select(b =>
        {
            var from = b.Start.ToUniversalTime();
            var to = (b.End > DateTime.Now ? DateTime.Now : b.End).ToUniversalTime();
            var value = item is null
                ? ChartCalculator.ConsumptionCost(_data.Snapshot, from, to)
                : ChartCalculator.ConsumptionQuantity(_data.Snapshot, item.Id, from, to);
            value = Math.Max(0, value);
            var tooltip = item is null
                ? string.Format(Strings.Chart_BucketTooltipCost, b.Title, value)
                : string.Format(Strings.Chart_BucketTooltipQty, b.Title, NumberInput.Format(Math.Round(value, 2)), item.Unit?.Name);
            return (b.Label, value, tooltip);
        })];

        var max = _buckets.Count == 0 ? 0 : _buckets.Max(b => b.Value);
        Bars = [.. _buckets.Select((b, i) => new Controls.BarPoint(
            b.Label,
            item is null ? Compact.Money(b.Value) : NumberInput.Format(Math.Round(b.Value, 1)),
            max <= 0 ? 0 : (double)(b.Value / max),
            b.Tooltip,
            i == _buckets.Count - 1))];

        var total = _buckets.Sum(b => b.Value);
        BarsTitle = item is null ? Strings.Reports_ChartsCostTitle : string.Format(Strings.Reports_ChartsQtyTitle, item.Unit?.Name);
        Summary = item is null
            ? string.Format(Strings.Reports_ChartsSummaryCost, total)
            : string.Format(Strings.Reports_ChartsSummaryQty, NumberInput.Format(Math.Round(total, 2)), item.Unit?.Name);

        if (item is null)
        {
            StockPoints = null;
            var fromUtc = buckets[0].Start.ToUniversalTime();
            ItemCharts = [.. _data.Snapshot.Items
                .Where(i => !i.IsArchived)
                .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(i => new ItemChart(
                    i.Id,
                    i.Name,
                    i.Unit?.Name,
                    i.MinStock,
                    ChartCalculator.GetStockHistory(_data.Snapshot, i.Id, fromUtc, _data.NowUtc),
                    string.Format(
                        Strings.Reports_ChartsItemConsumption,
                        NumberInput.Format(Math.Round(Math.Max(0, ChartCalculator.ConsumptionQuantity(_data.Snapshot, i.Id, fromUtc, _data.NowUtc)), 2)),
                        i.Unit?.Name)))];
            if (!_canViewCosts)
            {
                Summary = string.Empty;
            }
        }
        else
        {
            ItemCharts = [];
            var now = _data.NowUtc;
            StockPoints = ChartCalculator.GetStockHistory(_data.Snapshot, item.Id, buckets[0].Start.ToUniversalTime(), now);
            MinStock = item.MinStock;
            UnitName = item.Unit?.Name;
        }
    }

    /// <summary>Weeks (Monday-based) for a month or a quarter, calendar months for half a year or a year.</summary>
    private static List<(DateTime Start, DateTime End, string Label, string Title)> BuildBuckets(ChartPeriod period, DateTime now)
    {
        var today = now.Date;
        var result = new List<(DateTime, DateTime, string, string)>();
        if (period is ChartPeriod.Month or ChartPeriod.Quarter)
        {
            var days = period == ChartPeriod.Month ? 30 : 91;
            var start = today.AddDays(-days + 1);
            start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
            for (var week = start; week <= today; week = week.AddDays(7))
            {
                var title = $"{week:dd.MM} — {week.AddDays(6):dd.MM}";
                result.Add((week, week.AddDays(7), string.Format(Strings.Chart_WeekLabel, week), title));
            }
        }
        else
        {
            var months = period == ChartPeriod.HalfYear ? 6 : 12;
            var first = new DateTime(today.Year, today.Month, 1).AddMonths(-months + 1);
            for (var month = first; month <= today; month = month.AddMonths(1))
            {
                var label = month.ToString("MMM", CultureInfo.CurrentCulture).TrimEnd('.');
                result.Add((month, month.AddMonths(1), label, month.ToString("MMMM yyyy", CultureInfo.CurrentCulture)));
            }
        }

        return result;
    }
}

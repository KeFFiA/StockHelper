using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels.Pages;

public sealed class StockTakeSessionFactory(
    IStockTakeRepository stockTakes,
    IItemRepository items,
    ILookupRepository<StorageLocation> locations,
    ILookupRepository<Unit> units,
    IStockDataReader stockData,
    IDialogService dialogs,
    INotificationService notifications,
    IExportService export,
    ICurrentUserService currentUser,
    ILogger<StockTakeSessionViewModel> logger)
{
    public StockTakeSessionViewModel Create(int stockTakeId) =>
        new(stockTakeId, stockTakes, items, locations, units, stockData, dialogs, notifications, export, currentUser, logger);
}

/// <summary>Counting screen: one row per item, quantities are entered per storage location and saved immediately.</summary>
public sealed partial class StockTakeSessionViewModel : ViewModelBase
{
    private readonly int _stockTakeId;
    private readonly IStockTakeRepository _stockTakes;
    private readonly IItemRepository _items;
    private readonly ILookupRepository<StorageLocation> _locations;
    private readonly ILookupRepository<Unit> _units;
    private readonly IStockDataReader _stockData;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IExportService _export;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger _logger;

    // (itemId, locationId) → counted quantity: the source of truth for the whole session.
    private readonly Dictionary<(int ItemId, int LocationId), decimal> _counts = [];

    // Enter and focus loss can both commit the same cell: saves run one at a time.
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _headerLoaded;

    public StockTakeSessionViewModel(
        int stockTakeId,
        IStockTakeRepository stockTakes,
        IItemRepository items,
        ILookupRepository<StorageLocation> locations,
        ILookupRepository<Unit> units,
        IStockDataReader stockData,
        IDialogService dialogs,
        INotificationService notifications,
        IExportService export,
        ICurrentUserService currentUser,
        ILogger logger)
    {
        _units = units;
        _stockTakeId = stockTakeId;
        _stockTakes = stockTakes;
        _items = items;
        _locations = locations;
        _stockData = stockData;
        _dialogs = dialogs;
        _notifications = notifications;
        _export = export;
        _currentUser = currentUser;
        _logger = logger;

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is StockTakeRow row
            && (!OnlyUncounted || !row.IsCountedHere)
            && TextSearch.Matches(SearchText, row.Name, row.Code, row.CategoryName);
    }

    public event EventHandler? Closed;

    public ObservableCollection<StockTakeRow> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public ObservableCollection<StorageLocation> Locations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraft), nameof(IsCompleted), nameof(CanEdit), nameof(CanReopen), nameof(StatusText))]
    public partial StockTakeStatus Status { get; set; }

    [ObservableProperty]
    public partial DateTime? Date { get; set; }

    [ObservableProperty]
    public partial string Time { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StorageLocation? Location { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OnlyUncounted { get; set; }

    [ObservableProperty]
    public partial string? HeaderError { get; set; }

    public bool IsDraft => Status == StockTakeStatus.Draft;

    public bool IsCompleted => Status == StockTakeStatus.Completed;

    public bool CanEdit => IsDraft && _currentUser.Has(Permission.EditStockTakes);

    public bool CanReopen => IsCompleted && _currentUser.Has(Permission.ReopenStockTakes);

    public string StatusText => Converters.EnumDisplay.Get(Status);

    public string Heading => Date is { } d ? string.Format(Strings.StockTakes_SessionHeading, d) : Strings.Nav_StockTakes;

    public int CountedCount => Rows.Count(r => r.Total is not null);

    public string Progress => string.Format(Strings.StockTakes_Progress, CountedCount, Rows.Count);

    public double ProgressValue => Rows.Count == 0 ? 0 : 100.0 * CountedCount / Rows.Count;

    public async Task LoadAsync()
    {
        var stockTake = await _stockTakes.GetWithLinesAsync(_stockTakeId)
            ?? throw new DomainException(DomainErrorCode.NotFound, "Stock-take");

        Status = stockTake.Status;
        (var date, Time) = DateTimeInput.Split(stockTake.Date);
        Date = date;
        Note = stockTake.Note ?? string.Empty;
        _headerLoaded = true;

        _counts.Clear();
        foreach (var line in stockTake.Lines)
        {
            _counts[(line.ItemId, line.StorageLocationId)] = line.CountedQuantity;
        }

        var countedItemIds = stockTake.Lines.Select(l => l.ItemId).ToHashSet();
        var usedLocationIds = stockTake.Lines.Select(l => l.StorageLocationId).ToHashSet();

        // Completed counts show exactly what was counted; drafts list every active item.
        var allItems = await _items.GetAllAsync(includeArchived: true);
        var items = allItems.Where(i => IsDraft ? !i.IsArchived || countedItemIds.Contains(i.Id) : countedItemIds.Contains(i.Id));

        var expected = await LoadExpectedAsync(stockTake.Date);
        var allUnits = await _units.GetAllAsync(includeArchived: true);

        Rows.Clear();
        foreach (var item in items)
        {
            var itemUnit = allUnits.FirstOrDefault(u => u.Id == item.UnitId);
            var units = itemUnit is null ? [] : UnitConverter.CompatibleUnits(itemUnit, allUnits);
            var row = new StockTakeRow(item, expected.TryGetValue(item.Id, out var e) ? e : null, itemUnit, units);
            row.CommitRequested += async (_, _) => await CommitAsync(row);
            Rows.Add(row);
        }

        var locations = await _locations.GetAllAsync(includeArchived: true);
        Locations.Clear();
        foreach (var location in locations.Where(l => !l.IsArchived || usedLocationIds.Contains(l.Id)))
        {
            Locations.Add(location);
        }

        Location = Locations.FirstOrDefault(l => l.Id == Location?.Id) ?? Locations.FirstOrDefault();
        RefreshRows();
        OnPropertyChanged(nameof(Heading));
    }

    partial void OnLocationChanged(StorageLocation? value) => RefreshRows();

    partial void OnSearchTextChanged(string value) => RowsView.Refresh();

    partial void OnOnlyUncountedChanged(bool value) => RowsView.Refresh();

    partial void OnDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(Heading));
        _ = SaveHeaderAsync();
    }

    partial void OnTimeChanged(string value) => _ = SaveHeaderAsync();

    [RelayCommand]
    private Task SaveNoteAsync() => SaveHeaderAsync();

    [RelayCommand]
    private void Back() => Closed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task CompleteAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var notCounted = Rows.Count - CountedCount;
        var message = notCounted > 0
            ? string.Format(Strings.StockTakes_CompleteConfirmPartial, CountedCount, Rows.Count, notCounted)
            : string.Format(Strings.StockTakes_CompleteConfirm, CountedCount);

        if (!_dialogs.Confirm(message, Strings.StockTakes_CompleteTitle, Strings.StockTakes_Complete))
        {
            return;
        }

        await _stockTakes.CompleteAsync(_stockTakeId);
        _notifications.Success(Strings.StockTakes_Completed, Heading);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (!CanReopen || !_dialogs.Confirm(Strings.StockTakes_ReopenConfirm, confirmText: Strings.StockTakes_Reopen))
        {
            return;
        }

        await _stockTakes.ReopenAsync(_stockTakeId);
        _notifications.Show(NotificationKind.Info, Strings.StockTakes_Reopened, Heading);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanEdit || !_dialogs.Confirm(Strings.StockTakes_DeleteConfirm, confirmText: Strings.Common_Delete, isDestructive: true))
        {
            return;
        }

        await _stockTakes.DeleteAsync(_stockTakeId);
        _notifications.Success(Strings.Common_DeletedNotice, Heading);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private Task ExportAsync() => _export.ExportAsync(Heading, [BuildExportSheet()]);

    private ExportSheet BuildExportSheet()
    {
        var headers = new List<string> { Strings.Items_Name, Strings.Items_Code, Strings.Items_Category, Strings.Items_Unit };
        var formats = new List<ExportFormat> { ExportFormat.Text, ExportFormat.Text, ExportFormat.Text, ExportFormat.Text };
        foreach (var location in Locations)
        {
            headers.Add(location.Name);
            formats.Add(ExportFormat.Quantity);
        }

        headers.Add(Strings.StockTakes_Total);
        formats.Add(ExportFormat.Quantity);
        headers.Add(Strings.StockTakes_Expected);
        formats.Add(ExportFormat.Quantity);

        var rows = Rows.Select(row =>
        {
            var values = new List<object?> { row.Name, row.Code, row.CategoryName, row.UnitName };
            values.AddRange(Locations.Select(l => _counts.TryGetValue((row.ItemId, l.Id), out var q) ? q : (object?)null));
            values.Add(row.Total);
            values.Add(row.Expected);
            return values.ToArray();
        }).ToList();

        return new ExportSheet(Strings.Nav_StockTakes, Heading, $"{StatusText} · {Progress}", headers, formats, rows);
    }

    private async Task CommitAsync(StockTakeRow row)
    {
        if (!CanEdit || Location is null)
        {
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            await CommitCoreAsync(row, Location);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task CommitCoreAsync(StockTakeRow row, StorageLocation location)
    {

        decimal? quantity = null;
        if (!string.IsNullOrWhiteSpace(row.CountText))
        {
            if (!NumberInput.TryParse(row.CountText, out var parsed) || parsed < 0)
            {
                row.Error = Strings.Common_InvalidNumber;
                return;
            }

            quantity = row.ToItemUnits(parsed);
        }

        var key = (row.ItemId, location.Id);
        var current = _counts.TryGetValue(key, out var existing) ? existing : (decimal?)null;
        row.Error = null;
        if (current == quantity)
        {
            row.SetCountHere(quantity);
            return;
        }

        try
        {
            await _stockTakes.SetLineAsync(_stockTakeId, row.ItemId, location.Id, quantity);
            if (quantity is null)
            {
                _counts.Remove(key);
            }
            else
            {
                _counts[key] = quantity.Value;
            }

            UpdateRow(row);
            row.FlashSaved();
            OnProgressChanged();
        }
        catch (DomainException ex)
        {
            row.Error = ErrorMessages.For(ex);
            _logger.LogWarning(ex, "Failed to save count for item {ItemId}", row.ItemId);
        }
    }

    private async Task SaveHeaderAsync()
    {
        if (!_headerLoaded || !CanEdit)
        {
            return;
        }

        HeaderError = null;
        if (!DateTimeInput.TryCombine(Date, Time, out var dateUtc))
        {
            HeaderError = Strings.Common_InvalidDateTime;
            return;
        }

        try
        {
            await _stockTakes.UpdateHeaderAsync(_stockTakeId, dateUtc, Note);
        }
        catch (DomainException ex)
        {
            HeaderError = ErrorMessages.For(ex);
        }
    }

    /// <summary>Calculated stock at the count date: helps to spot typos while counting.</summary>
    private async Task<IReadOnlyDictionary<int, decimal>> LoadExpectedAsync(DateTime atUtc)
    {
        var snapshot = await _stockData.LoadAsync();
        return StockCalculator.ExpectedStockAt(snapshot, atUtc, excludeStockTakeId: _stockTakeId);
    }

    private void RefreshRows()
    {
        foreach (var row in Rows)
        {
            UpdateRow(row);
        }

        RowsView.Refresh();
        OnProgressChanged();
    }

    private void UpdateRow(StockTakeRow row)
    {
        var here = Location is not null && _counts.TryGetValue((row.ItemId, Location.Id), out var q) ? q : (decimal?)null;
        var all = _counts.Where(kv => kv.Key.ItemId == row.ItemId).Select(kv => kv.Value).ToList();
        row.SetValues(here, all.Count == 0 ? null : all.Sum(), CanEdit);
    }

    private void OnProgressChanged()
    {
        OnPropertyChanged(nameof(CountedCount));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressValue));
    }
}

public sealed partial class StockTakeRow : ObservableObject
{
    private System.Windows.Threading.DispatcherTimer? _savedTimer;

    private decimal? _here;

    public StockTakeRow(Item item, decimal? expected, Unit? itemUnit = null, IReadOnlyList<Unit>? units = null)
    {
        ItemUnit = itemUnit;
        Units = units ?? [];
        CountUnit = itemUnit;
        ItemId = item.Id;
        Name = item.Name;
        Code = item.Code;
        CategoryName = item.Category?.Name;
        UnitName = item.Unit?.Name;
        Expected = expected;
    }

    public event EventHandler? CommitRequested;

    public int ItemId { get; }

    public string Name { get; }

    public string? Code { get; }

    public string? CategoryName { get; }

    public string? UnitName { get; }

    public decimal? Expected { get; }

    public Unit? ItemUnit { get; }

    /// <summary>Item unit and its packages: count "3 банки по 5 л" directly.</summary>
    public IReadOnlyList<Unit> Units { get; }

    public bool HasUnitChoice => Units.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountHint))]
    public partial Unit? CountUnit { get; set; }

    /// <summary>"= 15 л" when counting in packages.</summary>
    public string? CountHint => _here is { } here && CountUnit is not null && ItemUnit is not null && CountUnit.Id != ItemUnit.Id
        ? string.Format(Strings.Quantity_Converted, NumberInput.Format(here), ItemUnit.Name)
        : null;

    partial void OnCountUnitChanged(Unit? value) => SetCountHere(_here);

    public decimal ToItemUnits(decimal quantity) =>
        CountUnit is null || ItemUnit is null ? quantity : UnitConverter.ToItemUnits(quantity, CountUnit, ItemUnit);

    /// <summary>Shows the stored quantity (item units) in the selected counting unit.</summary>
    public void SetCountHere(decimal? here)
    {
        _here = here;
        CountText = here is null ? string.Empty
            : NumberInput.Format(CountUnit is null || ItemUnit is null ? here.Value : UnitConverter.FromItemUnits(here.Value, CountUnit, ItemUnit));
        OnPropertyChanged(nameof(CountHint));
    }

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difference), nameof(HasDifference))]
    public partial decimal? Total { get; private set; }

    [ObservableProperty]
    public partial bool IsCountedHere { get; private set; }

    [ObservableProperty]
    public partial bool IsEditable { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool JustSaved { get; private set; }

    /// <summary>Counted total minus calculated stock (null when either is unknown).</summary>
    public decimal? Difference => Total is { } t && Expected is { } e ? t - e : null;

    public bool HasDifference => Difference is { } d && d != 0;

    [RelayCommand]
    private void Commit() => CommitRequested?.Invoke(this, EventArgs.Empty);

    public void SetValues(decimal? here, decimal? total, bool editable)
    {
        SetCountHere(here);
        IsCountedHere = here is not null;
        Total = total;
        IsEditable = editable;
    }

    public void FlashSaved()
    {
        JustSaved = true;
        _savedTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _savedTimer.Tick += OnTick;
        _savedTimer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _savedTimer!.Stop();
        _savedTimer.Tick -= OnTick;
        JustSaved = false;
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels.Pages;

public sealed partial class ItemsViewModel : PageViewModel
{
    // Remembered for the session so a series of new items keeps the same category and unit.
    private static int? _lastCategoryId;
    private static int? _lastUnitId;

    private readonly IItemRepository _items;
    private readonly ILookupRepository<Category> _categories;
    private readonly ILookupRepository<Unit> _units;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IExportService _export;
    private readonly ICurrentUserService _currentUser;
    private readonly IAnalyticsService _analytics;

    public ItemsViewModel(
        IItemRepository items,
        ILookupRepository<Category> categories,
        ILookupRepository<Unit> units,
        IDialogService dialogs,
        INotificationService notifications,
        IExportService export,
        ICurrentUserService currentUser,
        IAnalyticsService analytics)
    {
        _items = items;
        _categories = categories;
        _units = units;
        _dialogs = dialogs;
        _notifications = notifications;
        _export = export;
        _currentUser = currentUser;
        _analytics = analytics;

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = Filter;
    }

    public override string Title => Strings.Nav_Items;

    public bool CanEdit => _currentUser.Has(Permission.ManageCatalog);

    public ObservableCollection<ItemRow> Items { get; } = [];

    public ICollectionView ItemsView { get; }

    public ObservableCollection<Category> FilterCategories { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Category? CategoryFilter { get; set; }

    [ObservableProperty]
    public partial bool ShowArchived { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    public partial ItemRow? SelectedItem { get; set; }

    [ObservableProperty]
    public partial ItemEditorViewModel? Editor { get; set; }

    public string Summary => string.Format(Strings.Items_Summary, ItemsView.Cast<object>().Count(), Items.Count);

    public override Task OnNavigatedToAsync() => LoadAsync();

    public override Task<bool> OnNavigatingFromAsync() =>
        Task.FromResult(Editor is not { IsDirty: true } || _dialogs.Confirm(Strings.Common_DiscardChanges, confirmText: Strings.Common_Discard));

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnCategoryFilterChanged(Category? value) => RefreshView();

    partial void OnShowArchivedChanged(bool value) => RefreshView();

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var selectedId = SelectedItem?.Item.Id;
        var items = await _items.GetAllAsync(includeArchived: true);
        var categories = await _categories.GetAllAsync(includeArchived: false);
        var statuses = (await _analytics.LoadAsync()).Statuses.ToDictionary(s => s.Item.Id);

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(new ItemRow(item, statuses.GetValueOrDefault(item.Id)));
        }

        var filterId = CategoryFilter?.Id;
        FilterCategories.Clear();
        foreach (var category in categories)
        {
            FilterCategories.Add(category);
        }

        CategoryFilter = FilterCategories.FirstOrDefault(c => c.Id == filterId);
        SelectedItem = Items.FirstOrDefault(i => i.Item.Id == selectedId);
        RefreshView();
    });

    [RelayCommand]
    private void ClearCategoryFilter() => CategoryFilter = null;

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanEdit || !ConfirmLeaveEditor())
        {
            return;
        }

        Editor = await CreateEditorAsync(null);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (SelectedItem is null || !ConfirmLeaveEditor())
        {
            return;
        }

        Editor = await CreateEditorAsync(SelectedItem.Item);
    }

    [RelayCommand]
    private Task ExportAsync() => _export.ExportAsync(
        Strings.Nav_Items,
        ItemsView.Cast<ItemRow>().ToList(),
        [
            new ExportColumn<ItemRow>(Strings.Items_Name, r => r.Item.Name),
            new ExportColumn<ItemRow>(Strings.Items_Code, r => r.Item.Code),
            new ExportColumn<ItemRow>(Strings.Items_Category, r => r.Item.Category?.Name),
            new ExportColumn<ItemRow>(Strings.Items_Unit, r => r.Item.Unit?.Name),
            new ExportColumn<ItemRow>(Strings.Reports_EstimatedStock, r => r.Status?.EstimatedStock, ExportFormat.Quantity),
            new ExportColumn<ItemRow>(Strings.Items_MinStock, r => r.Item.MinStock, ExportFormat.Quantity),
            new ExportColumn<ItemRow>(Strings.Reports_DaysLeft, r => r.Status?.DaysLeft is { } d ? Math.Floor(d) : null, ExportFormat.Integer),
            new ExportColumn<ItemRow>(Strings.Items_Price, r => r.Item.Price, ExportFormat.Money),
            new ExportColumn<ItemRow>(Strings.Items_Note, r => r.Item.Note),
            new ExportColumn<ItemRow>(Strings.Common_Status, r => r.Item.IsArchived ? Strings.Common_Archived : Strings.Common_Active),
        ]);

    private bool HasSelection() => SelectedItem is not null;

    private bool ConfirmLeaveEditor() =>
        Editor is not { IsDirty: true } || _dialogs.Confirm(Strings.Common_DiscardChanges, confirmText: Strings.Common_Discard);

    private async Task<ItemEditorViewModel> CreateEditorAsync(Item? item)
    {
        var categories = (await _categories.GetAllAsync(includeArchived: true))
            .Where(c => !c.IsArchived || c.Id == item?.CategoryId).ToList();
        var units = (await _units.GetAllAsync(includeArchived: true))
            .Where(u => !u.IsArchived || u.Id == item?.UnitId).ToList();

        var editor = new ItemEditorViewModel(item, categories, units, CanEdit);
        if (item is null)
        {
            var defaultUnitName = Strings.Seed_Units.Split('|')[0];
            editor.Category = categories.FirstOrDefault(c => c.Id == _lastCategoryId) ?? editor.Category;
            editor.Unit = units.FirstOrDefault(u => u.Id == _lastUnitId)
                ?? units.FirstOrDefault(u => string.Equals(u.Name, defaultUnitName, StringComparison.CurrentCultureIgnoreCase))
                ?? editor.Unit;
            editor.MarkClean();
        }

        editor.SaveRequested += async (_, _) => await SaveAsync(editor);
        editor.CancelRequested += (_, _) => Editor = null;
        editor.ArchiveRequested += async (_, _) => await ToggleArchiveAsync(editor);
        editor.DeleteRequested += async (_, _) => await DeleteAsync(editor);
        return editor;
    }

    private async Task SaveAsync(ItemEditorViewModel editor)
    {
        if (!editor.TryBuild(out var item))
        {
            return;
        }

        try
        {
            var saved = item.Id == 0 ? await _items.AddAsync(item) : await _items.UpdateAsync(item);
            _lastCategoryId = item.CategoryId;
            _lastUnitId = item.UnitId;
            Editor = null;
            _notifications.Success(item.Id == 0 ? Strings.Items_Created : Strings.Common_Saved, saved.Name);
            await LoadAsync();
            SelectedItem = Items.FirstOrDefault(i => i.Item.Id == saved.Id);
        }
        catch (DomainException ex)
        {
            editor.ErrorMessage = ErrorMessages.For(ex);
        }
        catch (ConcurrencyConflictException)
        {
            editor.ErrorMessage = Strings.Error_Concurrency;
        }
    }

    private async Task ToggleArchiveAsync(ItemEditorViewModel editor)
    {
        if (editor.Id == 0)
        {
            return;
        }

        var archive = !editor.IsArchived;
        if (archive && !_dialogs.Confirm(string.Format(Strings.Items_ArchiveConfirm, editor.Name), confirmText: Strings.Common_Archive))
        {
            return;
        }

        await _items.SetArchivedAsync(editor.Id, archive);
        Editor = null;
        _notifications.Success(archive ? Strings.Common_ArchivedNotice : Strings.Common_RestoredNotice, editor.Name);
        await LoadAsync();
    }

    private async Task DeleteAsync(ItemEditorViewModel editor)
    {
        if (editor.Id == 0)
        {
            return;
        }

        if (await _items.HasHistoryAsync(editor.Id))
        {
            _dialogs.ShowInfo(Strings.Items_DeleteHasHistory);
            return;
        }

        if (!_dialogs.Confirm(string.Format(Strings.Items_DeleteConfirm, editor.Name), confirmText: Strings.Common_Delete, isDestructive: true))
        {
            return;
        }

        await _items.DeleteAsync(editor.Id);
        Editor = null;
        _notifications.Success(Strings.Common_DeletedNotice, editor.Name);
        await LoadAsync();
    }

    private bool Filter(object obj)
    {
        if (obj is not ItemRow { Item: var item })
        {
            return false;
        }

        if (!ShowArchived && item.IsArchived)
        {
            return false;
        }

        if (CategoryFilter is not null && item.CategoryId != CategoryFilter.Id)
        {
            return false;
        }

        return TextSearch.Matches(SearchText, item.Name, item.Code, item.Category?.Name, item.Note);
    }

    private void RefreshView()
    {
        ItemsView.Refresh();
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>Catalog row with the calculated stock position (null for archived items).</summary>
public sealed record ItemRow(Item Item, ItemStockStatus? Status);

public sealed partial class ItemEditorViewModel : ObservableObject
{
    private readonly Item? _original;

    public ItemEditorViewModel(Item? item, IReadOnlyList<Category> categories, IReadOnlyList<Unit> units, bool canEdit)
    {
        _original = item;
        Categories = categories;
        Units = units;
        CanEdit = canEdit;

        Name = item?.Name ?? string.Empty;
        Code = item?.Code ?? string.Empty;
        Category = categories.FirstOrDefault(c => c.Id == item?.CategoryId) ?? (item is null ? categories.FirstOrDefault() : null);
        Unit = units.FirstOrDefault(u => u.Id == item?.UnitId) ?? (item is null ? units.FirstOrDefault() : null);
        MinStock = NumberInput.Format(item?.MinStock ?? 0);
        Price = NumberInput.Format(item?.Price ?? 0);
        Note = item?.Note ?? string.Empty;
        IsDirty = false;
    }

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? ArchiveRequested;

    public event EventHandler? DeleteRequested;

    public int Id => _original?.Id ?? 0;

    public bool IsNew => _original is null;

    public bool IsArchived => _original?.IsArchived ?? false;

    public bool CanEdit { get; }

    public string Heading => IsNew ? Strings.Items_NewHeading : Strings.Items_EditHeading;

    public string ArchiveText => IsArchived ? Strings.Common_Restore : Strings.Common_Archive;

    public IReadOnlyList<Category> Categories { get; }

    public IReadOnlyList<Unit> Units { get; }

    public bool IsDirty { get; private set; }

    public void MarkClean() => IsDirty = false;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Code { get; set; }

    [ObservableProperty]
    public partial Category? Category { get; set; }

    [ObservableProperty]
    public partial Unit? Unit { get; set; }

    [ObservableProperty]
    public partial string MinStock { get; set; }

    [ObservableProperty]
    public partial string Price { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [RelayCommand]
    private void Save() => SaveRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleArchive() => ArchiveRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this, EventArgs.Empty);

    public bool TryBuild(out Item item)
    {
        item = null!;
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = Strings.DomainError_NameRequired;
            return false;
        }

        if (Category is null || Unit is null)
        {
            ErrorMessage = Strings.Items_SelectCategoryAndUnit;
            return false;
        }

        if (!TryParseNonNegative(MinStock, out var minStock) || !TryParseNonNegative(Price, out var price))
        {
            ErrorMessage = Strings.Common_InvalidNumber;
            return false;
        }

        item = new Item
        {
            Id = Id,
            ConcurrencyStamp = _original?.ConcurrencyStamp ?? Guid.Empty,
            Name = Name,
            Code = Code,
            CategoryId = Category.Id,
            UnitId = Unit.Id,
            MinStock = minStock,
            Price = price,
            Note = Note,
            IsArchived = IsArchived,
        };
        return true;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(ErrorMessage) or nameof(IsDirty)))
        {
            IsDirty = true;
        }
    }

    private static bool TryParseNonNegative(string text, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }

        return NumberInput.TryParse(text, out value) && value >= 0;
    }
}

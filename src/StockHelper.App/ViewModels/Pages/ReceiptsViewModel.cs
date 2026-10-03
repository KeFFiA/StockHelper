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

namespace StockHelper.App.ViewModels.Pages;

public sealed partial class ReceiptsViewModel : PageViewModel
{
    private readonly IReceiptRepository _receipts;
    private readonly IItemRepository _items;
    private readonly ILookupRepository<StorageLocation> _locations;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IExportService _export;
    private readonly ICurrentUserService _currentUser;

    public ReceiptsViewModel(
        IReceiptRepository receipts,
        IItemRepository items,
        ILookupRepository<StorageLocation> locations,
        IDialogService dialogs,
        INotificationService notifications,
        IExportService export,
        ICurrentUserService currentUser)
    {
        _receipts = receipts;
        _items = items;
        _locations = locations;
        _dialogs = dialogs;
        _notifications = notifications;
        _export = export;
        _currentUser = currentUser;

        ReceiptsView = CollectionViewSource.GetDefaultView(Receipts);
        ReceiptsView.Filter = o => o is Receipt r && TextSearch.Matches(SearchText, r.Item?.Name, r.Item?.Code, r.Note, r.StorageLocation?.Name);

        var today = DateTime.Today;
        FromDate = today.AddDays(-30);
        ToDate = today;
    }

    public override string Title => Strings.Nav_Receipts;

    public override System.Windows.Input.ICommand? NewShortcut => AddCommand;

    public override System.Windows.Input.ICommand? RefreshShortcut => LoadCommand;

    public override System.Windows.Input.ICommand? ExportShortcut => ExportCommand;

    public bool CanEdit => _currentUser.Has(Permission.ManageReceipts);

    public ObservableCollection<Receipt> Receipts { get; } = [];

    public ICollectionView ReceiptsView { get; }

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Receipt? SelectedReceipt { get; set; }

    [ObservableProperty]
    public partial ReceiptEditorViewModel? Editor { get; set; }

    public string Summary
    {
        get
        {
            var visible = ReceiptsView.Cast<Receipt>().ToList();
            return string.Format(Strings.Receipts_Summary, visible.Count, visible.Sum(r => r.Amount));
        }
    }

    public override Task OnNavigatedToAsync() => LoadAsync();

    public override Task<bool> OnNavigatingFromAsync() => Task.FromResult(ConfirmLeave());

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnFromDateChanged(DateTime? value) => _ = LoadAsync();

    partial void OnToDateChanged(DateTime? value) => _ = LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var selectedId = SelectedReceipt?.Id;
        var filter = new ReceiptFilter(
            FromDate?.Date.ToUniversalTime(),
            ToDate?.Date.AddDays(1).AddTicks(-1).ToUniversalTime());

        Receipts.Clear();
        foreach (var receipt in await _receipts.GetAsync(filter))
        {
            Receipts.Add(receipt);
        }

        SelectedReceipt = Receipts.FirstOrDefault(r => r.Id == selectedId);
        RefreshView();
    });

    [RelayCommand]
    private void SetPeriod(string? days)
    {
        var today = DateTime.Today;
        if (int.TryParse(days, out var n) && n > 0)
        {
            FromDate = today.AddDays(-n + 1);
        }
        else
        {
            FromDate = null;
        }

        ToDate = today;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (CanEdit && ConfirmLeave())
        {
            Editor = await CreateEditorAsync(null);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedReceipt is not null && ConfirmLeave())
        {
            Editor = await CreateEditorAsync(SelectedReceipt);
        }
    }

    [RelayCommand]
    private Task ExportAsync() => _export.ExportAsync(
        Strings.Nav_Receipts,
        ReceiptsView.Cast<Receipt>().ToList(),
        [
            new ExportColumn<Receipt>(Strings.Receipts_Date, r => r.Date, ExportFormat.DateTime),
            new ExportColumn<Receipt>(Strings.Items_Name, r => r.Item?.Name),
            new ExportColumn<Receipt>(Strings.Items_Code, r => r.Item?.Code),
            new ExportColumn<Receipt>(Strings.Receipts_Quantity, r => r.Quantity, ExportFormat.Quantity),
            new ExportColumn<Receipt>(Strings.Items_Unit, r => r.Item?.Unit?.Name),
            new ExportColumn<Receipt>(Strings.Receipts_Price, r => r.Price, ExportFormat.Money),
            new ExportColumn<Receipt>(Strings.Receipts_Amount, r => r.Amount, ExportFormat.Money),
            new ExportColumn<Receipt>(Strings.Receipts_Location, r => r.StorageLocation?.Name),
            new ExportColumn<Receipt>(Strings.Items_Note, r => r.Note),
            new ExportColumn<Receipt>(Strings.Common_CreatedBy, r => r.CreatedBy),
        ],
        PeriodText());

    private string PeriodText() => string.Format(Strings.Common_PeriodFormat,
        FromDate?.ToString("d") ?? "…", ToDate?.ToString("d") ?? "…");

    private bool ConfirmLeave() =>
        Editor is not { IsDirty: true } || _dialogs.Confirm(Strings.Common_DiscardChanges, confirmText: Strings.Common_Discard);

    private async Task<ReceiptEditorViewModel> CreateEditorAsync(Receipt? receipt)
    {
        var items = (await _items.GetAllAsync(includeArchived: true))
            .Where(i => !i.IsArchived || i.Id == receipt?.ItemId).ToList();
        var locations = (await _locations.GetAllAsync(includeArchived: true))
            .Where(l => !l.IsArchived || l.Id == receipt?.StorageLocationId).ToList();

        var editor = new ReceiptEditorViewModel(receipt, items, locations, CanEdit);
        editor.SaveRequested += async (_, _) => await SaveAsync(editor);
        editor.CancelRequested += (_, _) => Editor = null;
        editor.DeleteRequested += async (_, _) => await DeleteAsync(editor);
        return editor;
    }

    private async Task SaveAsync(ReceiptEditorViewModel editor)
    {
        if (!editor.TryBuild(out var receipt))
        {
            return;
        }

        try
        {
            var saved = receipt.Id == 0 ? await _receipts.AddAsync(receipt) : await _receipts.UpdateAsync(receipt);

            if (editor.UpdateItemPrice && editor.Item is { } item && item.Price != receipt.Price)
            {
                // Keep the catalog price current: the latest purchase price becomes the item price.
                var fresh = await _items.GetAsync(item.Id);
                if (fresh is not null)
                {
                    fresh.Price = receipt.Price;
                    await _items.UpdateAsync(fresh);
                }
            }

            var keepAdding = editor.IsNew && editor.AddAnother;
            Editor = null;
            _notifications.Success(editor.IsNew ? Strings.Receipts_Created : Strings.Common_Saved,
                $"{editor.Item?.Name} · {NumberInput.Format(receipt.Quantity)} {editor.Item?.Unit?.Name}");
            await LoadAsync();
            SelectedReceipt = Receipts.FirstOrDefault(r => r.Id == saved.Id);

            if (keepAdding)
            {
                Editor = await CreateEditorAsync(null);
                Editor.ContinueFrom(editor);
            }
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

    private async Task DeleteAsync(ReceiptEditorViewModel editor)
    {
        if (editor.IsNew ||
            !_dialogs.Confirm(Strings.Receipts_DeleteConfirm, confirmText: Strings.Common_Delete, isDestructive: true))
        {
            return;
        }

        await _receipts.DeleteAsync(editor.Id);
        Editor = null;
        _notifications.Success(Strings.Common_DeletedNotice);
        await LoadAsync();
    }

    private void RefreshView()
    {
        ReceiptsView.Refresh();
        OnPropertyChanged(nameof(Summary));
    }
}

public sealed partial class ReceiptEditorViewModel : ObservableObject
{
    private readonly Receipt? _original;

    public ReceiptEditorViewModel(Receipt? receipt, IReadOnlyList<Item> items, IReadOnlyList<StorageLocation> locations, bool canEdit)
    {
        _original = receipt;
        Items = items;
        Locations = [new LocationOption(null, Strings.Receipts_NoLocation), .. locations.Select(l => new LocationOption(l.Id, l.Name))];
        CanEdit = canEdit;

        Item = items.FirstOrDefault(i => i.Id == receipt?.ItemId);
        Quantity = receipt is null ? string.Empty : NumberInput.Format(receipt.Quantity);
        Price = receipt is null ? string.Empty : NumberInput.Format(receipt.Price);
        (Date, Time) = receipt is null ? (DateTime.Today, DateTime.Now.ToString("HH:mm")) : DateTimeInput.Split(receipt.Date);
        Location = Locations.FirstOrDefault(l => l.Id == receipt?.StorageLocationId) ?? Locations[0];
        Note = receipt?.Note ?? string.Empty;
        UpdateItemPrice = receipt is null;
        IsDirty = false;
    }

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? DeleteRequested;

    public int Id => _original?.Id ?? 0;

    public bool IsNew => _original is null;

    public bool CanEdit { get; }

    public bool CanDelete => CanEdit && !IsNew;

    public string Heading => IsNew ? Strings.Receipts_NewHeading : Strings.Receipts_EditHeading;

    public IReadOnlyList<Item> Items { get; }

    public IReadOnlyList<LocationOption> Locations { get; }

    public bool IsDirty { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitName), nameof(AmountText))]
    public partial Item? Item { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountText))]
    public partial string Quantity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountText))]
    public partial string Price { get; set; }

    [ObservableProperty]
    public partial DateTime? Date { get; set; }

    [ObservableProperty]
    public partial string Time { get; set; }

    [ObservableProperty]
    public partial LocationOption Location { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial bool UpdateItemPrice { get; set; }

    [ObservableProperty]
    public partial bool AddAnother { get; set; } = true;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public string? UnitName => Item?.Unit?.Name;

    public string AmountText =>
        NumberInput.TryParse(Quantity, out var q) && NumberInput.TryParse(Price, out var p)
            ? string.Format(Strings.Receipts_AmountPreview, q * p)
            : string.Empty;

    partial void OnItemChanged(Item? value)
    {
        // Suggest the current catalog price for a new receipt.
        if (IsNew && value is not null && string.IsNullOrWhiteSpace(Price))
        {
            Price = NumberInput.Format(value.Price);
        }
    }

    /// <summary>Carries date, location and options over to the next receipt of a batch.</summary>
    public void ContinueFrom(ReceiptEditorViewModel previous)
    {
        Date = previous.Date;
        Time = previous.Time;
        Location = Locations.FirstOrDefault(l => l.Id == previous.Location.Id) ?? Locations[0];
        UpdateItemPrice = previous.UpdateItemPrice;
        AddAnother = true;
        IsDirty = false;
    }

    [RelayCommand]
    private void Save() => SaveRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this, EventArgs.Empty);

    public bool TryBuild(out Receipt receipt)
    {
        receipt = null!;
        ErrorMessage = null;

        if (Item is null)
        {
            ErrorMessage = Strings.Receipts_SelectItem;
            return false;
        }

        if (!NumberInput.TryParse(Quantity, out var quantity) || quantity <= 0)
        {
            ErrorMessage = Strings.DomainError_QuantityMustBePositive;
            return false;
        }

        var price = 0m;
        if (!string.IsNullOrWhiteSpace(Price) && (!NumberInput.TryParse(Price, out price) || price < 0))
        {
            ErrorMessage = Strings.Common_InvalidNumber;
            return false;
        }

        if (!DateTimeInput.TryCombine(Date, Time, out var dateUtc))
        {
            ErrorMessage = Strings.Common_InvalidDateTime;
            return false;
        }

        receipt = new Receipt
        {
            Id = Id,
            ConcurrencyStamp = _original?.ConcurrencyStamp ?? Guid.Empty,
            ItemId = Item.Id,
            Quantity = quantity,
            Price = price,
            Date = dateUtc,
            StorageLocationId = Location.Id,
            Note = Note,
        };
        return true;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Item) or nameof(Quantity) or nameof(Price) or nameof(Date) or nameof(Time) or nameof(Location) or nameof(Note))
        {
            IsDirty = true;
        }
    }
}

public sealed record LocationOption(int? Id, string Name);

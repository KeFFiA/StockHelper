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

/// <summary>Issuing goods (a can of paint to a painter) and taking back what is left.</summary>
public sealed partial class IssuesViewModel : PageViewModel
{
    private readonly IIssueRepository _issues;
    private readonly IItemRepository _items;
    private readonly ILookupRepository<StorageLocation> _locations;
    private readonly ILookupRepository<Unit> _units;
    private readonly IAnalyticsService _analytics;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IExportService _export;
    private readonly ICurrentUserService _currentUser;

    public IssuesViewModel(
        IIssueRepository issues,
        IItemRepository items,
        ILookupRepository<StorageLocation> locations,
        ILookupRepository<Unit> units,
        IAnalyticsService analytics,
        IDialogService dialogs,
        INotificationService notifications,
        IExportService export,
        ICurrentUserService currentUser)
    {
        _issues = issues;
        _items = items;
        _locations = locations;
        _units = units;
        _analytics = analytics;
        _dialogs = dialogs;
        _notifications = notifications;
        _export = export;
        _currentUser = currentUser;

        IssuesView = CollectionViewSource.GetDefaultView(Issues);
        IssuesView.Filter = o => o is Issue i && TextSearch.Matches(SearchText, i.Item?.Name, i.Item?.Code, i.IssuedTo, i.Note);
        FromDate = DateTime.Today.AddDays(-29);
        ToDate = DateTime.Today;
    }

    public override string Title => Strings.Nav_Issues;

    public override System.Windows.Input.ICommand? NewShortcut => AddCommand;

    public override System.Windows.Input.ICommand? RefreshShortcut => LoadCommand;

    public override System.Windows.Input.ICommand? ExportShortcut => ExportCommand;

    public bool CanEdit => _currentUser.Has(Permission.ManageIssues);

    public ObservableCollection<Issue> Issues { get; } = [];

    public ICollectionView IssuesView { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeriodVisible))]
    public partial bool OnlyOnHand { get; set; }

    public bool IsPeriodVisible => !OnlyOnHand;

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Issue? SelectedIssue { get; set; }

    [ObservableProperty]
    public partial IssueEditorViewModel? Editor { get; set; }

    [ObservableProperty]
    public partial int OnHandCount { get; private set; }

    public string Summary
    {
        get
        {
            var visible = IssuesView.Cast<Issue>().ToList();
            return string.Format(Strings.Issues_Summary, visible.Count, OnHandCount);
        }
    }

    public override async Task OnNavigatedToAsync()
    {
        await LoadAsync();
    }

    public override Task<bool> OnNavigatingFromAsync() => Task.FromResult(ConfirmLeave());

    partial void OnOnlyOnHandChanged(bool value) => _ = LoadAsync();

    partial void OnFromDateChanged(DateTime? value) => _ = LoadAsync();

    partial void OnToDateChanged(DateTime? value) => _ = LoadAsync();

    partial void OnSearchTextChanged(string value) => RefreshView();

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var selectedId = SelectedIssue?.Id;
        var filter = OnlyOnHand
            ? new IssueFilter(OnlyOnHand: true)
            : new IssueFilter(FromDate?.Date.ToUniversalTime(), ToDate?.Date.AddDays(1).AddTicks(-1).ToUniversalTime());

        var list = await _issues.GetAsync(filter);
        Issues.Clear();
        foreach (var issue in list)
        {
            Issues.Add(issue);
        }

        OnHandCount = OnlyOnHand ? list.Count : (await _issues.GetAsync(new IssueFilter(OnlyOnHand: true))).Count;
        SelectedIssue = Issues.FirstOrDefault(i => i.Id == selectedId);
        RefreshView();
    });

    [RelayCommand]
    private void SetPeriod(string? days)
    {
        OnlyOnHand = false;
        FromDate = int.TryParse(days, out var n) && n > 0 ? DateTime.Today.AddDays(-n + 1) : null;
        ToDate = DateTime.Today;
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
        if (SelectedIssue is not null && ConfirmLeave())
        {
            Editor = await CreateEditorAsync(SelectedIssue);
        }
    }

    /// <summary>Row action: open the issue with the return form focused.</summary>
    [RelayCommand]
    private async Task ReturnAsync(Issue? issue)
    {
        if (issue is null || !ConfirmLeave())
        {
            return;
        }

        SelectedIssue = issue;
        Editor = await CreateEditorAsync(issue);
        Editor.IsReturnMode = true;
    }

    [RelayCommand]
    private Task ExportAsync() => _export.ExportAsync(
        Strings.Nav_Issues,
        IssuesView.Cast<Issue>().ToList(),
        [
            new ExportColumn<Issue>(Strings.Receipts_Date, i => i.Date, ExportFormat.DateTime),
            new ExportColumn<Issue>(Strings.Items_Name, i => i.Item?.Name),
            new ExportColumn<Issue>(Strings.Issues_Issued, i => i.Quantity, ExportFormat.Quantity),
            new ExportColumn<Issue>(Strings.Items_Unit, i => i.Item?.Unit?.Name),
            new ExportColumn<Issue>(Strings.Issues_EnteredAs, i => Formatting.Entered(i)),
            new ExportColumn<Issue>(Strings.Issues_To, i => i.IssuedTo),
            new ExportColumn<Issue>(Strings.Issues_Returned, i => i.ReturnedQuantity, ExportFormat.Quantity),
            new ExportColumn<Issue>(Strings.Issues_ReturnedAt, i => i.ReturnedAt, ExportFormat.DateTime),
            new ExportColumn<Issue>(Strings.Issues_Net, i => i.NetQuantity, ExportFormat.Quantity),
            new ExportColumn<Issue>(Strings.Items_Note, i => i.Note),
            new ExportColumn<Issue>(Strings.Common_CreatedBy, i => i.CreatedBy),
        ]);

    private bool ConfirmLeave() =>
        Editor is not { IsDirty: true } || _dialogs.Confirm(Strings.Common_DiscardChanges, confirmText: Strings.Common_Discard);

    private async Task<IssueEditorViewModel> CreateEditorAsync(Issue? issue)
    {
        var items = (await _items.GetAllAsync(includeArchived: true)).Where(i => !i.IsArchived || i.Id == issue?.ItemId).ToList();
        var locations = (await _locations.GetAllAsync(includeArchived: true)).Where(l => !l.IsArchived || l.Id == issue?.StorageLocationId).ToList();
        var units = await _units.GetAllAsync(includeArchived: true);
        var recipients = await _issues.GetRecipientsAsync();
        var stock = (await _analytics.LoadAsync()).Statuses.ToDictionary(s => s.Item.Id, s => s.EstimatedStock);

        var editor = new IssueEditorViewModel(issue, items, locations, units, recipients, stock, CanEdit);
        editor.SaveRequested += async (_, _) => await SaveAsync(editor);
        editor.CancelRequested += (_, _) => Editor = null;
        editor.DeleteRequested += async (_, _) => await DeleteAsync(editor);
        editor.ReturnRequested += async (_, _) => await RegisterReturnAsync(editor);
        editor.CancelReturnRequested += async (_, _) => await CancelReturnAsync(editor);
        return editor;
    }

    private async Task SaveAsync(IssueEditorViewModel editor)
    {
        if (!editor.TryBuild(out var issue))
        {
            return;
        }

        if (editor.ExceedsStock && !_dialogs.Confirm(editor.StockWarning!, confirmText: Strings.Issues_IssueAnyway))
        {
            return;
        }

        try
        {
            var saved = issue.Id == 0 ? await _issues.AddAsync(issue) : await _issues.UpdateAsync(issue);
            var keepAdding = editor.IsNew && editor.AddAnother;
            Editor = null;
            _notifications.Success(editor.IsNew ? Strings.Issues_Created : Strings.Common_Saved,
                $"{editor.Item?.Name} · {NumberInput.Format(issue.Quantity)} {editor.Item?.Unit?.Name}");
            await LoadAsync();
            SelectedIssue = Issues.FirstOrDefault(i => i.Id == saved.Id);

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

    private async Task RegisterReturnAsync(IssueEditorViewModel editor)
    {
        if (!editor.TryGetReturn(out var quantity, out var returnedAt))
        {
            return;
        }

        try
        {
            await _issues.ReturnAsync(editor.Id, quantity, returnedAt);
            Editor = null;
            _notifications.Success(Strings.Issues_ReturnedNotice,
                $"{editor.Item?.Name} · {NumberInput.Format(quantity)} {editor.Item?.Unit?.Name}");
            await LoadAsync();
        }
        catch (DomainException ex)
        {
            editor.ReturnError = ErrorMessages.For(ex);
        }
    }

    private async Task CancelReturnAsync(IssueEditorViewModel editor)
    {
        if (!_dialogs.Confirm(Strings.Issues_CancelReturnConfirm, confirmText: Strings.Issues_CancelReturn))
        {
            return;
        }

        await _issues.CancelReturnAsync(editor.Id);
        Editor = null;
        await LoadAsync();
    }

    private async Task DeleteAsync(IssueEditorViewModel editor)
    {
        if (editor.IsNew ||
            !_dialogs.Confirm(Strings.Issues_DeleteConfirm, confirmText: Strings.Common_Delete, isDestructive: true))
        {
            return;
        }

        await _issues.DeleteAsync(editor.Id);
        Editor = null;
        _notifications.Success(Strings.Common_DeletedNotice);
        await LoadAsync();
    }

    private void RefreshView()
    {
        IssuesView.Refresh();
        OnPropertyChanged(nameof(Summary));
    }
}

public sealed partial class IssueEditorViewModel : ObservableObject
{
    private readonly Issue? _original;
    private readonly IReadOnlyDictionary<int, decimal> _stock;

    public IssueEditorViewModel(
        Issue? issue,
        IReadOnlyList<Item> items,
        IReadOnlyList<StorageLocation> locations,
        IReadOnlyList<Unit> units,
        IReadOnlyList<string> recipients,
        IReadOnlyDictionary<int, decimal> stock,
        bool canEdit)
    {
        _original = issue;
        _stock = stock;
        Items = items;
        Recipients = recipients;
        Locations = [new LocationOption(null, Strings.Receipts_NoLocation), .. locations.Select(l => new LocationOption(l.Id, l.Name))];
        CanEdit = canEdit;

        Quantity = new QuantityInput(units);
        Quantity.Changed += (_, _) => OnQuantityChanged();
        ReturnQuantity = new QuantityInput(units, Strings.Issues_ReturnQuantity);

        Item = items.FirstOrDefault(i => i.Id == issue?.ItemId);
        Quantity.SetItem(Item, issue?.UnitId, issue?.UnitQuantity, issue?.Quantity);
        ReturnQuantity.SetItem(Item);
        (Date, Time) = issue is null ? (DateTime.Today, DateTime.Now.ToString("HH:mm")) : DateTimeInput.Split(issue.Date);
        (ReturnDate, ReturnTime) = (DateTime.Today, DateTime.Now.ToString("HH:mm"));
        IssuedTo = issue?.IssuedTo ?? string.Empty;
        Location = Locations.FirstOrDefault(l => l.Id == issue?.StorageLocationId) ?? Locations[0];
        Note = issue?.Note ?? string.Empty;
        ExpectReturn = issue?.ExpectReturn ?? false;
        IsDirty = false;
    }

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? ReturnRequested;

    public event EventHandler? CancelReturnRequested;

    public int Id => _original?.Id ?? 0;

    public bool IsNew => _original is null;

    public bool CanEdit { get; }

    public bool CanDelete => CanEdit && !IsNew;

    public string Heading => IsReturnMode ? Strings.Issues_ReturnTitle : IsNew ? Strings.Issues_NewHeading : Strings.Issues_EditHeading;

    public IReadOnlyList<Item> Items { get; }

    public IReadOnlyList<string> Recipients { get; }

    public IReadOnlyList<LocationOption> Locations { get; }

    public QuantityInput Quantity { get; }

    public QuantityInput ReturnQuantity { get; }

    public bool IsDirty { get; private set; }

    /// <summary>The return block is shown for saved issues that were not returned yet.</summary>
    public bool CanReturn => CanEdit && _original is { IsReturned: false };

    public bool IsReturned => _original?.IsReturned == true;

    public string? ReturnedText => _original is { IsReturned: true } issue
        ? string.Format(Strings.Issues_ReturnedText,
            NumberInput.Format(issue.ReturnedQuantity ?? 0), issue.Item?.Unit?.Name,
            issue.ReturnedAt!.Value.ToLocalTime(), NumberInput.Format(issue.NetQuantity))
        : null;

    /// <summary>Opened via "Вернуть": only the issue summary and the return form are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(IsEditMode))]
    public partial bool IsReturnMode { get; set; }

    public bool IsEditMode => !IsReturnMode;

    public string Summary => _original is null
        ? string.Empty
        : string.Format(Strings.Issues_ReturnSummary, _original.Item?.Name, NumberInput.Format(_original.Quantity), _original.Item?.Unit?.Name,
            Formatting.Entered(_original) is { } entered ? $" ({entered})" : string.Empty,
            string.IsNullOrWhiteSpace(_original.IssuedTo) ? Strings.Issues_NoRecipient : _original.IssuedTo,
            _original.Date.ToLocalTime());

    [RelayCommand]
    private void ShowDetails() => IsReturnMode = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StockText), nameof(StockWarning), nameof(ExceedsStock))]
    public partial Item? Item { get; set; }

    [ObservableProperty]
    public partial DateTime? Date { get; set; }

    [ObservableProperty]
    public partial string Time { get; set; }

    [ObservableProperty]
    public partial string IssuedTo { get; set; }

    [ObservableProperty]
    public partial LocationOption Location { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial bool ExpectReturn { get; set; }

    [ObservableProperty]
    public partial bool AddAnother { get; set; } = true;

    [ObservableProperty]
    public partial DateTime? ReturnDate { get; set; }

    [ObservableProperty]
    public partial string ReturnTime { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? ReturnError { get; set; }

    /// <summary>"На складе ≈ 12 л" — estimated stock of the selected item.</summary>
    public string? StockText => Item is not null && _stock.TryGetValue(Item.Id, out var stock)
        ? string.Format(Strings.Issues_InStock, NumberInput.Format(stock), Item.Unit?.Name)
        : null;

    public bool ExceedsStock =>
        IsNew && Item is not null && _stock.TryGetValue(Item.Id, out var stock) && Quantity.TryGetItemQuantity(out var q) && q > stock;

    public string? StockWarning => ExceedsStock ? string.Format(Strings.Issues_ExceedsStock, StockText) : null;

    partial void OnItemChanged(Item? value)
    {
        Quantity.SetItem(value);
        ReturnQuantity.SetItem(value);
    }

    private void OnQuantityChanged()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(ExceedsStock));
        OnPropertyChanged(nameof(StockWarning));
    }

    public void ContinueFrom(IssueEditorViewModel previous)
    {
        Date = previous.Date;
        Time = previous.Time;
        IssuedTo = previous.IssuedTo;
        Location = Locations.FirstOrDefault(l => l.Id == previous.Location.Id) ?? Locations[0];
        ExpectReturn = previous.ExpectReturn;
        AddAnother = true;
        IsDirty = false;
    }

    [RelayCommand]
    private void Save() => SaveRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Return() => ReturnRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CancelReturn() => CancelReturnRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Nothing came back (everything was used up).</summary>
    [RelayCommand]
    private void ReturnNothing() => ReturnQuantity.Text = "0";

    public bool TryBuild(out Issue issue)
    {
        issue = null!;
        ErrorMessage = null;

        if (Item is null)
        {
            ErrorMessage = Strings.Receipts_SelectItem;
            return false;
        }

        if (!Quantity.TryGetItemQuantity(out var quantity) || quantity <= 0)
        {
            ErrorMessage = Strings.DomainError_QuantityMustBePositive;
            return false;
        }

        if (!DateTimeInput.TryCombine(Date, Time, out var dateUtc))
        {
            ErrorMessage = Strings.Common_InvalidDateTime;
            return false;
        }

        issue = new Issue
        {
            Id = Id,
            ConcurrencyStamp = _original?.ConcurrencyStamp ?? Guid.Empty,
            ItemId = Item.Id,
            Quantity = quantity,
            UnitId = Quantity.EnteredUnitId,
            UnitQuantity = Quantity.EnteredQuantity,
            Date = dateUtc,
            IssuedTo = IssuedTo,
            StorageLocationId = Location.Id,
            Note = Note,
            ExpectReturn = ExpectReturn,
            ReturnedQuantity = _original?.ReturnedQuantity,
        };
        return true;
    }

    public bool TryGetReturn(out decimal quantity, out DateTime returnedAtUtc)
    {
        ReturnError = null;
        returnedAtUtc = default;
        if (!ReturnQuantity.TryGetItemQuantity(out quantity) || quantity < 0)
        {
            ReturnError = Strings.Common_InvalidNumber;
            return false;
        }

        if (_original is not null && quantity > _original.Quantity)
        {
            ReturnError = Strings.DomainError_ReturnExceedsIssued;
            return false;
        }

        if (!DateTimeInput.TryCombine(ReturnDate, ReturnTime, out returnedAtUtc))
        {
            ReturnError = Strings.Common_InvalidDateTime;
            return false;
        }

        return true;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Item) or nameof(Date) or nameof(Time) or nameof(IssuedTo) or nameof(Location) or nameof(Note) or nameof(ExpectReturn))
        {
            IsDirty = true;
        }
    }
}

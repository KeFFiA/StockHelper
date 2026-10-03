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

/// <summary>Reference data: categories, units and storage locations, shown as segments of one page.</summary>
public sealed partial class CatalogsViewModel : PageViewModel
{
    public CatalogsViewModel(
        ILookupRepository<Category> categories,
        ILookupRepository<Unit> units,
        ILookupRepository<StorageLocation> locations,
        IDialogService dialogs,
        INotificationService notifications,
        ICurrentUserService currentUser)
    {
        Sections =
        [
            new LookupSectionViewModel<Category>(categories, dialogs, notifications,
                Strings.Catalogs_Categories, Strings.Catalogs_CategoryNew, "Icon.Tag",
                currentUser.Has(Permission.ManageCatalog), hasDescription: false, Strings.Catalogs_CategoryHint),
            new LookupSectionViewModel<Unit>(units, dialogs, notifications,
                Strings.Catalogs_Units, Strings.Catalogs_UnitNew, "Icon.Ruler",
                currentUser.Has(Permission.ManageCatalog), hasDescription: false, Strings.Catalogs_UnitHint),
            new LookupSectionViewModel<StorageLocation>(locations, dialogs, notifications,
                Strings.Catalogs_Locations, Strings.Catalogs_LocationNew, "Icon.Location",
                currentUser.Has(Permission.ManageLocations), hasDescription: true, Strings.Catalogs_LocationHint),
        ];
        SelectedSection = Sections[0];
    }

    public override string Title => Strings.Nav_Catalogs;

    public IReadOnlyList<LookupSectionViewModel> Sections { get; }

    [ObservableProperty]
    public partial LookupSectionViewModel SelectedSection { get; set; }

    public override Task OnNavigatedToAsync() => SelectedSection.LoadAsync();

    public override Task<bool> OnNavigatingFromAsync() => Task.FromResult(SelectedSection.ConfirmLeave());

    partial void OnSelectedSectionChanged(LookupSectionViewModel value) => _ = value.LoadAsync();
}

public sealed class LookupRow(LookupEntity entity)
{
    public LookupEntity Entity { get; } = entity;

    public int Id => Entity.Id;

    public string Name => Entity.Name;

    public string? Description => (Entity as StorageLocation)?.Description;

    public bool IsArchived => Entity.IsArchived;
}

/// <summary>Non-generic base so a single DataTemplate can render every section.</summary>
public abstract partial class LookupSectionViewModel : ViewModelBase
{
    protected LookupSectionViewModel(string title, string newTitle, string iconKey, bool canEdit, bool hasDescription, string hint)
    {
        Title = title;
        NewTitle = newTitle;
        IconKey = iconKey;
        CanEdit = canEdit;
        HasDescription = hasDescription;
        Hint = hint;
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is LookupRow row && (ShowArchived || !row.IsArchived) && TextSearch.Matches(SearchText, row.Name, row.Description);
    }

    public string Title { get; }

    public string NewTitle { get; }

    public string IconKey { get; }

    public string Hint { get; }

    public bool CanEdit { get; }

    public bool HasDescription { get; }

    public ObservableCollection<LookupRow> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowArchived { get; set; }

    [ObservableProperty]
    public partial LookupRow? SelectedRow { get; set; }

    [ObservableProperty]
    public partial LookupEditorViewModel? Editor { get; set; }

    partial void OnSearchTextChanged(string value) => RowsView.Refresh();

    partial void OnShowArchivedChanged(bool value) => RowsView.Refresh();

    public abstract Task LoadAsync();

    public abstract bool ConfirmLeave();
}

public sealed partial class LookupSectionViewModel<T> : LookupSectionViewModel where T : LookupEntity, new()
{
    private readonly ILookupRepository<T> _repository;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public LookupSectionViewModel(
        ILookupRepository<T> repository,
        IDialogService dialogs,
        INotificationService notifications,
        string title,
        string newTitle,
        string iconKey,
        bool canEdit,
        bool hasDescription,
        string hint)
        : base(title, newTitle, iconKey, canEdit, hasDescription, hint)
    {
        _repository = repository;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    [RelayCommand]
    public override Task LoadAsync() => RunBusyAsync(async () =>
    {
        var selectedId = SelectedRow?.Id;
        var entities = await _repository.GetAllAsync(includeArchived: true);
        Rows.Clear();
        foreach (var entity in entities)
        {
            Rows.Add(new LookupRow(entity));
        }

        SelectedRow = Rows.FirstOrDefault(r => r.Id == selectedId);
    });

    public override bool ConfirmLeave() =>
        Editor is not { IsDirty: true } || _dialogs.Confirm(Strings.Common_DiscardChanges, confirmText: Strings.Common_Discard);

    [RelayCommand]
    private void Add()
    {
        if (CanEdit && ConfirmLeave())
        {
            Editor = CreateEditor(null);
        }
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedRow is not null && ConfirmLeave())
        {
            Editor = CreateEditor((T)SelectedRow.Entity);
        }
    }

    private LookupEditorViewModel CreateEditor(T? entity)
    {
        var editor = new LookupEditorViewModel(entity, entity is null ? NewTitle : Strings.Common_EditHeading, CanEdit, HasDescription);
        editor.SaveRequested += async (_, _) => await SaveAsync(editor, entity);
        editor.CancelRequested += (_, _) => Editor = null;
        editor.ArchiveRequested += async (_, _) => await ToggleArchiveAsync(editor);
        editor.DeleteRequested += async (_, _) => await DeleteAsync(editor);
        return editor;
    }

    private async Task SaveAsync(LookupEditorViewModel editor, T? original)
    {
        if (string.IsNullOrWhiteSpace(editor.Name))
        {
            editor.ErrorMessage = Strings.DomainError_NameRequired;
            return;
        }

        var entity = new T
        {
            Id = original?.Id ?? 0,
            ConcurrencyStamp = original?.ConcurrencyStamp ?? Guid.Empty,
            Name = editor.Name,
            IsArchived = original?.IsArchived ?? false,
        };
        if (entity is StorageLocation location)
        {
            location.Description = editor.Description;
        }

        try
        {
            var saved = entity.Id == 0 ? await _repository.AddAsync(entity) : await _repository.UpdateAsync(entity);
            Editor = null;
            _notifications.Success(Strings.Common_Saved, saved.Name);
            await LoadAsync();
            SelectedRow = Rows.FirstOrDefault(r => r.Id == saved.Id);
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

    private async Task ToggleArchiveAsync(LookupEditorViewModel editor)
    {
        var archive = !editor.IsArchived;
        await _repository.SetArchivedAsync(editor.Id, archive);
        Editor = null;
        _notifications.Success(archive ? Strings.Common_ArchivedNotice : Strings.Common_RestoredNotice, editor.Name);
        await LoadAsync();
    }

    private async Task DeleteAsync(LookupEditorViewModel editor)
    {
        if (await _repository.IsInUseAsync(editor.Id))
        {
            _dialogs.ShowInfo(Strings.Catalogs_DeleteInUse);
            return;
        }

        if (!_dialogs.Confirm(string.Format(Strings.Catalogs_DeleteConfirm, editor.Name), confirmText: Strings.Common_Delete, isDestructive: true))
        {
            return;
        }

        await _repository.DeleteAsync(editor.Id);
        Editor = null;
        _notifications.Success(Strings.Common_DeletedNotice, editor.Name);
        await LoadAsync();
    }
}

public sealed partial class LookupEditorViewModel : ObservableObject
{
    private readonly LookupEntity? _original;

    public LookupEditorViewModel(LookupEntity? entity, string heading, bool canEdit, bool hasDescription)
    {
        _original = entity;
        Heading = heading;
        CanEdit = canEdit;
        HasDescription = hasDescription;
        Name = entity?.Name ?? string.Empty;
        Description = (entity as StorageLocation)?.Description ?? string.Empty;
        IsDirty = false;
    }

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? ArchiveRequested;

    public event EventHandler? DeleteRequested;

    public int Id => _original?.Id ?? 0;

    public bool IsNew => _original is null;

    public bool IsArchived => _original?.IsArchived ?? false;

    public string Heading { get; }

    public bool CanEdit { get; }

    public bool HasDescription { get; }

    public string ArchiveText => IsArchived ? Strings.Common_Restore : Strings.Common_Archive;

    public bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

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

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(Description))
        {
            IsDirty = true;
        }
    }
}

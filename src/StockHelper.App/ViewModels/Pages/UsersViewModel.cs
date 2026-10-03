using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Entities;
using StockHelper.Core.Errors;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels.Pages;

public sealed partial class UsersViewModel(
    IUserRepository users,
    IAuthService auth,
    IDialogService dialogs,
    INotificationService notifications) : PageViewModel
{
    public override string Title => Strings.Nav_Users;

    public override System.Windows.Input.ICommand? NewShortcut => AddCommand;

    public override System.Windows.Input.ICommand? RefreshShortcut => LoadCommand;

    public ObservableCollection<User> Users { get; } = [];

    [ObservableProperty]
    public partial User? SelectedUser { get; set; }

    [ObservableProperty]
    public partial UserEditorViewModel? Editor { get; set; }

    public override Task OnNavigatedToAsync() => LoadAsync();

    public override Task<bool> OnNavigatingFromAsync() => Task.FromResult(ConfirmLeave());

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var selectedId = SelectedUser?.Id;
        Users.Clear();
        foreach (var user in await users.GetAllAsync())
        {
            Users.Add(user);
        }

        SelectedUser = Users.FirstOrDefault(u => u.Id == selectedId);
    });

    [RelayCommand]
    private void Add()
    {
        if (ConfirmLeave())
        {
            Editor = CreateEditor(null);
        }
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedUser is not null && ConfirmLeave())
        {
            Editor = CreateEditor(SelectedUser);
        }
    }

    private bool ConfirmLeave() =>
        Editor is not { IsDirty: true } || dialogs.Confirm(Strings.Common_DiscardChanges, confirmText: Strings.Common_Discard);

    private UserEditorViewModel CreateEditor(User? user)
    {
        var editor = new UserEditorViewModel(user);
        editor.SaveRequested += async (_, _) => await SaveAsync(editor, user);
        editor.CancelRequested += (_, _) => Editor = null;
        return editor;
    }

    private async Task SaveAsync(UserEditorViewModel editor, User? original)
    {
        editor.ErrorMessage = null;
        if (editor.Password != editor.PasswordConfirm)
        {
            editor.ErrorMessage = Strings.Auth_PasswordsDoNotMatch;
            return;
        }

        try
        {
            User saved;
            if (original is null)
            {
                saved = await auth.CreateUserAsync(editor.Login, editor.DisplayName, editor.Role, editor.Password);
            }
            else
            {
                var user = new User
                {
                    Id = original.Id,
                    ConcurrencyStamp = original.ConcurrencyStamp,
                    Login = editor.Login,
                    DisplayName = editor.DisplayName,
                    Role = editor.Role,
                    IsActive = editor.IsActive,
                    PasswordHash = original.PasswordHash,
                };
                saved = await auth.UpdateUserAsync(user);
                if (!string.IsNullOrEmpty(editor.Password))
                {
                    await auth.SetPasswordAsync(saved, editor.Password);
                }
            }

            Editor = null;
            notifications.Success(Strings.Common_Saved, saved.DisplayName);
            await LoadAsync();
            SelectedUser = Users.FirstOrDefault(u => u.Id == saved.Id);
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
}

public sealed partial class UserEditorViewModel : ObservableObject
{
    public UserEditorViewModel(User? user)
    {
        IsNew = user is null;
        Login = user?.Login ?? string.Empty;
        DisplayName = user?.DisplayName ?? string.Empty;
        Role = user?.Role ?? UserRole.Storekeeper;
        IsActive = user?.IsActive ?? true;
        IsDirty = false;
    }

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public bool IsNew { get; }

    public string Heading => IsNew ? Strings.Users_NewHeading : Strings.Common_EditHeading;

    public string PasswordLabel => IsNew ? Strings.Auth_Password : Strings.Users_NewPassword;

    public IReadOnlyList<UserRole> Roles { get; } = [UserRole.Storekeeper, UserRole.Manager, UserRole.Administrator];

    public bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial string Login { get; set; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleDescription))]
    public partial UserRole Role { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PasswordConfirm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public string RoleDescription =>
        Strings.ResourceManager.GetString($"UserRole_{Role}_Description", Strings.Culture) ?? string.Empty;

    [RelayCommand]
    private void Save() => SaveRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Login) or nameof(DisplayName) or nameof(Role) or nameof(IsActive) or nameof(Password))
        {
            IsDirty = true;
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Errors;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels;

/// <summary>First-run administrator setup or regular sign-in.</summary>
public sealed partial class AuthViewModel(IAuthService auth, ISettingsService settings) : ViewModelBase
{
    public event EventHandler? Succeeded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(Subheading), nameof(SubmitText))]
    public partial bool IsSetup { get; set; }

    [ObservableProperty]
    public partial string Login { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PasswordConfirm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public string Heading => IsSetup ? Strings.Auth_SetupHeading : Strings.Auth_SignInHeading;

    public string Subheading => IsSetup ? Strings.Auth_SetupSubheading : Strings.Auth_SignInSubheading;

    public string SubmitText => IsSetup ? Strings.Auth_SetupSubmit : Strings.Auth_SignInSubmit;

    public string Version => AppInfo.Version;

    public async Task InitializeAsync()
    {
        IsSetup = await auth.IsSetupRequiredAsync();
        Login = IsSetup ? string.Empty : settings.Current.LastLogin ?? string.Empty;
        Password = string.Empty;
        PasswordConfirm = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Login) || (IsSetup && string.IsNullOrWhiteSpace(DisplayName)))
        {
            ErrorMessage = Strings.Auth_FillAllFields;
            return;
        }

        if (IsSetup && Password != PasswordConfirm)
        {
            ErrorMessage = Strings.Auth_PasswordsDoNotMatch;
            return;
        }

        try
        {
            IsBusy = true;
            if (IsSetup)
            {
                await auth.CreateFirstAdministratorAsync(Login, DisplayName, Password);
            }
            else
            {
                await auth.SignInAsync(Login, Password);
            }

            await settings.SaveAsync(settings.Current with { LastLogin = Login.Trim() });
            Password = string.Empty;
            PasswordConfirm = string.Empty;
            Succeeded?.Invoke(this, EventArgs.Empty);
        }
        catch (DomainException ex)
        {
            ErrorMessage = ErrorMessages.For(ex);
            Password = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

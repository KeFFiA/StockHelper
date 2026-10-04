using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Errors;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels;

public enum AuthMode
{
    SignIn,

    /// <summary>First run: create the first administrator.</summary>
    Setup,

    /// <summary>"Forgot password": new administrator password by the recovery code.</summary>
    Recover,

    /// <summary>Started with <c>--reset-admin</c> on the computer with the database.</summary>
    LocalReset,
}

/// <summary>Sign-in, first-run setup and the two ways to regain access (recovery code, local reset).</summary>
public sealed partial class AuthViewModel(IAuthService auth, IAccountRecoveryService recovery, ISettingsService settings) : ViewModelBase
{
    public event EventHandler? Succeeded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(Subheading), nameof(SubmitText), nameof(LoginLabel), nameof(PasswordLabel),
        nameof(ShowDisplayName), nameof(ShowConfirm), nameof(ShowCode), nameof(CanForget), nameof(CanGoBack))]
    public partial AuthMode Mode { get; set; }

    [ObservableProperty]
    public partial string Login { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PasswordConfirm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RecoveryCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>A recovery code issued by the last action: shown once before the window closes.</summary>
    public string? IssuedCode { get; private set; }

    public string Heading => Mode switch
    {
        AuthMode.Setup => Strings.Auth_SetupHeading,
        AuthMode.Recover => Strings.Auth_RecoverHeading,
        AuthMode.LocalReset => Strings.Auth_ResetHeading,
        _ => Strings.Auth_SignInHeading,
    };

    public string Subheading => Mode switch
    {
        AuthMode.Setup => Strings.Auth_SetupSubheading,
        AuthMode.Recover => Strings.Auth_RecoverSubheading,
        AuthMode.LocalReset => Strings.Auth_ResetSubheading,
        _ => Strings.Auth_SignInSubheading,
    };

    public string SubmitText => Mode switch
    {
        AuthMode.Setup => Strings.Auth_SetupSubmit,
        AuthMode.Recover => Strings.Auth_RecoverSubmit,
        AuthMode.LocalReset => Strings.Auth_ResetSubmit,
        _ => Strings.Auth_SignInSubmit,
    };

    public string LoginLabel => Mode is AuthMode.Recover or AuthMode.LocalReset ? Strings.Auth_AdminLogin : Strings.Auth_Login;

    public string PasswordLabel => Mode is AuthMode.Recover or AuthMode.LocalReset ? Strings.Auth_NewPassword : Strings.Auth_Password;

    public bool ShowDisplayName => Mode is AuthMode.Setup or AuthMode.LocalReset;

    public bool ShowConfirm => Mode != AuthMode.SignIn;

    public bool ShowCode => Mode == AuthMode.Recover;

    public bool CanForget => Mode == AuthMode.SignIn && !AppInfo.IsDemo;

    public bool CanGoBack => Mode == AuthMode.Recover;

    public string Version => AppInfo.Version;

    public bool IsDemo => AppInfo.IsDemo;

    public IReadOnlyList<DemoAccount> DemoAccounts { get; } = AppInfo.IsDemo
        ? DemoData.Accounts.Select(a => new DemoAccount(a.Login, a.DisplayName, Converters.EnumDisplay.Get(a.Role))).ToList()
        : [];

    [RelayCommand]
    private Task SignInAsDemoAsync(DemoAccount account)
    {
        Login = account.Login;
        Password = AppInfo.DemoPassword;
        return SubmitAsync();
    }

    /// <param name="localReset">The app was started with <c>--reset-admin</c>.</param>
    public async Task InitializeAsync(bool localReset = false)
    {
        Mode = localReset ? AuthMode.LocalReset : await auth.IsSetupRequiredAsync() ? AuthMode.Setup : AuthMode.SignIn;
        Login = Mode == AuthMode.SignIn ? settings.Current.LastLogin ?? string.Empty : string.Empty;
        DisplayName = string.Empty;
        ClearSecrets();
        ErrorMessage = null;
        IssuedCode = null;
    }

    [RelayCommand]
    private void ForgotPassword()
    {
        Mode = AuthMode.Recover;
        ClearSecrets();
        ErrorMessage = null;
    }

    [RelayCommand]
    private void BackToSignIn()
    {
        Mode = AuthMode.SignIn;
        ClearSecrets();
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Login) || (ShowDisplayName && string.IsNullOrWhiteSpace(DisplayName)) ||
            (ShowCode && string.IsNullOrWhiteSpace(RecoveryCode)))
        {
            ErrorMessage = Strings.Auth_FillAllFields;
            return;
        }

        if (ShowConfirm && Password != PasswordConfirm)
        {
            ErrorMessage = Strings.Auth_PasswordsDoNotMatch;
            return;
        }

        try
        {
            IsBusy = true;
            switch (Mode)
            {
                case AuthMode.Setup:
                    await auth.CreateFirstAdministratorAsync(Login, DisplayName, Password);
                    IssuedCode = await recovery.IssueCodeAsync();
                    break;
                case AuthMode.Recover:
                    IssuedCode = (await recovery.RecoverAsync(RecoveryCode, Login, Password)).NewCode;
                    break;
                case AuthMode.LocalReset:
                    IssuedCode = (await recovery.ResetLocallyAsync(Login, DisplayName, Password)).NewCode;
                    break;
                default:
                    await auth.SignInAsync(Login, Password);
                    break;
            }

            await settings.SaveAsync(settings.Current with { LastLogin = Login.Trim() });
            ClearSecrets();
            Succeeded?.Invoke(this, EventArgs.Empty);
        }
        catch (DomainException ex)
        {
            ErrorMessage = ErrorMessages.For(ex);
            Password = string.Empty;
            PasswordConfirm = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSecrets()
    {
        Password = string.Empty;
        PasswordConfirm = string.Empty;
        RecoveryCode = string.Empty;
    }
}

public sealed record DemoAccount(string Login, string DisplayName, string Role);

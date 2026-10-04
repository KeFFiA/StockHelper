using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Errors;
using StockHelper.Core.Security;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels.Pages;

public sealed record ThemeOption(AppTheme Theme, string Title, string IconKey);

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IBackupService _backups;
    private readonly IAuthService _auth;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _currentUser;
    private readonly IChangelogService _changelog;
    private readonly IAccountRecoveryService _recovery;

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        IBackupService backups,
        IAuthService auth,
        IDialogService dialogs,
        INotificationService notifications,
        ICurrentUserService currentUser,
        IUpdateService updates,
        IChangelogService changelog,
        IAccountRecoveryService recovery)
    {
        _changelog = changelog;
        _recovery = recovery;
        _settings = settings;
        _theme = theme;
        _backups = backups;
        _auth = auth;
        _dialogs = dialogs;
        _notifications = notifications;
        _currentUser = currentUser;
        Updates = updates;

        Themes =
        [
            new ThemeOption(AppTheme.System, Strings.Settings_ThemeSystem, "Icon.Settings"),
            new ThemeOption(AppTheme.Light, Strings.Settings_ThemeLight, "Icon.Light"),
            new ThemeOption(AppTheme.Dark, Strings.Settings_ThemeDark, "Icon.Dark"),
        ];
        SelectedTheme = Themes.First(t => t.Theme == settings.Current.Theme);
        ResetForecastFields();
    }

    public override string Title => Strings.Nav_Settings;

    public IUpdateService Updates { get; }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public bool CanManageSettings => _currentUser.Has(Permission.ManageSettings);

    public bool CanManageBackups => _currentUser.Has(Permission.ManageBackups);

    public bool CanManageRecovery => _currentUser.Has(Permission.ManageUsers);

    [ObservableProperty]
    public partial string RecoveryStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRecoveryCode { get; set; }

    public string Version => AppInfo.Version;

    public string Copyright => AppInfo.Copyright;

    public string DataDirectory => AppPaths.DataDirectory;

    public ObservableCollection<BackupInfo> Backups { get; } = [];

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; }

    [ObservableProperty]
    public partial string PurchaseHorizonDays { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ForecastWindowDays { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BackupsToKeep { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ForecastError { get; set; }

    [ObservableProperty]
    public partial BackupInfo? SelectedBackup { get; set; }

    [ObservableProperty]
    public partial string CurrentPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPasswordConfirm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    public override async Task OnNavigatedToAsync()
    {
        if (CanManageBackups)
        {
            await LoadBackupsAsync();
        }

        if (CanManageRecovery)
        {
            await LoadRecoveryStatusAsync();
        }
    }

    private async Task LoadRecoveryStatusAsync()
    {
        var issuedAt = await _recovery.GetCodeIssuedAtAsync();
        HasRecoveryCode = issuedAt is not null;
        RecoveryStatus = issuedAt is { } at
            ? string.Format(Strings.Settings_RecoveryIssued, at.ToLocalTime())
            : Strings.Settings_RecoveryMissing;
    }

    [RelayCommand]
    private async Task CreateRecoveryCodeAsync()
    {
        if (HasRecoveryCode && !_dialogs.Confirm(Strings.Settings_RecoveryConfirm, Strings.Settings_Recovery, Strings.Settings_RecoveryCreate))
        {
            return;
        }

        var code = await _recovery.IssueCodeAsync();
        _dialogs.ShowRecoveryCode(code);
        await LoadRecoveryStatusAsync();
    }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (value.Theme == _settings.Current.Theme)
        {
            return;
        }

        _theme.Apply(value.Theme);
        _ = _settings.SaveAsync(_settings.Current with { Theme = value.Theme });
    }

    [RelayCommand]
    private async Task SaveForecastAsync()
    {
        ForecastError = null;
        if (!int.TryParse(PurchaseHorizonDays, out var horizon) || horizon is < 1 or > 365 ||
            !int.TryParse(ForecastWindowDays, out var window) || window is < 7 or > 3650 ||
            !int.TryParse(BackupsToKeep, out var keep) || keep is < 1 or > 500)
        {
            ForecastError = Strings.Settings_InvalidNumbers;
            return;
        }

        await _settings.SaveAsync(_settings.Current with
        {
            PurchaseHorizonDays = horizon,
            ForecastWindowDays = window,
            BackupsToKeep = keep,
        });
        _backups.Prune(keep);
        _notifications.Success(Strings.Common_Saved);
    }

    [RelayCommand]
    private void ResetForecastFields()
    {
        PurchaseHorizonDays = _settings.Current.PurchaseHorizonDays.ToString();
        ForecastWindowDays = _settings.Current.ForecastWindowDays.ToString();
        BackupsToKeep = _settings.Current.BackupsToKeep.ToString();
        ForecastError = null;
    }

    [RelayCommand]
    private Task LoadBackupsAsync() => RunBusyAsync(async () =>
    {
        Backups.Clear();
        foreach (var backup in await _backups.GetBackupsAsync())
        {
            Backups.Add(backup);
        }
    });

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        await RunBusyAsync(async () =>
        {
            var path = await _backups.CreateBackupAsync("manual");
            _backups.Prune(_settings.Current.BackupsToKeep);
            _notifications.Success(Strings.Settings_BackupCreated, Path.GetFileName(path));
        });
        await LoadBackupsAsync();
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        backup ??= SelectedBackup;
        if (backup is null)
        {
            return;
        }

        var message = string.Format(Strings.Settings_RestoreConfirm, backup.CreatedAtUtc.ToLocalTime());
        if (!_dialogs.Confirm(message, Strings.Settings_RestoreTitle, Strings.Settings_Restore, isDestructive: true))
        {
            return;
        }

        await RunBusyAsync(() => _backups.RestoreAsync(backup.Path));
        _dialogs.ShowInfo(Strings.Settings_RestoreDone);
        AppRestart.Restart();
    }

    [RelayCommand]
    private async Task RestoreFromFileAsync()
    {
        var path = _dialogs.PickOpenFile(Strings.Settings_BackupFilter, AppPaths.BackupsDirectory);
        if (path is not null)
        {
            await RestoreBackupAsync(new BackupInfo(path, Path.GetFileName(path), File.GetCreationTimeUtc(path), new FileInfo(path).Length));
        }
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        PasswordError = null;
        if (NewPassword != NewPasswordConfirm)
        {
            PasswordError = Strings.Auth_PasswordsDoNotMatch;
            return;
        }

        try
        {
            await _auth.ChangeOwnPasswordAsync(CurrentPassword, NewPassword);
            CurrentPassword = NewPassword = NewPasswordConfirm = string.Empty;
            _notifications.Success(Strings.Settings_PasswordChanged);
        }
        catch (DomainException ex)
        {
            PasswordError = ex.Code == DomainErrorCode.InvalidCredentials ? Strings.Settings_WrongCurrentPassword : ErrorMessages.For(ex);
        }
    }

    [RelayCommand]
    private void ShowChangelog() => _changelog.ShowAll();

    [RelayCommand]
    private void ShowLicense() =>
        _dialogs.ShowDocument(Strings.License_Title, Strings.License_Subtitle, "Icon.Document", AppInfo.LicenseText);

    [RelayCommand]
    private static void OpenFolder(string? path)
    {
        var folder = path switch
        {
            "backups" => AppPaths.BackupsDirectory,
            "logs" => AppPaths.LogsDirectory,
            _ => AppPaths.DataDirectory,
        };
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}

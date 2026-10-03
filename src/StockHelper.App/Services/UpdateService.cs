using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StockHelper.App.Resources;
using Velopack;
using Velopack.Sources;

namespace StockHelper.App.Services;

public interface IUpdateService : INotifyPropertyChanged
{
    /// <summary>False in development builds (not installed by Velopack): updates are disabled.</summary>
    bool IsInstalled { get; }

    bool IsChecking { get; }

    bool IsUpdateReady { get; }

    string StatusText { get; }

    /// <summary>Startup check: downloads silently and shows an unobtrusive notification. Errors are only logged.</summary>
    Task CheckInBackgroundAsync();

    IAsyncRelayCommand CheckNowCommand { get; }

    IRelayCommand RestartCommand { get; }
}

/// <summary>Automatic updates from GitHub Releases via Velopack.</summary>
public sealed partial class UpdateService : ObservableObject, IUpdateService
{
    public const string RepositoryUrl = "https://github.com/KeFFiA/StockHelper";

    private readonly INotificationService _notifications;
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService(INotificationService notifications, ILogger<UpdateService> logger)
    {
        _notifications = notifications;
        _logger = logger;

        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update manager is unavailable");
        }

        StatusText = IsInstalled ? string.Empty : Strings.Updates_DevBuild;
    }

    public bool IsInstalled => _manager?.IsInstalled == true;

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    public partial bool IsUpdateReady { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; }

    public async Task CheckInBackgroundAsync()
    {
        if (!IsInstalled)
        {
            return;
        }

        try
        {
            if (await CheckAndDownloadAsync())
            {
                ShowReadyNotification();
            }
        }
        catch (Exception ex)
        {
            // No internet, GitHub unavailable, etc.: not a user-facing error.
            _logger.LogWarning(ex, "Background update check failed");
            StatusText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (!IsInstalled)
        {
            StatusText = Strings.Updates_DevBuild;
            return;
        }

        try
        {
            if (await CheckAndDownloadAsync())
            {
                ShowReadyNotification();
            }
            else if (!IsUpdateReady)
            {
                StatusText = Strings.Updates_UpToDate;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual update check failed");
            StatusText = Strings.Updates_CheckFailed;
        }
    }

    [RelayCommand]
    private void Restart()
    {
        if (_manager is not null && _pending is not null)
        {
            _logger.LogInformation("Applying update {Version} and restarting", _pending.TargetFullRelease.Version);
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
    }

    /// <returns>True when a new update has just been downloaded.</returns>
    private async Task<bool> CheckAndDownloadAsync()
    {
        if (_manager is null || IsChecking)
        {
            return false;
        }

        if (IsUpdateReady)
        {
            return false;
        }

        IsChecking = true;
        StatusText = Strings.Updates_Checking;
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                StatusText = Strings.Updates_UpToDate;
                return false;
            }

            StatusText = string.Format(Strings.Updates_Downloading, info.TargetFullRelease.Version);
            await _manager.DownloadUpdatesAsync(info);
            _pending = info;
            IsUpdateReady = true;
            StatusText = string.Format(Strings.Updates_Ready, info.TargetFullRelease.Version);
            _logger.LogInformation("Update {Version} downloaded", info.TargetFullRelease.Version);
            return true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void ShowReadyNotification() =>
        _notifications.Show(
            NotificationKind.Info,
            Strings.Updates_AvailableTitle,
            string.Format(Strings.Updates_AvailableMessage, _pending?.TargetFullRelease.Version),
            Strings.Updates_RestartNow,
            Restart);
}

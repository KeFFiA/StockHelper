using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.ViewModels;
using StockHelper.App.Views;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Services;

namespace StockHelper.App.Services;

/// <summary>Owns the app lifecycle: database initialization, sign-in, main window and sign-out.</summary>
public sealed class SessionCoordinator(
    IServiceProvider services,
    IDatabaseInitializer databaseInitializer,
    IAuthService auth,
    IThemeService theme,
    ISettingsService settings,
    IBackupService backups,
    IUpdateService updates,
    IChangelogService changelog,
    IDialogService dialogs)
{
    /// <summary>
    /// Local administrator reset: run on the computer with the database, e.g.
    /// <c>%LOCALAPPDATA%\StockHelper\current\StockHelper.exe --reset-admin</c>.
    /// </summary>
    public const string ResetAdminArgument = "--reset-admin";

    private MainWindow? _mainWindow;

    public async Task StartAsync()
    {
        theme.Apply(settings.Current.Theme);
        await databaseInitializer.InitializeAsync(CreateSeedData());
        backups.Prune(settings.Current.BackupsToKeep);

        if (AppInfo.IsDemo)
        {
            var demo = services.GetRequiredService<DemoData>();
            await demo.SeedUsersAsync();
            await demo.SeedAsync();
        }
        else if (Environment.GetCommandLineArgs().Contains("--demo", StringComparer.OrdinalIgnoreCase))
        {
            await services.GetRequiredService<DemoData>().SeedAsync();
        }

        var localReset = Environment.GetCommandLineArgs().Contains(ResetAdminArgument, StringComparer.OrdinalIgnoreCase);
        if (!await ShowAuthAsync(localReset))
        {
            Application.Current.Shutdown();
            return;
        }

        _mainWindow = services.GetRequiredService<MainWindow>();
        Application.Current.MainWindow = _mainWindow;
        _mainWindow.Closed += (_, _) => Application.Current.Shutdown();
        _mainWindow.Show();
        await services.GetRequiredService<ShellViewModel>().StartAsync();
        await changelog.ShowIfUpdatedAsync();

        // Fire and forget: never blocks the UI, errors are logged inside.
        _ = updates.CheckInBackgroundAsync();
    }

    public async Task SignOutAsync()
    {
        auth.SignOut();
        _mainWindow?.Hide();

        if (!await ShowAuthAsync())
        {
            Application.Current.Shutdown();
            return;
        }

        _mainWindow?.Show();
        await services.GetRequiredService<ShellViewModel>().StartAsync();
    }

    private async Task<bool> ShowAuthAsync(bool localReset = false)
    {
        var viewModel = services.GetRequiredService<AuthViewModel>();
        await viewModel.InitializeAsync(localReset);
        var window = new AuthWindow(viewModel, dialogs);
        return window.ShowDialog() == true;
    }

    private static SeedData CreateSeedData() => new(
        Units: Strings.Seed_Units.Split('|'),
        DefaultLocation: Strings.Seed_DefaultLocation);
}

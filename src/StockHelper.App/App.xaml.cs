using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.App.ViewModels;
using StockHelper.App.Views;

namespace StockHelper.App;

public partial class App : Application
{
    private readonly string[] _args;
    private IHost? _host;

    public App(string[] args)
    {
        _args = args;

        // WPF bindings use en-US by default; format numbers and dates with the user's regional settings.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
    }

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services ?? throw new InvalidOperationException("Host is not started.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = AppHost.Build(_args);
            GlobalExceptionHandler.Register(this, _host.Services.GetRequiredService<IDialogService>());
            await _host.StartAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Closed += (_, _) => Shutdown();
            window.Show();

            await _host.Services.GetRequiredService<ShellViewModel>().StartAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show(Strings.Error_Startup, Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Stop off the UI thread so hosted services can't deadlock on the dispatcher.
            Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(5))).Wait(TimeSpan.FromSeconds(6));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}

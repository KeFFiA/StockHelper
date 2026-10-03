using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.App.Services;

namespace StockHelper.App;

public partial class App : Application
{
    private readonly string[] _args;
    private IHost? _host;

    public App(string[] args)
    {
        _args = args;

        // The UI is Russian, so dates and numbers use Russian formatting regardless of the Windows region.
        var culture = CultureInfo.GetCultureInfo(AppCulture.Name);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // WPF bindings use en-US unless the language is set explicitly.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
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

            await _host.Services.GetRequiredService<SessionCoordinator>().StartAsync();
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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using StockHelper.App.Services;
using StockHelper.App.ViewModels;
using StockHelper.App.ViewModels.Pages;
using StockHelper.App.Views;

namespace StockHelper.App.Infrastructure;

/// <summary>Composition root: configuration, logging and DI registrations.</summary>
public static class AppHost
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
        builder.Services.AddSerilog();

        AddUi(builder.Services);

        return builder.Build();
    }

    private static void AddUi(IServiceCollection services)
    {
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<DashboardViewModel>();
    }
}

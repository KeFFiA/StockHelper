using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using StockHelper.App.Services;
using StockHelper.App.ViewModels;
using StockHelper.App.ViewModels.Pages;
using StockHelper.App.Views;
using StockHelper.Core.Abstractions;
using StockHelper.Core.Security;
using StockHelper.Core.Services;
using StockHelper.Data;

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

        builder.Services.AddStockHelperData(ReadDatabaseOptions(builder.Configuration));
        AddCore(builder.Services);
        AddUi(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// "Database" section of appsettings.json. An empty connection string means the local SQLite file in %APPDATA%.
    /// Switching to PostgreSQL = Provider "PostgreSql" + a connection string.
    /// </summary>
    private static DatabaseOptions ReadDatabaseOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Database");
        var provider = Enum.TryParse<DatabaseProvider>(section["Provider"], ignoreCase: true, out var p) ? p : DatabaseProvider.Sqlite;
        var connectionString = section["ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString) && provider == DatabaseProvider.Sqlite)
        {
            connectionString = $"Data Source={AppPaths.DatabaseFile}";
        }

        return new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString ?? string.Empty,
            BackupsDirectory = AppPaths.BackupsDirectory,
        };
    }

    private static void AddCore(IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
    }

    private static void AddUi(IServiceCollection services)
    {
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IExportService, ExcelExportService>();
        services.AddSingleton<SessionCoordinator>();
        services.AddSingleton<IAnalyticsService, AnalyticsService>();
        services.AddTransient<DemoData>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<AuthViewModel>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ItemsViewModel>();
        services.AddTransient<CatalogsViewModel>();
        services.AddTransient<ReceiptsViewModel>();
        services.AddTransient<StockTakesViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddSingleton<StockTakeSessionFactory>();
        services.AddTransient<UsersViewModel>();
    }
}

using Serilog;
using StockHelper.App.Infrastructure;
using Velopack;

namespace StockHelper.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles install/update hooks and applies an update downloaded earlier
        // ("restart later" means the update is installed on the next start).
        VelopackApp.Build().Run();

        AppPaths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.LogsDirectory, "stockhelper-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting StockHelper {Version}", AppInfo.Version);
            var app = new App(args);
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}

using System.Reflection;

namespace StockHelper.App.Infrastructure;

public static class AppInfo
{
    /// <summary>Demo build for showing the app to customers (built with -p:DemoBuild=true).</summary>
#if DEMO
    public static bool IsDemo => true;
#else
    public static bool IsDemo => false;
#endif

    /// <summary>Password of the sample accounts in the demo build.</summary>
    public const string DemoPassword = "demo";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}

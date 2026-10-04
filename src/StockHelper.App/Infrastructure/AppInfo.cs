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

    public static string Copyright { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>License agreement (LICENSE.md of the repository, Russian and English), embedded at build time.</summary>
    public static string LicenseText
    {
        get
        {
            using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("StockHelper.License.md");
            return stream is null ? string.Empty : new StreamReader(stream).ReadToEnd();
        }
    }

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}

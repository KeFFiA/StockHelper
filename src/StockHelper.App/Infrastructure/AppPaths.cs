namespace StockHelper.App.Infrastructure;

/// <summary>
/// User data lives outside the install folder because Velopack replaces the app folder on update.
/// </summary>
public static class AppPaths
{
    /// <summary>%APPDATA%\StockHelper, or STOCKHELPER_DATA_DIR when set (tests, demos, portable use).</summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("STOCKHELPER_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StockHelper");

    public static string DatabaseFile => Path.Combine(DataDirectory, "stockhelper.db");

    public static string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

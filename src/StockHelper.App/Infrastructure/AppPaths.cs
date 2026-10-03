namespace StockHelper.App.Infrastructure;

/// <summary>
/// User data lives outside the install folder because Velopack replaces the app folder on update.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StockHelper");

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

using System.Diagnostics;
using System.Windows;

namespace StockHelper.App.Infrastructure;

public static class AppRestart
{
    /// <summary>Starts a new instance with the same arguments and closes the current one.</summary>
    public static void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            // One-time switches are not repeated on restart.
            var args = Environment.GetCommandLineArgs().Skip(1).Where(a =>
                !a.Equals("--demo", StringComparison.OrdinalIgnoreCase) &&
                !a.Equals(Services.SessionCoordinator.ResetAdminArgument, StringComparison.OrdinalIgnoreCase));
            Process.Start(new ProcessStartInfo(exe, string.Join(' ', args)) { UseShellExecute = false });
        }

        Application.Current.Shutdown();
    }
}

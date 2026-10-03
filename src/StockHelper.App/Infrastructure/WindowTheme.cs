using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using StockHelper.Core.Abstractions;

namespace StockHelper.App.Infrastructure;

/// <summary>
/// Makes the native title bar follow the app theme (Windows 10 2004+ / Windows 11 keep it light otherwise).
/// </summary>
public static class WindowTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private static AppTheme _theme = AppTheme.System;

    /// <summary>Registers a handler so every window gets the right title bar when it is shown.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Apply((Window)s)));

    public static void SetTheme(AppTheme theme)
    {
        _theme = theme;
        foreach (Window window in Application.Current.Windows)
        {
            Apply(window);
        }
    }

    public static bool IsDark => _theme switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => IsSystemDark(),
    };

    private static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));

        // Windows 10 repaints the caption only when the frame changes or the window is re-activated.
        _ = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        if (window.IsActive)
        {
            _ = SendMessage(handle, WmNcActivate, IntPtr.Zero, IntPtr.Zero);
            _ = SendMessage(handle, WmNcActivate, new IntPtr(1), IntPtr.Zero);
        }
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int WmNcActivate = 0x0086;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

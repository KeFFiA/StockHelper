using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StockHelper.App.Services;

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A non-blocking message shown in the shell's notification area (InfoBar-like toast).</summary>
public sealed partial class Notification : ObservableObject
{
    public required NotificationKind Kind { get; init; }

    public required string Title { get; init; }

    public string? Message { get; init; }

    public string? ActionText { get; init; }

    public IRelayCommand? ActionCommand { get; init; }

    public IRelayCommand? DismissCommand { get; set; }

    public bool IsSuccess => Kind == NotificationKind.Success;

    public bool IsWarning => Kind == NotificationKind.Warning;

    public bool IsError => Kind == NotificationKind.Error;

    public bool IsInfo => Kind == NotificationKind.Info;

    public bool HasAction => ActionCommand is not null;
}

public interface INotificationService
{
    ObservableCollection<Notification> Items { get; }

    /// <summary>Shows a toast. Toasts without an action disappear automatically.</summary>
    void Show(NotificationKind kind, string title, string? message = null, string? actionText = null, Action? action = null);

    void Success(string title, string? message = null);
}

public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(4);

    public ObservableCollection<Notification> Items { get; } = [];

    public void Success(string title, string? message = null) => Show(NotificationKind.Success, title, message);

    public void Show(NotificationKind kind, string title, string? message = null, string? actionText = null, Action? action = null)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Show(kind, title, message, actionText, action));
            return;
        }

        Notification? notification = null;
        notification = new Notification
        {
            Kind = kind,
            Title = title,
            Message = message,
            ActionText = actionText,
            ActionCommand = action is null ? null : new RelayCommand(() =>
            {
                Items.Remove(notification!);
                action();
            }),
        };
        notification.DismissCommand = new RelayCommand(() => Items.Remove(notification));
        Items.Add(notification);

        if (action is null)
        {
            var timer = new DispatcherTimer { Interval = AutoDismissAfter };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Items.Remove(notification);
            };
            timer.Start();
        }
    }
}

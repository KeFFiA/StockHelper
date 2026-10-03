using System.Windows;
using System.Windows.Controls;

namespace StockHelper.App.Behaviors;

/// <summary>Makes <see cref="PasswordBox.Password"/> bindable for MVVM.</summary>
public static class PasswordBoxBinding
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    static PasswordBoxBinding()
    {
        // A class handler works even when the bound value never changes from its default (empty string).
        EventManager.RegisterClassHandler(typeof(PasswordBox), PasswordBox.PasswordChangedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                var box = (PasswordBox)sender;
                if (GetPassword(box) != box.Password)
                {
                    SetPassword(box, box.Password);
                }
            }));
    }

    public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box && box.Password != (e.NewValue as string ?? string.Empty))
        {
            box.Password = e.NewValue as string ?? string.Empty;
        }
    }
}

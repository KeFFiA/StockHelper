using System.Windows;
using System.Windows.Input;
using StockHelper.App.Services;

namespace StockHelper.App.Views.Dialogs;

public partial class DialogWindow : Window
{
    public DialogWindow(DialogKind kind, string title, string message, string primary, string? secondary, bool destructive)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primary;

        if (secondary is null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsCancel = true;
        }
        else
        {
            SecondaryButton.Content = secondary;
        }

        var (iconKey, brushKey) = kind switch
        {
            DialogKind.Error => ("Icon.Error", "SystemFillColorCriticalBrush"),
            DialogKind.Warning => ("Icon.Warning", "SystemFillColorCautionBrush"),
            _ => ("Icon.Info", "AccentFillColorDefaultBrush"),
        };
        IconText.Text = (string)FindResource(iconKey);
        IconText.SetResourceReference(ForegroundProperty, brushKey);
        IconCircle.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, brushKey);

        if (destructive)
        {
            // Only destructive actions get a red button, and Enter does not confirm them by accident.
            PrimaryButton.SetResourceReference(BackgroundProperty, "SystemFillColorCriticalBrush");
            PrimaryButton.SetResourceReference(BorderBrushProperty, "SystemFillColorCriticalBrush");
            PrimaryButton.IsDefault = false;
            SecondaryButton.IsDefault = true;
        }

        Loaded += (_, _) => (destructive ? SecondaryButton : PrimaryButton).Focus();
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

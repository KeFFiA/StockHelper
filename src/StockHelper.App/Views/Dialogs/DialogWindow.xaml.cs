using System.Windows;
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
            DialogKind.Question => ("Icon.Info", "AccentTextFillColorPrimaryBrush"),
            _ => ("Icon.Info", "AccentTextFillColorPrimaryBrush"),
        };
        IconText.Text = (string)FindResource(iconKey);
        IconText.SetResourceReference(ForegroundProperty, brushKey);

        if (destructive)
        {
            // Destructive actions must not be confirmed by a stray Enter press.
            PrimaryButton.IsDefault = false;
            SecondaryButton.IsDefault = true;
        }

        Loaded += (_, _) => (destructive ? SecondaryButton : PrimaryButton).Focus();
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => DialogResult = true;
}

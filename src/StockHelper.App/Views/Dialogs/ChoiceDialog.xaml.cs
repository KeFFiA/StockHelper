using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockHelper.App.Services;

namespace StockHelper.App.Views.Dialogs;

/// <summary>Asks the user to pick one of several options presented as tiles.</summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog(string title, string message, IReadOnlyList<DialogOption> options)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        Options.ItemsSource = options.Select((o, i) => new
        {
            Index = i,
            o.Title,
            o.Description,
            Icon = o.IconKey is null ? null : FindResource(o.IconKey),
        }).ToList();
    }

    public int? SelectedIndex { get; private set; }

    private void OnOptionClick(object sender, RoutedEventArgs e)
    {
        SelectedIndex = (int)((Button)sender).Tag;
        DialogResult = true;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

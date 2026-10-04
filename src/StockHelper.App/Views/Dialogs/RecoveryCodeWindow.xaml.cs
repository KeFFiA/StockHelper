using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using StockHelper.App.Resources;

namespace StockHelper.App.Views.Dialogs;

/// <summary>Shows a recovery code once. It closes only after the user confirms the code is saved.</summary>
public partial class RecoveryCodeWindow : Window
{
    private readonly string _code;

    public RecoveryCodeWindow(string code)
    {
        InitializeComponent();
        _code = code;
        CodeBox.Text = code;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_code);
            CopyButton.Content = Strings.Recovery_Copied;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is busy in another app: the code can still be selected and copied by hand.
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = Strings.Recovery_FileName, Filter = Strings.Recovery_FileFilter, AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, string.Format(Strings.Recovery_FileContent, _code, DateTime.Now));
        }
    }

    private void OnDone(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

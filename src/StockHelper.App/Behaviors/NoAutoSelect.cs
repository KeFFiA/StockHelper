using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace StockHelper.App.Behaviors;

/// <summary>
/// An editable ComboBox selects all of its text on focus and after an item is picked, so a stray key press
/// wipes the value. App-wide, the caret is put at the end of the text instead.
/// </summary>
public static class NoAutoSelect
{
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnComboBoxEvent), true);
        EventManager.RegisterClassHandler(typeof(ComboBox), Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnComboBoxEvent), true);
    }

    /// <summary>Puts the caret at the end of the text without selecting it.</summary>
    public static void CaretToEnd(TextBox box) => box.Select(box.Text.Length, 0);

    private static void OnComboBoxEvent(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox { IsEditable: true } combo)
        {
            return;
        }

        // After WPF's own selection, which happens later in the same input cycle.
        combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox box && box.SelectionLength > 0 && box.SelectionLength == box.Text.Length)
            {
                CaretToEnd(box);
            }
        });
    }
}

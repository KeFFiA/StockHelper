using System.Windows;
using Microsoft.Win32;
using StockHelper.App.Resources;
using StockHelper.App.Views.Dialogs;

namespace StockHelper.App.Services;

public enum DialogKind
{
    Info,
    Warning,
    Error,
    Question,
}

/// <summary>An option of a choice dialog; <c>IconKey</c> refers to an Icon.* token.</summary>
public sealed record DialogOption(string Title, string Description, string? IconKey = null);

public interface IDialogService
{
    /// <summary>Returns the index of the chosen option or null when cancelled.</summary>
    int? Choose(string title, string message, IReadOnlyList<DialogOption> options);

    void ShowInfo(string message, string? title = null);

    void ShowError(string message, string? title = null);

    bool Confirm(string message, string? title = null, string? confirmText = null, bool isDestructive = false);

    /// <summary>Returns the selected path or null when cancelled.</summary>
    string? PickSaveFile(string defaultFileName, string filter);

    string? PickOpenFile(string filter, string? initialDirectory = null);

    /// <summary>Shows a just-issued recovery code once; returns after the user confirms they saved it.</summary>
    void ShowRecoveryCode(string code);
}

public sealed class DialogService : IDialogService
{
    public void ShowInfo(string message, string? title = null) =>
        Show(DialogKind.Info, title ?? Strings.Dialog_InfoTitle, message, Strings.Common_Ok, null, false);

    public void ShowError(string message, string? title = null) =>
        Show(DialogKind.Error, title ?? Strings.Error_Title, message, Strings.Common_Ok, null, false);

    public bool Confirm(string message, string? title = null, string? confirmText = null, bool isDestructive = false) =>
        Show(isDestructive ? DialogKind.Warning : DialogKind.Question, title ?? Strings.Dialog_ConfirmTitle, message,
            confirmText ?? Strings.Common_Yes, Strings.Common_Cancel, isDestructive);

    public int? Choose(string title, string message, IReadOnlyList<DialogOption> options)
    {
        var window = new ChoiceDialog(title, message, options);
        var owner = GetOwner();
        if (owner is not null)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true ? window.SelectedIndex : null;
    }

    public string? PickSaveFile(string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = defaultFileName, Filter = filter, AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    public void ShowRecoveryCode(string code)
    {
        var window = new RecoveryCodeWindow(code);
        var owner = GetOwner();
        if (owner is not null)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
    }

    private static bool Show(DialogKind kind, string title, string message, string primary, string? secondary, bool destructive)
    {
        var owner = GetOwner();
        var window = new DialogWindow(kind, title, message, primary, secondary, destructive);
        if (owner is not null && owner != window)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }

    private static Window? GetOwner()
    {
        var app = Application.Current;
        return app?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
               ?? app?.MainWindow;
    }
}

using System.Windows;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.ViewModels;
using AMKeepSafe.Views;

namespace AMKeepSafe.Services;

public sealed class DialogService : IDialogService
{
    public void ShowMessage(string title, string message) => MessageWindow.Show(ActiveWindow(), title, message);

    public bool ConfirmPreview(FixPreview preview)
    {
        var window = new PreviewWindow(preview) { Owner = ActiveWindow() };
        return window.ShowDialog() == true && preview.IsConfirmed;
    }

    public void ShowUndo(UndoViewModel undo)
    {
        var window = new UndoWindow { DataContext = undo, Owner = ActiveWindow() };
        window.ShowDialog();
    }

    public bool Confirm(string heading, string body, string yesText, string noText) =>
        // The main (default) button is the safe "no", so pressing Enter never does the risky thing.
        ChoiceWindow.Show(ActiveWindow(), new Choice(heading, body, PrimaryText: noText, SecondaryText: yesText)) == ChoiceResult.Secondary;

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
}

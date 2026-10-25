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

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
}

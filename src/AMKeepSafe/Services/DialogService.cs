using System.Windows;
using AMKeepSafe.Views;

namespace AMKeepSafe.Services;

public sealed class DialogService : IDialogService
{
    public void ShowMessage(string title, string message) => MessageWindow.Show(ActiveWindow(), title, message);

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
}

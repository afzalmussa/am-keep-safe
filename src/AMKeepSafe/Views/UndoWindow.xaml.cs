using System.Windows;

namespace AMKeepSafe.Views;

public partial class UndoWindow : Window
{
    public UndoWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowSizing.FitToWorkArea(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

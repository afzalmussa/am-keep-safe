using System.Windows;

namespace AMKeepSafe.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowSizing.FitToWorkArea(this);
    }
}

using System.Windows;
using AMKeepSafe.Core.Fixes;

namespace AMKeepSafe.Views;

public partial class PreviewWindow : Window
{
    private readonly FixPreview _preview;

    public PreviewWindow(FixPreview preview)
    {
        InitializeComponent();
        _preview = preview;
        DataContext = preview;
        MaxHeight = Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 16);

        HeadingBlock.Text = preview.Title;
        IntroBlock.Text = preview.IsDryRun ? "" : preview.Intro;
        IntroBlock.Visibility = preview.IsDryRun ? Visibility.Collapsed : Visibility.Visible;
        DryRunText.Text = preview.Intro;
        DryRunBanner.Visibility = preview.IsDryRun ? Visibility.Visible : Visibility.Collapsed;
        ItemsList.ItemsSource = preview.Items;
        RestartBlock.Text = preview.RestartNote ?? "";
        RestartBlock.Visibility = preview.RestartNote is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = preview.CancelButton;
        ConfirmButton.Content = preview.ConfirmButton;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _preview.Confirm();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

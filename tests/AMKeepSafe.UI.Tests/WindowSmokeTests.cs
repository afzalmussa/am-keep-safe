using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AMKeepSafe.Core.Configuration;
using AMKeepSafe.ViewModels;
using AMKeepSafe.Views;

namespace AMKeepSafe.UI.Tests;

/// <summary>
/// Loads each window the way the app does. A missing style, broken binding source or bad
/// image path in the XAML throws here instead of crashing on a customer's PC.
/// </summary>
public class WindowSmokeTests
{
    private static MainViewModel CreateViewModel(bool dryRun = false) =>
        new(new FakeLog(), new FakeShell(), new FakeDialogs(), AppSettings.LoadEmbedded(),
            dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default);

    private static MainWindow CreateMainWindow(bool dryRun = false)
    {
        var window = new MainWindow { DataContext = CreateViewModel(dryRun) };
        LayOut((FrameworkElement)window.Content);
        return window;
    }

    private static void LayOut(FrameworkElement element)
    {
        element.Measure(new Size(1040, 720));
        element.Arrange(new Rect(0, 0, 1040, 720));
        element.UpdateLayout();
    }

    [Fact]
    public void Theme_resources_are_available()
    {
        WpfHost.Run(() =>
        {
            foreach (var key in new[] { "DarkBrush", "MintBrush", "LightBrush", "TextBrush", "HeadingFont", "BodyFont",
                                         "PrimaryButton", "SecondaryButton", "HeaderButton", "LinkButton", "Card",
                                         "AppWindowStyle", "MainTabControl", "MainTabItem", "BoolToVisibility" })
            {
                Assert.NotNull(Application.Current.TryFindResource(key));
            }
        });
    }

    [Fact]
    public void Main_window_has_the_four_tabs_in_order()
    {
        WpfHost.Run(() =>
        {
            var window = CreateMainWindow();

            var tabs = FindAll<TabItem>(window).Select(t => t.Header?.ToString() ?? "").ToArray();
            Assert.Equal(["Safety check", "Browser check", "Scam help", "Pro"], tabs);
            window.Close();
        });
    }

    [Fact]
    public void Safety_check_is_the_tab_that_opens_first()
    {
        WpfHost.Run(() =>
        {
            var window = CreateMainWindow();

            Assert.Equal("Safety check", FindAll<TabControl>(window).Single().SelectedItem is TabItem t ? t.Header : null);
            window.Close();
        });
    }

    [Fact]
    public void Main_window_has_the_brand_logo()
    {
        WpfHost.Run(() =>
        {
            var window = CreateMainWindow();

            var logo = Assert.Single(FindAll<Image>(window));
            Assert.NotNull(logo.Source);
            Assert.Equal("AM Software logo", AutomationProperties.GetName(logo));
            window.Close();
        });
    }

    [Fact]
    public void Header_shows_free_status_and_the_licence_button()
    {
        WpfHost.Run(() =>
        {
            var window = CreateMainWindow();

            Assert.Contains(FindAll<TextBlock>(window), t => t.Text == "Free version");
            Assert.Contains(FindAll<Button>(window), b => (b.Content as string) == "Enter licence key");
            window.Close();
        });
    }

    [Fact]
    public void Footer_has_the_business_name_help_link_and_view_log()
    {
        WpfHost.Run(() =>
        {
            var window = CreateMainWindow();
            var buttons = FindAll<Button>(window).Select(b => b.Content as string).ToList();

            Assert.Contains(FindAll<TextBlock>(window), t => t.Text == "AM Software, ");
            Assert.Contains("amsoftware.uk", buttons);
            Assert.Contains("Get help", buttons);
            Assert.Contains("View log", buttons);
            Assert.Contains("Check for updates", buttons);
            Assert.Contains("Report a problem", buttons);
            window.Close();
        });
    }

    [Fact]
    public void Every_button_has_a_name_a_screen_reader_can_read()
    {
        WpfHost.Run(() =>
        {
            var window = CreateMainWindow();

            Assert.All(FindAll<Button>(window), b => Assert.False(
                string.IsNullOrWhiteSpace(b.Content as string) && string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)),
                "A button has no text and no AutomationProperties.Name"));
            window.Close();
        });
    }

    [Fact]
    public void Dry_run_banner_only_shows_in_dry_run()
    {
        WpfHost.Run(() =>
        {
            Assert.False(BannerVisible(dryRun: false));
            Assert.True(BannerVisible(dryRun: true));
        });

        static bool BannerVisible(bool dryRun)
        {
            var window = CreateMainWindow(dryRun);
            var banner = FindAll<TextBlock>(window).Single(t => t.Text.StartsWith("Test mode is on", StringComparison.Ordinal));
            var visible = IsVisibleInTree(banner);
            window.Close();
            return visible;
        }
    }

    [Fact]
    public void Get_Pro_is_hidden_while_no_checkout_link_is_set_up()
    {
        WpfHost.Run(() =>
        {
            var window = CreateMainWindow();

            var getPro = FindAll<Button>(window).Single(b => (b.Content as string) == "Get Pro");
            Assert.False(IsVisibleInTree(getPro));
            window.Close();
        });
    }

    // Sizes are the space left for the window on common laptop screens once Windows scaling and the taskbar are taken off.
    [Theory]
    [InlineData("main-window", 1040, 720, false)]
    [InlineData("main-window-laptop-125-percent", 1093, 570, false)]
    [InlineData("main-window-small-laptop-150-percent", 853, 460, false)]
    [InlineData("main-window-test-mode", 1040, 720, true)]
    public void Main_window_renders_at_different_sizes(string name, double width, double height, bool dryRun)
    {
        WpfHost.Run(() =>
        {
            var window = new MainWindow { DataContext = CreateViewModel(dryRun) };

            var bitmap = Screenshots.Render(window, name, width, height);

            Assert.True(Screenshots.HasContent(bitmap));
            window.Close();
        });
    }

    [Fact]
    public void Message_window_shows_heading_and_text()
    {
        WpfHost.Run(() =>
        {
            var window = new MessageWindow("A heading", "Some words");
            LayOut((FrameworkElement)window.Content);

            var texts = FindAll<TextBlock>(window).Select(t => t.Text).ToArray();
            Assert.Contains("A heading", texts);
            Assert.Contains("Some words", texts);
            window.Close();
        });
    }

    // Walks the logical tree, which exists before the window is shown on screen.
    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    // IsVisible is false for any window that isn't on screen, so check Visibility up the tree instead.
    // Stops below the Window itself, whose Visibility stays Collapsed until it is shown.
    private static bool IsVisibleInTree(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null and not Window; current = LogicalTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }
}

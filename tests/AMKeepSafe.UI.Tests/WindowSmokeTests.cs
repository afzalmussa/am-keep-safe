using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Configuration;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.ViewModels;
using AMKeepSafe.Views;

namespace AMKeepSafe.UI.Tests;

/// <summary>
/// Loads each window the way the app does. A missing style, broken binding source or bad
/// image path in the XAML throws here instead of crashing on a customer's PC.
/// </summary>
public class WindowSmokeTests
{
    // Built the way the app builds it, so every binding has something to bind to.
    private static MainViewModel CreateViewModel(bool dryRun = false) =>
        SampleChecks.MainWindowModel(SampleChecks.ViewModel(SampleChecks.Mixed()), dryRun);

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
            Assert.Contains("Undo changes", buttons);
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
    public void Safety_check_tab_shows_the_Check_my_PC_button_before_checking()
    {
        WpfHost.Run(() =>
        {
            var window = new MainWindow { DataContext = SampleChecks.MainWindowModel(SampleChecks.ViewModel(SampleChecks.Mixed())) };
            LayOut((FrameworkElement)window.Content);

            var view = FindAll<SafetyCheckView>(window).Single();
            LayOut(view);
            Assert.Contains(FindVisual<Button>(view), b => (b.Content as string) == "Check my PC");
            window.Close();
        });
    }

    [Theory]
    [InlineData("safety-check-results", 1040, 1900)]
    [InlineData("safety-check-results-small-laptop-150-percent", 853, 2600)]
    public Task Safety_check_results_render_with_every_colour(string name, double width, double height) => WpfHost.RunAsync(async () =>
    {
        var safetyCheck = SampleChecks.ViewModel(SampleChecks.Mixed());
        await safetyCheck.CheckCommand.ExecuteAsync(null);
        safetyCheck.Results.First(r => r.HasSteps).ToggleStepsCommand.Execute(null);
        var window = new MainWindow { DataContext = SampleChecks.MainWindowModel(safetyCheck) };

        Assert.True(Screenshots.HasContent(Screenshots.Render(window, name, width, height)));
        window.Close();
    });

    // Trust rule 2: no Pro prompt anywhere on the Safety check tab, even when results are red or amber.
    [Fact]
    public Task Safety_check_tab_never_mentions_Pro_even_with_red_and_amber_results() => WpfHost.RunAsync(async () =>
    {
        var safetyCheck = SampleChecks.ViewModel(SampleChecks.Mixed());
        await safetyCheck.CheckCommand.ExecuteAsync(null);
        foreach (var result in safetyCheck.Results.Where(r => r.HasSteps))
        {
            result.ToggleStepsCommand.Execute(null);
        }

        var view = new SafetyCheckView { DataContext = safetyCheck, Width = 1040 };
        view.Measure(new Size(1040, 6000));
        view.Arrange(new Rect(0, 0, 1040, 6000));
        view.UpdateLayout();

        Assert.Contains(safetyCheck.Results, r => r.Status.NeedsAction());
        var words = AllText(view).ToList();
        Assert.Contains(words, w => w.Contains("firewall", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(words, w => System.Text.RegularExpressions.Regex.IsMatch(w, @"\bPro\b|licence|upgrade", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    });

    [Fact]
    public Task Every_result_button_has_a_name_a_screen_reader_can_read() => WpfHost.RunAsync(async () =>
    {
        var safetyCheck = SampleChecks.ViewModel(SampleChecks.Mixed());
        await safetyCheck.CheckCommand.ExecuteAsync(null);
        var view = new SafetyCheckView { DataContext = safetyCheck };
        view.Measure(new Size(1040, 6000));
        view.Arrange(new Rect(0, 0, 1040, 6000));
        view.UpdateLayout();

        var buttons = FindVisual<Button>(view).ToList();
        Assert.True(buttons.Count > 5);
        Assert.All(buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Content as string), "A result button has no text"));
    });

    private static FixPreview SamplePreview(bool dryRun)
    {
        var catalog = AMKeepSafe.Core.Fixes.FixCatalog.LoadEmbedded();
        var change = new SettingChange
        {
            Key = "smb1Off", Kind = ChangeKind.WindowsFeature, Target = "SMB1Protocol", From = "Enabled", To = "Disabled",
            NeedsRestart = true, Undo = UndoSupport.InApp, Description = "Switch off SMB1, the old way of sharing files",
        };
        return FixPreview.ForFix(catalog.Common, catalog["smb1-off"], [change], dryRun);
    }

    [Theory]
    [InlineData(false, "preview")]
    [InlineData(true, "preview-test-mode")]
    public void Preview_window_shows_exactly_what_will_change_with_Cancel_as_the_default(bool dryRun, string name)
    {
        WpfHost.Run(() =>
        {
            var window = new PreviewWindow(SamplePreview(dryRun));
            LayOut((FrameworkElement)window.Content);

            var buttons = FindAll<Button>(window).ToList();
            var cancel = buttons.Single(b => (b.Content as string) == "Cancel");
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.Contains(buttons, b => (b.Content as string) == (dryRun ? "Show what would happen" : "Make the change"));
            Assert.True(Screenshots.HasContent(Screenshots.Render(window, name, 640)));
            window.Close();
        });
    }

    [Fact]
    public Task Undo_window_renders_with_both_kinds_of_entry() => WpfHost.RunAsync(async () =>
    {
        var flow = new FixFlow();
        var safetyCheck = flow.SafetyCheck();
        await safetyCheck.CheckCommand.ExecuteAsync(null);
        foreach (var result in safetyCheck.Results.Where(r => r.Status.NeedsAction()).ToList())
        {
            await result.Fixes[0].RunCommand.ExecuteAsync(null);
        }

        flow.Undo.Refresh();
        Assert.Equal(2, flow.Undo.Entries.Count);
        var window = new UndoWindow { DataContext = flow.Undo };

        Assert.True(Screenshots.HasContent(Screenshots.Render(window, "undo-changes", 720, 560)));
        window.Close();
    });

    [Fact]
    public Task Result_with_a_fix_renders_its_Fix_this_button() => WpfHost.RunAsync(async () =>
    {
        var flow = new FixFlow();
        var safetyCheck = flow.SafetyCheck();
        await safetyCheck.CheckCommand.ExecuteAsync(null);
        var view = new SafetyCheckView { DataContext = safetyCheck };
        view.Measure(new Size(1040, 4000));
        view.Arrange(new Rect(0, 0, 1040, 4000));
        view.UpdateLayout();

        var labels = FindVisual<Button>(view).Select(b => b.Content as string).ToList();
        Assert.Contains("Fix this", labels);
        Assert.Contains("Run a quick scan with Microsoft Defender", labels);
    });

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

    // Walks the visual tree, which includes items drawn from templates once layout has run.
    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisual<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<string> AllText(DependencyObject root) =>
        FindVisual<TextBlock>(root).Select(t => t.Text)
            .Concat(FindVisual<ContentPresenter>(root).Select(c => c.Content as string ?? ""))
            .Where(t => t.Length > 0);

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

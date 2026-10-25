using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Configuration;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.UI.Tests;

// Run on the UI thread, as in the app, so findings that arrive from the Safety check land in order.
public class BrowserCheckViewModelTests
{
    private const string HijackedSearch = """
        { "default_search_provider_data": { "template_url_data": { "short_name": "Super Search", "url": "https://supersearch.example/?q={searchTerms}" } } }
        """;

    private static CheckResultViewModel Result(BrowserCheckViewModel vm, string id) =>
        vm.Sections.SelectMany(s => s.Results).Single(r => r.CheckId == id);

    [Fact]
    public Task Before_checking_it_shows_the_check_button_and_nothing_else() => WpfHost.RunAsync(() =>
    {
        using var samples = new BrowserSamples();
        var vm = samples.ViewModel();

        Assert.Equal("Check my browsers", vm.CheckButtonText);
        Assert.Empty(vm.Sections);
        Assert.False(vm.HasHeadline);
        Assert.False(vm.HasNote);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Checking_shows_a_section_per_browser_profile_with_a_calm_headline() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples(HijackedSearch);
        var vm = samples.ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        var section = Assert.Single(vm.Sections);
        Assert.Equal("Google Chrome", section.Label);
        Assert.Equal(Browser.Chrome, section.Browser);
        Assert.Equal(CheckStatus.Attention, Result(vm, "chrome-default-search").Status);
        Assert.Equal(samples.Runner.Text.Catalog.Summary.SomeToLookAt, vm.Headline);
        Assert.Equal("Check again", vm.CheckButtonText);
        Assert.False(vm.IsChecking);
    });

    [Fact]
    public Task An_untouched_browser_gets_the_careful_all_clear() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples();
        var vm = samples.ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal("No problems found in the browsers we checked.", vm.Headline);
    });

    [Fact]
    public Task Findings_from_the_Safety_check_show_here_too() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples(HijackedSearch);
        var vm = samples.ViewModel();

        await samples.Runner.RunAsync();
        await Task.Yield();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        Assert.Single(vm.Sections);
        Assert.True(vm.HasResults);
    });

    [Fact]
    public Task A_different_account_is_explained_in_a_note() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples(context: new PcContext(false, @"PC\Admin", @"PC\Pat"));
        var vm = samples.ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.True(vm.HasNote);
        Assert.Contains(@"PC\Admin", vm.Note);
        Assert.Contains(@"PC\Pat", vm.Note);
    });

    [Fact]
    public Task Browser_page_buttons_open_that_browsers_own_page() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples(HijackedSearch);
        var vm = samples.ViewModel();
        await vm.CheckCommand.ExecuteAsync(null);

        var open = Result(vm, "chrome-default-search").Actions.Single(a => a.Label == "Open search settings");
        open.OpenCommand.Execute(null);

        Assert.Equal([(Browser.Chrome, "chrome://settings/search")], samples.Shell.OpenedBrowserPages);
        Assert.Empty(samples.Dialogs.Messages);
    });

    [Fact]
    public Task Managed_settings_offer_a_free_fix_and_nothing_about_Pro() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples();
        samples.Registry.Set(RegistryRoot.LocalMachine, BrowserSamples.ChromePolicies, "HomepageLocation", "https://hijack.example");
        var vm = samples.ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);
        var managed = Result(vm, "chrome-managed");

        Assert.Equal(CheckStatus.Attention, managed.Status);
        Assert.Equal(["Remove these settings"], managed.Fixes.Select(f => f.Label));
        Assert.Contains("HomepageLocation = https://hijack.example", managed.Items);
        Assert.DoesNotMatch(@"\bPro\b|licence|upgrade", string.Join(" ", managed.Summary, string.Join(" ", managed.Steps.Select(s => s.Text))));
    });

    [Fact]
    public Task On_a_work_PC_there_is_no_button_to_remove_the_settings() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples(context: new PcContext(true, @"WORK\Pat", @"WORK\Pat"));
        samples.Registry.Set(RegistryRoot.LocalMachine, BrowserSamples.ChromePolicies, "HomepageLocation", "https://intranet.example");
        var vm = samples.ViewModel();

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(CheckStatus.Info, Result(vm, "chrome-managed").Status);
        Assert.Empty(Result(vm, "chrome-managed").Fixes);
    });

    [Fact]
    public Task Removing_settings_previews_backs_up_offers_to_close_the_browser_and_checks_again() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples();
        samples.Registry.Set(RegistryRoot.LocalMachine, BrowserSamples.ChromePolicies, "HomepageLocation", "https://hijack.example");
        samples.Processes.Running.Add(Browser.Chrome);
        samples.Dialogs.YesAnswer = true;
        var vm = samples.ViewModel();
        await vm.CheckCommand.ExecuteAsync(null);

        await Result(vm, "chrome-managed").Fixes[0].RunCommand.ExecuteAsync(null);

        var preview = Assert.Single(samples.Dialogs.Previews);
        Assert.Contains("Google Chrome", preview.Title);
        Assert.Single(samples.Changer.Backups);
        Assert.Equal(SettingChange.Absent, samples.Changer.Applied.Single().To);
        Assert.Equal(samples.Changer.Backups[0], Assert.Single(samples.Journal.Entries).BackupFile);

        var question = Assert.Single(samples.Dialogs.Questions);
        Assert.Contains("Anything you haven't saved", question);
        Assert.Equal([Browser.Chrome], samples.Processes.CloseRequests);

        Assert.Equal(CheckStatus.Good, Result(vm, "chrome-managed").Status);
    });

    [Fact]
    public Task Saying_not_now_leaves_the_browser_open() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples();
        samples.Registry.Set(RegistryRoot.CurrentUser, BrowserSamples.ChromePolicies, "HomepageLocation", "https://hijack.example");
        samples.Processes.Running.Add(Browser.Chrome);
        samples.Dialogs.YesAnswer = false;
        var vm = samples.ViewModel();
        await vm.CheckCommand.ExecuteAsync(null);

        await Result(vm, "chrome-managed").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.Single(samples.Dialogs.Questions);
        Assert.Empty(samples.Processes.CloseRequests);
        Assert.Contains(Browser.Chrome, samples.Processes.Running);
    });

    [Fact]
    public Task A_browser_that_wont_close_is_never_forced_and_the_person_is_told() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples();
        samples.Registry.Set(RegistryRoot.LocalMachine, BrowserSamples.ChromePolicies, "HomepageLocation", "https://hijack.example");
        samples.Processes.Running.Add(Browser.Chrome);
        samples.Processes.Closes = false;
        samples.Dialogs.YesAnswer = true;
        var vm = samples.ViewModel();
        await vm.CheckCommand.ExecuteAsync(null);

        await Result(vm, "chrome-managed").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.Contains(samples.Dialogs.Messages, m => m.Message.Contains("Google Chrome is still open"));
    });

    [Fact]
    public Task No_close_question_when_the_browser_isnt_running() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples();
        samples.Registry.Set(RegistryRoot.LocalMachine, BrowserSamples.ChromePolicies, "HomepageLocation", "https://hijack.example");
        var vm = samples.ViewModel();
        await vm.CheckCommand.ExecuteAsync(null);

        await Result(vm, "chrome-managed").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.Empty(samples.Dialogs.Questions);
        Assert.Equal(CheckStatus.Good, Result(vm, "chrome-managed").Status);
    });

    [Fact]
    public Task Cancelling_the_preview_removes_nothing_and_asks_nothing_more() => WpfHost.RunAsync(async () =>
    {
        using var samples = new BrowserSamples();
        samples.Registry.Set(RegistryRoot.LocalMachine, BrowserSamples.ChromePolicies, "HomepageLocation", "https://hijack.example");
        samples.Processes.Running.Add(Browser.Chrome);
        samples.Dialogs.ConfirmAnswer = false;
        var vm = samples.ViewModel();
        await vm.CheckCommand.ExecuteAsync(null);

        await Result(vm, "chrome-managed").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.Empty(samples.Changer.Backups);
        Assert.Empty(samples.Changer.Applied);
        Assert.Empty(samples.Dialogs.Questions);
    });

    [Fact]
    public Task Open_the_Browser_check_on_the_Safety_check_moves_to_the_tab() => WpfHost.RunAsync(() =>
    {
        var log = new FakeLog();
        var main = new MainViewModel(log, new FakeShell(), new FakeDialogs(), AppSettings.LoadEmbedded(), AppOptions.Default);
        var catalog = CheckCatalog.LoadEmbedded();
        var result = catalog.Render(catalog[BrowserSummaryCheck.CheckId], new CheckOutcome(BrowserSummaryCheck.SomethingToLook));
        var vm = new CheckResultViewModel(result, "Worth a look", new FakeShell(), new FakeDialogs(), log, links: new ResultLinks((_, _) => null, main.GoToTab));

        Assert.Equal(0, main.SelectedTab);
        vm.Actions.Single(a => a.Action.AppTab == AppTab.BrowserCheck).OpenCommand.Execute(null);

        Assert.Equal((int)AppTab.BrowserCheck, main.SelectedTab);
        return Task.CompletedTask;
    });
}

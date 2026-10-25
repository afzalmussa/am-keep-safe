using System.Reflection;
using System.Text.RegularExpressions;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.UI.Tests;

public partial class SafetyCheckViewModelTests
{
    private readonly FakeShell _shell = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLog _log = new();

    private async Task<SafetyCheckViewModel> CheckedAsync(IReadOnlyList<ISafetyCheck>? checks = null)
    {
        var vm = SampleChecks.ViewModel(checks ?? SampleChecks.Mixed(), _shell, _dialogs, _log);
        await vm.CheckCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public void Before_checking_there_are_no_results_and_the_button_says_Check_my_PC()
    {
        var vm = SampleChecks.ViewModel(SampleChecks.Mixed());

        Assert.False(vm.HasResults);
        Assert.Empty(vm.Results);
        Assert.Equal("Check my PC", vm.CheckButtonText);
    }

    [Fact]
    public async Task Checking_shows_every_result_with_what_needs_doing_first()
    {
        var vm = await CheckedAsync();

        Assert.Equal(SampleChecks.Catalog.Checks.Count, vm.Results.Count);
        Assert.Equal(CheckStatus.Problem, vm.Results[0].Status);
        Assert.Equal("Firewall", vm.Results[0].Title);
        Assert.Equal([CheckStatus.Problem, CheckStatus.Attention, CheckStatus.Attention], vm.Results.Take(3).Select(r => r.Status));
        Assert.Equal(CheckStatus.Good, vm.Results[^1].Status);
        Assert.True(vm.HasResults);
        Assert.False(vm.IsChecking);
        Assert.Equal("Check again", vm.CheckButtonText);
    }

    [Fact]
    public async Task Headline_is_calm_when_something_needs_a_look()
    {
        var vm = await CheckedAsync();

        Assert.StartsWith(SampleChecks.Catalog.Summary.SomeToLookAt, vm.Headline);
        Assert.Contains(SampleChecks.Catalog.Summary.SomeNotChecked, vm.Headline);
    }

    [Fact]
    public async Task Headline_for_a_healthy_PC_is_the_careful_all_clear()
    {
        var vm = await CheckedAsync(SampleChecks.Healthy());

        Assert.Equal("No problems found in the things we checked.", vm.Headline);
    }

    [Fact]
    public async Task Each_result_has_its_status_label_and_words_from_checks_json()
    {
        var firewall = (await CheckedAsync()).Results.Single(r => r.CheckId == "firewall");

        Assert.Equal("Needs your attention", firewall.StatusLabel);
        Assert.Equal("The firewall is switched off for public networks, like in cafés. It's best to have it on everywhere.", firewall.Summary);
        Assert.Contains("Needs your attention", firewall.AccessibleName);
    }

    [Fact]
    public async Task Steps_are_hidden_until_asked_for_and_the_button_says_what_it_does()
    {
        var vm = await CheckedAsync();
        var firewall = vm.Results.Single(r => r.CheckId == "firewall");
        var encryption = vm.Results.Single(r => r.CheckId == "encryption");

        Assert.False(firewall.StepsOpen);
        Assert.Equal("How to fix", firewall.StepsButtonText);
        Assert.Equal("What you can do", encryption.StepsButtonText);

        firewall.ToggleStepsCommand.Execute(null);

        Assert.True(firewall.StepsOpen);
        Assert.Equal("Hide the steps", firewall.StepsButtonText);
        Assert.Equal(1, firewall.Steps[0].Number);
        Assert.Equal("1.", firewall.Steps[0].NumberText);
    }

    [Fact]
    public async Task A_settings_button_opens_the_right_Windows_page_and_logs_it()
    {
        var firewall = (await CheckedAsync()).Results.Single(r => r.CheckId == "firewall");

        firewall.Actions.Single().OpenCommand.Execute(null);

        Assert.Equal([WindowsSettingsPage.Firewall], _shell.OpenedSettings);
        Assert.Contains(_log.Entries, e => e.Message.Contains("Open firewall settings"));
    }

    [Fact]
    public async Task If_a_settings_page_wont_open_it_says_how_to_find_it()
    {
        _shell.SettingsWork = false;
        var firewall = (await CheckedAsync()).Results.Single(r => r.CheckId == "firewall");

        firewall.Actions.Single().OpenCommand.Execute(null);

        Assert.Contains("Settings", Assert.Single(_dialogs.Messages).Message);
    }

    [Fact]
    public async Task The_recovery_key_button_opens_Microsofts_page()
    {
        var encryption = (await CheckedAsync()).Results.Single(r => r.CheckId == "encryption");

        encryption.Actions.Single().OpenCommand.Execute(null);

        Assert.Equal(["https://aka.ms/myrecoverykey"], _shell.OpenedUrls);
    }

    [Fact]
    public async Task Remote_control_apps_are_listed_under_their_result()
    {
        var tools = (await CheckedAsync()).Results.Single(r => r.CheckId == "remote-access-tools");

        Assert.True(tools.HasItems);
        Assert.Equal(["AnyDesk: installed", "TeamViewer: open right now"], tools.Items);
    }

    [Fact]
    public async Task What_we_checked_lists_every_check_and_says_it_is_not_an_antivirus()
    {
        var vm = await CheckedAsync();

        vm.ShowWhatWeCheckedCommand.Execute(null);

        var message = Assert.Single(_dialogs.Messages);
        Assert.Equal("What we checked", message.Title);
        Assert.All(SampleChecks.Catalog.Checks, c => Assert.Contains(c.Title, message.Message));
        Assert.Contains("not an antivirus", message.Message);
    }

    [Fact]
    public async Task Starting_the_check_is_logged()
    {
        await CheckedAsync();

        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Info && e.Message == "User started the safety check.");
    }

    // Trust rule 2: a red or amber result never has a Pro prompt next to it. The result has no way to show one.
    [Theory]
    [InlineData(typeof(CheckResultViewModel))]
    [InlineData(typeof(ResultActionViewModel))]
    [InlineData(typeof(StepViewModel))]
    [InlineData(typeof(FixButtonViewModel))]
    public void Result_view_models_have_nothing_to_do_with_Pro(Type type)
    {
        var names = type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name);

        Assert.DoesNotContain(names, n => ProWord().IsMatch(n) || n.Contains("Licence", StringComparison.Ordinal) || n.Contains("Upgrade", StringComparison.Ordinal));
    }

    // "Pro" as its own word in a member name, like IsPro or GetProCommand, but not "Progress".
    [GeneratedRegex("(^|[a-z])Pro([A-Z]|$)")]
    private static partial Regex ProWord();
}

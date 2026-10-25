using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.UI.Tests;

public class FixAndUndoTests
{
    private static async Task<(FixFlow Flow, SafetyCheckViewModel Vm)> CheckedAsync(bool dryRun = false)
    {
        var flow = new FixFlow(dryRun);
        var vm = flow.SafetyCheck();
        await vm.CheckCommand.ExecuteAsync(null);
        return (flow, vm);
    }

    private static CheckResultViewModel Result(SafetyCheckViewModel vm, string id) => vm.Results.Single(r => r.CheckId == id);

    [Fact]
    public async Task Fix_buttons_appear_only_where_fixes_json_offers_them()
    {
        var (_, vm) = await CheckedAsync();

        Assert.Equal(["Fix this"], Result(vm, "firewall").Fixes.Select(f => f.Label));
        Assert.Equal(["Fix this"], Result(vm, "remote-desktop").Fixes.Select(f => f.Label));
        Assert.Equal(["Run a quick scan with Microsoft Defender"], Result(vm, "antivirus").Fixes.Select(f => f.Label));
    }

    [Fact]
    public async Task Fixing_shows_the_preview_then_makes_the_change_and_the_result_turns_green()
    {
        var (flow, vm) = await CheckedAsync();
        var firewall = Result(vm, "firewall");
        Assert.Equal(CheckStatus.Problem, firewall.Status);

        await firewall.Fixes[0].RunCommand.ExecuteAsync(null);

        var preview = Assert.Single(flow.Dialogs.Previews);
        Assert.Equal("Turn the firewall back on", preview.Title);
        Assert.Equal("Turn the firewall on for public networks, like in cafés", Assert.Single(preview.Items).Description);
        Assert.Equal("on", flow.Changer.Get(ChangeKind.FirewallProfile, "Public"));
        Assert.Equal(CheckStatus.Good, Result(vm, "firewall").Status);
        Assert.Equal(("Done", "The firewall is on again."), flow.Dialogs.Messages[^1]);
    }

    [Fact]
    public async Task The_fixed_result_stays_in_the_same_place_so_nothing_jumps_around()
    {
        var (_, vm) = await CheckedAsync();
        var index = vm.Results.IndexOf(Result(vm, "firewall"));

        await Result(vm, "firewall").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.Equal("firewall", vm.Results[index].CheckId);
    }

    [Fact]
    public async Task Cancelling_the_preview_changes_nothing()
    {
        var (flow, vm) = await CheckedAsync();
        flow.Dialogs.ConfirmAnswer = false;

        await Result(vm, "firewall").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.Empty(flow.Changer.Applied);
        Assert.Empty(flow.Journal.Entries);
        Assert.Equal(CheckStatus.Problem, Result(vm, "firewall").Status);
        Assert.Contains(flow.Log.Entries, e => e.Message.Contains("cancelled"));
    }

    [Fact]
    public async Task In_test_mode_the_preview_says_so_and_nothing_changes()
    {
        var (flow, vm) = await CheckedAsync(dryRun: true);

        await Result(vm, "firewall").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.True(flow.Dialogs.Previews[0].IsDryRun);
        Assert.Empty(flow.Changer.Applied);
        Assert.Equal("off", flow.Changer.Get(ChangeKind.FirewallProfile, "Public"));
        Assert.Equal("Test mode", flow.Dialogs.Messages[^1].Title);
    }

    [Fact]
    public async Task Fixing_something_already_fixed_says_theres_nothing_to_change()
    {
        var (flow, vm) = await CheckedAsync();
        flow.Changer.Set(ChangeKind.FirewallProfile, "Public", "on");

        await Result(vm, "firewall").Fixes[0].RunCommand.ExecuteAsync(null);

        Assert.Empty(flow.Dialogs.Previews);
        Assert.Equal("Nothing to change", flow.Dialogs.Messages[^1].Title);
        Assert.Equal(CheckStatus.Good, Result(vm, "firewall").Status);
    }

    [Fact]
    public async Task Turning_Remote_Desktop_off_can_be_undone_from_the_undo_list()
    {
        var (flow, vm) = await CheckedAsync();
        await Result(vm, "remote-desktop").Fixes[0].RunCommand.ExecuteAsync(null);
        Assert.Equal("1", flow.Changer.Get(ChangeKind.RegistryValue, "fDenyTSConnections", RegistryRoot.LocalMachine, FixFlow.TerminalServer));

        flow.Undo.Refresh();
        var entry = Assert.Single(flow.Undo.Entries);
        Assert.Equal("Turn off Remote Desktop, so other computers can't use it to connect to this one", entry.DisplayName);
        Assert.True(entry.CanUndo);
        Assert.False(entry.CanOpenSettings);

        await entry.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Undo a change", flow.Dialogs.Previews[^1].Title);
        Assert.Equal("0", flow.Changer.Get(ChangeKind.RegistryValue, "fDenyTSConnections", RegistryRoot.LocalMachine, FixFlow.TerminalServer));
        var after = Assert.Single(flow.Undo.Entries);
        Assert.False(after.CanUndo);
        Assert.StartsWith("Undone on ", after.Note);
    }

    // Trust rule 6 on screen: protection switched on offers Windows Settings, never an Undo button.
    [Fact]
    public async Task Turning_the_firewall_on_offers_Windows_Settings_instead_of_undo()
    {
        var (flow, vm) = await CheckedAsync();
        await Result(vm, "firewall").Fixes[0].RunCommand.ExecuteAsync(null);

        flow.Undo.Refresh();
        var entry = Assert.Single(flow.Undo.Entries);

        Assert.False(entry.CanUndo);
        Assert.True(entry.CanOpenSettings);
        Assert.Equal(flow.FixCatalog.Common.ViaSettingsNote, entry.Note);

        entry.OpenSettingsCommand.Execute(null);
        Assert.Equal([WindowsSettingsPage.Firewall], flow.Shell.OpenedSettings);
    }

    [Fact]
    public void The_undo_list_says_so_when_nothing_has_been_changed()
    {
        var flow = new FixFlow();

        flow.Undo.Refresh();

        Assert.True(flow.Undo.IsEmpty);
    }

    [Fact]
    public void The_footer_Undo_changes_button_refreshes_and_opens_the_list()
    {
        var flow = new FixFlow();
        var main = new MainViewModel(flow.Log, flow.Shell, flow.Dialogs, AMKeepSafe.Core.Configuration.AppSettings.LoadEmbedded(),
            AMKeepSafe.Core.Configuration.AppOptions.Default, flow.SafetyCheck(), flow.Undo);

        main.OpenUndoCommand.Execute(null);

        Assert.Same(flow.Undo, Assert.Single(flow.Dialogs.UndoShown));
        Assert.Contains(flow.Log.Entries, e => e.Level == LogLevel.Info && e.Message == "User opened the undo list.");
    }

    [Fact]
    public async Task While_a_fix_runs_its_buttons_are_disabled()
    {
        var (flow, vm) = await CheckedAsync();
        var firewall = Result(vm, "firewall");

        firewall.IsBusy = true;

        Assert.False(firewall.Fixes[0].RunCommand.CanExecute(null));
        firewall.IsBusy = false;
        Assert.True(firewall.Fixes[0].RunCommand.CanExecute(null));
        Assert.Equal(flow.FixCatalog.Common.WorkingText, firewall.WorkingText);
    }
}

using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Fixes;

/// <summary>Each fix plans only what's needed, with the shipped wording, and planning never changes anything.</summary>
public class FixPlanTests
{
    private static readonly FixCatalog Catalog = FixCatalog.LoadEmbedded();
    private readonly FakeChanger _changer = new();

    private async Task<IReadOnlyList<SettingChange>> PlanAsync(IFix fix)
    {
        var plan = await fix.PlanAsync(Catalog[fix.Id], CancellationToken.None);
        Assert.Empty(_changer.Applied);
        return plan;
    }

    private static SettingChange Firewall(string profile) => new() { Key = "k", Kind = ChangeKind.FirewallProfile, Target = profile };

    private static SettingChange Uac(string name) => new()
    {
        Key = "k", Kind = ChangeKind.RegistryValue, Root = RegistryRoot.LocalMachine,
        RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", Target = name,
    };

    [Fact]
    public async Task Firewall_turns_on_only_the_networks_that_are_off()
    {
        _changer.Set(Firewall("Domain"), "on").Set(Firewall("Private"), "off").Set(Firewall("Public"), "off");

        var plan = await PlanAsync(new FirewallOnFix(_changer));

        Assert.Equal(["Private", "Public"], plan.Select(c => c.Target));
        Assert.All(plan, c => Assert.Equal("on", c.To));
        Assert.All(plan, c => Assert.Equal("off", c.From));
        Assert.Equal("Turn the firewall on for public networks, like in cafés", plan[1].Description);
        Assert.All(plan, c => Assert.Equal(UndoSupport.ViaSettings, c.Undo));
    }

    [Fact]
    public async Task Nothing_is_planned_when_the_firewall_is_already_on()
    {
        _changer.Set(Firewall("Domain"), "on").Set(Firewall("Private"), "on").Set(Firewall("Public"), "on");

        Assert.Empty(await PlanAsync(new FirewallOnFix(_changer)));
    }

    [Fact]
    public async Task Permission_prompts_change_only_the_values_that_are_off_and_flag_the_restart()
    {
        _changer.Set(Uac("EnableLUA"), "0").Set(Uac("ConsentPromptBehaviorAdmin"), "5");

        var change = Assert.Single(await PlanAsync(new UacOnFix(_changer)));

        Assert.Equal("EnableLUA", change.Target);
        Assert.Equal("1", change.To);
        Assert.True(change.NeedsRestart);
    }

    [Fact]
    public async Task Never_notify_is_put_back_to_Windows_normal_level()
    {
        _changer.Set(Uac("EnableLUA"), "1").Set(Uac("ConsentPromptBehaviorAdmin"), "0");

        var change = Assert.Single(await PlanAsync(new UacOnFix(_changer)));

        Assert.Equal("ConsentPromptBehaviorAdmin", change.Target);
        Assert.Equal("5", change.To);
        Assert.False(change.NeedsRestart);
    }

    [Fact]
    public async Task Permission_prompt_values_that_arent_set_are_left_alone()
    {
        // Not set means Windows' default, which is on.
        Assert.Empty(await PlanAsync(new UacOnFix(_changer)));
    }

    [Fact]
    public async Task Remote_Desktop_off_can_be_undone_in_the_app()
    {
        var change = Assert.Single(await PlanAsync(new RemoteDesktopOffFix(_changer)));

        Assert.Equal("1", change.To);
        Assert.Null(change.From);
        Assert.Equal(UndoSupport.InApp, change.Undo);
        Assert.Equal("Turn Remote Desktop back on", change.UndoDescription);
    }

    [Fact]
    public async Task SMB1_off_needs_a_restart_and_can_be_undone()
    {
        var feature = new SettingChange { Key = "k", Kind = ChangeKind.WindowsFeature, Target = "SMB1Protocol" };
        _changer.Set(feature, "Enabled");

        var change = Assert.Single(await PlanAsync(new Smb1OffFix(_changer)));

        Assert.Equal("Disabled", change.To);
        Assert.True(change.NeedsRestart);
        Assert.Equal(UndoSupport.InApp, change.Undo);
    }

    [Fact]
    public async Task System_Restore_is_turned_on_for_the_Windows_drive()
    {
        var change = Assert.Single(await PlanAsync(new SystemRestoreOnFix(_changer, "C:")));

        Assert.Equal(@"C:\", change.Target);
        Assert.Equal("on", change.To);
    }

    [Fact]
    public async Task Windows_Update_goes_back_to_Manual_which_is_normal()
    {
        _changer.Set(new SettingChange { Key = "k", Kind = ChangeKind.ServiceStart, Target = "wuauserv" }, "Disabled");

        var change = Assert.Single(await PlanAsync(new WindowsUpdateServiceFix(_changer)));

        Assert.Equal("Manual", change.To);
    }

    [Fact]
    public async Task Defender_real_time_protection_is_turned_on()
    {
        _changer.Set(new SettingChange { Key = "k", Kind = ChangeKind.DefenderRealTime, Target = "RealTimeProtection" }, "off");

        Assert.Equal("on", Assert.Single(await PlanAsync(new DefenderRealTimeOnFix(_changer))).To);
    }

    [Theory]
    [InlineData(QuickScanFix.FixId)]
    [InlineData(UpdateVirusInfoFix.FixId)]
    public async Task Scans_and_updates_are_one_task_with_nothing_to_undo(string id)
    {
        var fix = FixSet.Create(_changer, "C:").Single(f => f.Id == id);

        var change = Assert.Single(await PlanAsync(fix));

        Assert.Equal(ChangeKind.Task, change.Kind);
        Assert.Equal(UndoSupport.NotNeeded, change.Undo);
        Assert.False(string.IsNullOrWhiteSpace(change.Description));
    }

    // Trust rule 6, checked across every fix: whatever a fix plans never weakens protection, and every
    // change marked "undo in Windows Settings" really is one the guard would refuse to reverse.
    [Fact]
    public async Task No_fix_ever_plans_a_weakening_change_and_undo_routes_match_the_guard()
    {
        var everythingOff = new FakeChanger();
        foreach (var profile in new[] { "Domain", "Private", "Public" })
        {
            everythingOff.Set(Firewall(profile), "off");
        }

        everythingOff.Set(Uac("EnableLUA"), "0").Set(Uac("ConsentPromptBehaviorAdmin"), "0")
            .Set(new SettingChange { Key = "k", Kind = ChangeKind.DefenderRealTime, Target = "RealTimeProtection" }, "off")
            .Set(new SettingChange { Key = "k", Kind = ChangeKind.ServiceStart, Target = "wuauserv" }, "Disabled")
            .Set(new SettingChange { Key = "k", Kind = ChangeKind.WindowsFeature, Target = "SMB1Protocol" }, "Enabled")
            .Set(new SettingChange { Key = "k", Kind = ChangeKind.SystemRestore, Target = @"C:\" }, "off");

        foreach (var fix in FixSet.Create(everythingOff, "C:"))
        {
            foreach (var change in await fix.PlanAsync(Catalog[fix.Id], CancellationToken.None))
            {
                Assert.False(ProtectionGuard.WouldWeaken(change), $"{fix.Id} plans a weakening change: {change.TechnicalDetail}");

                if (change.Undo == UndoSupport.ViaSettings)
                {
                    Assert.True(ProtectionGuard.WouldWeaken(change.Reversed()), $"{fix.Id}: undo goes via Settings but the guard would allow it in the app");
                    Assert.NotNull(change.UndoPage);
                }

                if (change.Undo == UndoSupport.InApp)
                {
                    Assert.False(ProtectionGuard.WouldWeaken(change.Reversed()), $"{fix.Id}: undo is promised in the app but the guard would block it");
                }
            }
        }
    }
}

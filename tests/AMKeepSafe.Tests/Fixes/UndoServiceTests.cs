using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Undo;
using AMKeepSafe.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace AMKeepSafe.Tests.Fixes;

public class UndoServiceTests
{
    private static readonly FixCatalog Catalog = FixCatalog.LoadEmbedded();

    private readonly FakeChanger _changer = new();
    private readonly MemoryJournal _journal = new();
    private readonly MemoryLog _log = new();
    private readonly FakeTimeProvider _time = new(HealthyPc.Now);
    private readonly TempDirectory _backups = new();

    private FixRunner Runner(bool dryRun = false) => new(_changer, _journal, _log, _time, dryRun, _backups.Path);

    private UndoService Service(bool dryRun = false) => new(Runner(dryRun), _changer, _journal, Catalog.Common, _log, _time);

    /// <summary>Runs a real fix end to end, as the app would, so undo has something genuine to work on.</summary>
    private async Task FixAsync(IFix fix)
    {
        var definition = Catalog[fix.Id];
        var preview = FixPreview.ForFix(Catalog.Common, definition, await fix.PlanAsync(definition, CancellationToken.None), isDryRun: false);
        preview.Confirm();
        await Runner().ApplyAsync(preview);
    }

    private static SettingChange RemoteDesktopSetting => new()
    {
        Key = "remoteDesktopOff", Kind = ChangeKind.RegistryValue, Root = AMKeepSafe.Core.Platform.RegistryRoot.LocalMachine,
        RegistryKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server", Target = "fDenyTSConnections",
    };

    [Fact]
    public async Task Remote_Desktop_off_then_undo_puts_it_back_exactly()
    {
        _changer.Set(RemoteDesktopSetting, "0");
        await FixAsync(new RemoteDesktopOffFix(_changer));
        Assert.Equal("1", _changer.Get(RemoteDesktopSetting));

        var entry = Assert.Single(Service().Entries());
        var preview = await Service().PreviewAsync(entry);
        Assert.NotNull(preview);
        Assert.Equal("Turn Remote Desktop back on", preview!.Items[0].Description);
        Assert.Equal(Catalog.Common.UndoConfirmButton, preview.ConfirmButton);
        preview.Confirm();

        var outcome = await Service().UndoAsync(entry, preview);

        Assert.Equal(UndoOutcome.Done, outcome);
        Assert.Equal("0", _changer.Get(RemoteDesktopSetting));
        Assert.True(Assert.Single(Service().Entries()).IsUndone);
        Assert.True(_log.Has(LogLevel.Info, "Undone: Turn off Remote Desktop"));
    }

    [Fact]
    public async Task A_value_that_wasnt_set_before_is_removed_again_on_undo()
    {
        await FixAsync(new RemoteDesktopOffFix(_changer));
        var entry = Service().Entries()[0];
        var preview = (await Service().PreviewAsync(entry))!;
        preview.Confirm();

        await Service().UndoAsync(entry, preview);

        Assert.Null(_changer.Get(RemoteDesktopSetting));
        Assert.Null(_changer.Applied[^1].To);
    }

    // Trust rule 6: protection switched on is never switched off by the app.
    [Fact]
    public async Task Turning_the_firewall_on_cant_be_undone_in_the_app()
    {
        SettingChange Profile(string name) => new() { Key = "k", Kind = ChangeKind.FirewallProfile, Target = name };
        _changer.Set(Profile("Domain"), "on").Set(Profile("Private"), "on").Set(Profile("Public"), "off");
        await FixAsync(new FirewallOnFix(_changer));

        var entry = Assert.Single(Service().Entries());

        Assert.False(entry.CanUndoInApp);
        Assert.Null(await Service().PreviewAsync(entry));
    }

    [Fact]
    public async Task Even_a_forged_preview_cant_switch_protection_off()
    {
        var firewallOn = new SettingChange { Key = "public", Kind = ChangeKind.FirewallProfile, Target = "Public", From = "off", To = "on", Undo = UndoSupport.InApp, Description = "on", UndoDescription = "off" };
        _changer.Set(firewallOn, "on");
        var entry = new UndoEntry { Id = Guid.NewGuid(), When = HealthyPc.Now, Kind = UndoKind.SettingChanged, DisplayName = "Firewall", Data = firewallOn.ToUndoData(), CanUndoInApp = true };
        _journal.Entries.Add(entry);
        var preview = (await Service().PreviewAsync(entry))!;
        preview.Confirm();

        var outcome = await Service().UndoAsync(entry, preview);

        Assert.Equal(UndoOutcome.Failed, outcome);
        Assert.Equal("on", _changer.Get(firewallOn));
    }

    [Fact]
    public async Task Undo_needs_a_confirmed_preview()
    {
        _changer.Set(RemoteDesktopSetting, "0");
        await FixAsync(new RemoteDesktopOffFix(_changer));
        var entry = Service().Entries()[0];
        var preview = (await Service().PreviewAsync(entry))!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().UndoAsync(entry, preview));
        Assert.Equal("1", _changer.Get(RemoteDesktopSetting));
    }

    [Fact]
    public async Task Says_so_when_the_setting_has_been_changed_since()
    {
        _changer.Set(RemoteDesktopSetting, "0");
        await FixAsync(new RemoteDesktopOffFix(_changer));
        _changer.Set(RemoteDesktopSetting, "7");

        var preview = (await Service().PreviewAsync(Service().Entries()[0]))!;

        Assert.Equal(Catalog.Common.ChangedSinceNote, preview.Items[0].UndoNote);
        Assert.Equal("7", preview.Changes[0].From);
        Assert.Equal("0", preview.Changes[0].To);
    }

    [Fact]
    public async Task A_failed_undo_isnt_marked_as_undone()
    {
        _changer.Set(RemoteDesktopSetting, "0");
        await FixAsync(new RemoteDesktopOffFix(_changer));
        var entry = Service().Entries()[0];
        var preview = (await Service().PreviewAsync(entry))!;
        preview.Confirm();
        _changer.Refuse = true;

        Assert.Equal(UndoOutcome.Failed, await Service().UndoAsync(entry, preview));
        Assert.False(Service().Entries()[0].IsUndone);
    }

    [Fact]
    public async Task Dry_run_undo_changes_nothing()
    {
        _changer.Set(RemoteDesktopSetting, "0");
        await FixAsync(new RemoteDesktopOffFix(_changer));
        var entry = Service(dryRun: true).Entries()[0];
        var preview = (await Service(dryRun: true).PreviewAsync(entry))!;
        preview.Confirm();

        Assert.Equal(UndoOutcome.DryRun, await Service(dryRun: true).UndoAsync(entry, preview));
        Assert.Equal("1", _changer.Get(RemoteDesktopSetting));
        Assert.True(preview.IsDryRun);
    }

    [Fact]
    public async Task An_entry_already_undone_cant_be_undone_twice()
    {
        _changer.Set(RemoteDesktopSetting, "0");
        await FixAsync(new RemoteDesktopOffFix(_changer));
        _journal.MarkUndone(_journal.Entries[0].Id, HealthyPc.Now);

        Assert.Null(await Service().PreviewAsync(Service().Entries()[0]));
    }

    [Fact]
    public void The_list_is_newest_first_and_leaves_out_changes_that_never_happened()
    {
        UndoEntry Entry(string name, int minutes, bool notMade = false) => new()
        {
            Id = Guid.NewGuid(), When = HealthyPc.Now.AddMinutes(minutes), Kind = UndoKind.SettingChanged, DisplayName = name, NotMade = notMade,
        };
        _journal.Entries.AddRange([Entry("first", 0), Entry("refused", 5, notMade: true), Entry("second", 10)]);

        Assert.Equal(["second", "first"], Service().Entries().Select(e => e.DisplayName));
    }
}

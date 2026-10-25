using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Core.Undo;
using AMKeepSafe.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace AMKeepSafe.Tests.Fixes;

public class FixRunnerTests
{
    private static readonly FixCatalog Catalog = FixCatalog.LoadEmbedded();

    private static readonly SettingChange RemoteDesktopOff = new()
    {
        Key = "remoteDesktopOff", Kind = ChangeKind.RegistryValue, Root = RegistryRoot.LocalMachine,
        RegistryKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server", Target = "fDenyTSConnections",
        From = "0", To = "1", Description = "Turn off Remote Desktop", UndoDescription = "Turn Remote Desktop back on", Undo = UndoSupport.InApp,
    };

    private static readonly SettingChange FirewallPublicOn = new()
    {
        Key = "public", Kind = ChangeKind.FirewallProfile, Target = "Public", From = "off", To = "on",
        Description = "Turn the firewall on for public networks", Undo = UndoSupport.ViaSettings,
    };

    private static readonly SettingChange Scan = new() { Key = "scan", Kind = ChangeKind.Task, Target = "QuickScan", Undo = UndoSupport.NotNeeded, Description = "Scan" };

    private readonly FakeChanger _changer = new FakeChanger().Set(RemoteDesktopOff, "0").Set(FirewallPublicOn, "off");
    private readonly MemoryJournal _journal = new();
    private readonly MemoryLog _log = new();
    private readonly FakeTimeProvider _time = new(HealthyPc.Now);
    private readonly TempDirectory _backups = new();

    private FixRunner Runner(bool dryRun = false) => new(_changer, _journal, _log, _time, dryRun, _backups.Path);

    private static FixPreview Confirmed(params SettingChange[] changes)
    {
        var preview = FixPreview.ForFix(Catalog.Common, Catalog["remote-desktop-off"], changes, isDryRun: false);
        preview.Confirm();
        return preview;
    }

    // Trust rule 4.
    [Fact]
    public async Task Refuses_to_change_anything_without_a_confirmed_preview()
    {
        var preview = FixPreview.ForFix(Catalog.Common, Catalog["remote-desktop-off"], [RemoteDesktopOff], isDryRun: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner().ApplyAsync(preview));
        Assert.Empty(_changer.Applied);
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Makes_the_change_and_reads_it_back()
    {
        var result = await Runner().ApplyAsync(Confirmed(RemoteDesktopOff));

        Assert.Equal(FixOutcome.Done, result.Outcome);
        Assert.Equal("1", _changer.Get(RemoteDesktopOff));
        Assert.Equal([RemoteDesktopOff], result.Made);
    }

    // Trust rule 5.
    [Fact]
    public async Task Records_the_old_value_before_the_change_is_made()
    {
        var recordedFirst = false;
        _changer.BeforeApply = _ => recordedFirst = _journal.Entries.Count == 1;

        await Runner().ApplyAsync(Confirmed(RemoteDesktopOff));

        Assert.True(recordedFirst);
        var entry = Assert.Single(_journal.Entries);
        Assert.Equal("Turn off Remote Desktop", entry.DisplayName);
        Assert.Equal(UndoKind.RegistryValueChanged, entry.Kind);
        Assert.Equal("0", entry.Data["from"]);
        Assert.True(entry.CanUndoInApp);
        Assert.Equal(HealthyPc.Now, entry.When);
    }

    [Fact]
    public async Task If_the_old_value_cant_be_recorded_nothing_is_changed()
    {
        _journal.FailToSave = true;

        var result = await Runner().ApplyAsync(Confirmed(RemoteDesktopOff));

        Assert.Equal(FixOutcome.Failed, result.Outcome);
        Assert.Empty(_changer.Applied);
        Assert.Equal("0", _changer.Get(RemoteDesktopOff));
        Assert.True(_log.Has(LogLevel.Error, "Couldn't record the old value"));
    }

    [Fact]
    public async Task Protection_switched_on_is_recorded_but_not_undoable_in_the_app()
    {
        await Runner().ApplyAsync(Confirmed(FirewallPublicOn));

        var entry = Assert.Single(_journal.Entries);
        Assert.False(entry.CanUndoInApp);
        Assert.Equal(UndoKind.SettingChanged, entry.Kind);
    }

    [Fact]
    public async Task Tasks_like_a_scan_arent_put_on_the_undo_list()
    {
        var result = await Runner().ApplyAsync(Confirmed(Scan));

        Assert.Equal(FixOutcome.Done, result.Outcome);
        Assert.Single(_changer.Applied);
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task When_Windows_refuses_the_entry_is_marked_not_made()
    {
        _changer.Refuse = true;

        var result = await Runner().ApplyAsync(Confirmed(RemoteDesktopOff));

        Assert.Equal(FixOutcome.Failed, result.Outcome);
        Assert.True(Assert.Single(_journal.Entries).NotMade);
        Assert.True(_log.Has(LogLevel.Error, "didn't go through"));
    }

    [Fact]
    public async Task A_change_that_quietly_doesnt_stick_is_caught_by_reading_it_back()
    {
        // Tamper Protection does this to Defender settings.
        _changer.Ignore = true;

        var result = await Runner().ApplyAsync(Confirmed(RemoteDesktopOff));

        Assert.Equal(FixOutcome.Failed, result.Outcome);
        Assert.True(Assert.Single(_journal.Entries).NotMade);
    }

    [Fact]
    public async Task Some_done_and_some_not_is_partly_done()
    {
        var refusesFirewall = new RefusingChanger(_changer, ChangeKind.FirewallProfile);
        var runner = new FixRunner(refusesFirewall, _journal, _log, _time, dryRun: false, _backups.Path);

        var result = await runner.ApplyAsync(Confirmed(RemoteDesktopOff, FirewallPublicOn));

        Assert.Equal(FixOutcome.PartlyDone, result.Outcome);
        Assert.Equal([RemoteDesktopOff], result.Made);
        Assert.Equal([FirewallPublicOn], result.NotMade);
    }

    // Trust rule 6.
    [Fact]
    public async Task Refuses_a_change_that_would_weaken_protection_even_if_asked()
    {
        var firewallOff = FirewallPublicOn with { From = "on", To = "off" };

        var result = await Runner().ApplyAsync(Confirmed(firewallOff));

        Assert.Equal(FixOutcome.Failed, result.Outcome);
        Assert.Empty(_changer.Applied);
        Assert.Empty(_journal.Entries);
        Assert.True(_log.Has(LogLevel.Error, "Refused a change that would weaken protection"));
    }

    // Trust rule 11.
    [Fact]
    public async Task Dry_run_changes_nothing_records_nothing_and_logs_what_it_would_do()
    {
        var result = await Runner(dryRun: true).ApplyAsync(Confirmed(RemoteDesktopOff, Scan));

        Assert.Equal(FixOutcome.DryRun, result.Outcome);
        Assert.Empty(_changer.Applied);
        Assert.Empty(_journal.Entries);
        Assert.True(_log.Has(LogLevel.Info, "Would change: Turn off Remote Desktop."));
        Assert.True(_log.Has(LogLevel.Info, "fDenyTSConnections: from 0 to 1"));
    }

    // Trust rule 9.
    [Fact]
    public async Task Every_step_is_logged()
    {
        await Runner().ApplyAsync(Confirmed(RemoteDesktopOff));

        Assert.True(_log.Has(LogLevel.Info, "Changing: Turn off Remote Desktop."));
        Assert.True(_log.Has(LogLevel.Info, "Done: Turn off Remote Desktop."));
    }

    private sealed class RefusingChanger(ISystemChanger inner, ChangeKind refuse) : ISystemChanger
    {
        public Task<string> BackupAsync(SettingChange change, string folder, CancellationToken cancellationToken) => inner.BackupAsync(change, folder, cancellationToken);

        public Task<string?> ReadAsync(SettingChange change, CancellationToken cancellationToken) => inner.ReadAsync(change, cancellationToken);

        public Task ApplyAsync(SettingChange change, CancellationToken cancellationToken) =>
            change.Kind == refuse ? throw new InvalidOperationException("No.") : inner.ApplyAsync(change, cancellationToken);
    }
}

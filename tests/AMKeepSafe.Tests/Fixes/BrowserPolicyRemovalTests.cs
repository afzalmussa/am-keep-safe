using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Core.Undo;
using AMKeepSafe.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace AMKeepSafe.Tests.Fixes;

/// <summary>Removing "Managed by your organisation" settings: always backed up first, always undoable (trust rule 5).</summary>
public sealed class BrowserPolicyRemovalTests : IDisposable
{
    private const string Chrome = @"SOFTWARE\Policies\Google\Chrome";
    private static readonly FixCatalog Catalog = FixCatalog.LoadEmbedded();

    private readonly BrowserFolders _folders = new();
    private readonly FakeChanger _changer = new();
    private readonly MemoryJournal _journal = new();
    private readonly MemoryLog _log = new();
    private readonly FakeTimeProvider _time = new(HealthyPc.Now);
    private readonly TempDirectory _backups = new();

    public void Dispose()
    {
        _folders.Dispose();
        _backups.Dispose();
    }

    private FixRunner Runner(bool dryRun = false) => new(_changer, _journal, _log, _time, dryRun, _backups.Path);

    private UndoService Undo() => new(Runner(), _changer, _journal, Catalog.Common, _log, _time);

    private static SettingChange ChromeKey(RegistryRoot root = RegistryRoot.LocalMachine) => new()
    {
        Key = root == RegistryRoot.LocalMachine ? "machine" : "account", Kind = ChangeKind.RegistryKey, Root = root, RegistryKey = Chrome, Target = "*",
        From = SettingChange.Present, To = SettingChange.Absent, Contents = ["HomepageLocation = https://hijack.example"],
        Description = "Remove the settings", UndoDescription = "Put back the settings", Undo = UndoSupport.InApp,
    };

    private async Task<IReadOnlyList<SettingChange>> PlanAsync(Browser browser, FakeRegistry registry)
    {
        var fix = new RemoveBrowserPoliciesFix(browser, new BrowserPolicyReader(registry, _folders.Locations));
        return await fix.PlanAsync(Catalog[fix.Id], CancellationToken.None);
    }

    private async Task<FixRunResult> RemoveAsync(params SettingChange[] changes)
    {
        var preview = FixPreview.ForFix(Catalog.Common, Catalog[RemoveBrowserPoliciesFix.IdFor(Browser.Chrome)], changes, isDryRun: false);
        preview.Confirm();
        return await Runner().ApplyAsync(preview);
    }

    [Fact]
    public async Task Plans_one_removal_for_each_place_settings_are_found_and_shows_what_is_in_them()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, Chrome, "HomepageLocation", "https://hijack.example")
            .Set(RegistryRoot.CurrentUser, Chrome, "DefaultSearchProviderName", "Hijack");

        var changes = await PlanAsync(Browser.Chrome, registry);

        Assert.Equal(["machine", "account"], changes.Select(c => c.Key));
        Assert.All(changes, c =>
        {
            Assert.Equal(ChangeKind.RegistryKey, c.Kind);
            Assert.Equal(Chrome, c.RegistryKey);
            Assert.True(c.IsRemoval);
            Assert.Equal(UndoSupport.InApp, c.Undo);
            Assert.True(ProtectionGuard.Allows(c));
        });
        Assert.Equal(["HomepageLocation = https://hijack.example"], changes[0].Contents);
        Assert.Contains("HomepageLocation = https://hijack.example", changes[0].TechnicalDetail, StringComparison.Ordinal);
        Assert.Contains(@"HKLM\SOFTWARE\Policies\Google\Chrome", changes[0].TechnicalDetail, StringComparison.Ordinal);
        Assert.Equal("Remove the settings controlling Google Chrome for everyone on this PC", changes[0].Description);
    }

    [Fact]
    public async Task Firefox_policies_json_is_planned_as_a_file_removal()
    {
        var distribution = Path.Combine(_folders.FirefoxInstall, "distribution");
        Directory.CreateDirectory(distribution);
        File.WriteAllText(Path.Combine(distribution, "policies.json"), """{ "policies": { "Homepage": { "URL": "https://hijack.example" } } }""");

        var change = Assert.Single(await PlanAsync(Browser.Firefox, new FakeRegistry()));

        Assert.Equal("file", change.Key);
        Assert.Equal(ChangeKind.File, change.Kind);
        Assert.Equal(Path.Combine(distribution, "policies.json"), change.Target);
        Assert.True(ProtectionGuard.Allows(change));
    }

    [Fact]
    public async Task Nothing_to_remove_plans_nothing()
    {
        Assert.Empty(await PlanAsync(Browser.Edge, new FakeRegistry()));
    }

    [Fact]
    public async Task A_copy_is_kept_before_anything_is_removed_and_the_undo_list_knows_where_it_is()
    {
        _changer.Set(ChromeKey(), SettingChange.Present);
        var backedUpFirst = false;
        _changer.BeforeApply = _ => backedUpFirst = _changer.Backups.Count == 1;

        var result = await RemoveAsync(ChromeKey());

        Assert.Equal(FixOutcome.Done, result.Outcome);
        Assert.True(backedUpFirst);
        var backup = Assert.Single(_changer.Backups);
        Assert.StartsWith(_backups.Path, backup, StringComparison.Ordinal);
        var entry = Assert.Single(_journal.Entries);
        Assert.Equal(backup, entry.BackupFile);
        Assert.Equal(backup, entry.Data["backupFile"]);
        Assert.True(entry.CanUndoInApp);
        Assert.True(_log.Has(LogLevel.Info, "Kept a copy before removing it"));
    }

    [Fact]
    public async Task If_a_copy_cant_be_kept_nothing_is_removed()
    {
        _changer.Set(ChromeKey(), SettingChange.Present);
        _changer.FailBackup = true;

        var result = await RemoveAsync(ChromeKey());

        Assert.Equal(FixOutcome.Failed, result.Outcome);
        Assert.Empty(_changer.Applied);
        Assert.Empty(_journal.Entries);
        Assert.Equal(SettingChange.Present, _changer.Get(ChromeKey()));
        Assert.True(_log.Has(LogLevel.Error, "Couldn't keep a copy, so this wasn't removed"));
    }

    [Fact]
    public async Task Dry_run_doesnt_even_make_a_copy()
    {
        var preview = FixPreview.ForFix(Catalog.Common, Catalog[RemoveBrowserPoliciesFix.IdFor(Browser.Chrome)], [ChromeKey()], isDryRun: true);
        preview.Confirm();

        var result = await Runner(dryRun: true).ApplyAsync(preview);

        Assert.Equal(FixOutcome.DryRun, result.Outcome);
        Assert.Empty(_changer.Backups);
        Assert.Empty(_changer.Applied);
    }

    [Fact]
    public async Task Undo_puts_the_settings_back_from_the_copy()
    {
        _changer.Set(ChromeKey(), SettingChange.Present);
        await RemoveAsync(ChromeKey());
        Assert.Equal(SettingChange.Absent, _changer.Get(ChromeKey()));

        var entry = Assert.Single(Undo().Entries());
        var preview = (await Undo().PreviewAsync(entry))!;
        Assert.Equal("Put back the settings", preview.Items[0].Description);
        preview.Confirm();

        Assert.Equal(UndoOutcome.Done, await Undo().UndoAsync(entry, preview));
        Assert.Equal(SettingChange.Present, _changer.Get(ChromeKey()));
        var restore = _changer.Applied[^1];
        Assert.Equal(SettingChange.Present, restore.To);
        Assert.Equal(entry.BackupFile, restore.BackupFile);
        Assert.Single(_changer.Backups);
    }

    [Fact]
    public async Task Undo_fails_safely_if_the_copy_has_gone()
    {
        _changer.Set(ChromeKey(), SettingChange.Present);
        await RemoveAsync(ChromeKey());
        var entry = Undo().Entries()[0];
        File.Delete(entry.BackupFile!);
        var preview = (await Undo().PreviewAsync(entry))!;
        preview.Confirm();

        Assert.Equal(UndoOutcome.Failed, await Undo().UndoAsync(entry, preview));
        Assert.False(Undo().Entries()[0].IsUndone);
    }

    // Trust rule 6: the power to remove a whole key or file reaches the browser policy locations and nothing else.
    [Theory]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Edge")]
    [InlineData(@"software\policies\google\chrome")]
    [InlineData(@"SOFTWARE\Policies\Mozilla\Firefox")]
    public void Only_the_browser_policy_keys_can_be_removed(string key)
    {
        Assert.True(ProtectionGuard.Allows(ChromeKey() with { RegistryKey = key }));
        Assert.True(ProtectionGuard.Allows(ChromeKey(RegistryRoot.CurrentUser) with { RegistryKey = key }));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows Defender")]
    [InlineData(@"SOFTWARE\Policies\Microsoft")]
    [InlineData(@"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\WinDefend")]
    [InlineData(null)]
    public void Any_other_key_is_refused(string? key)
    {
        Assert.False(ProtectionGuard.Allows(ChromeKey() with { RegistryKey = key }));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Firefox\distribution\policies.json", true)]
    [InlineData(@"C:\Program Files (x86)\Mozilla Firefox\distribution\policies.json", true)]
    [InlineData(@"C:\Program Files\Mozilla Firefox\distribution\..\..\..\Windows\System32\drivers\etc\hosts", false)]
    [InlineData(@"C:\Program Files\Mozilla Firefox\..\..\Windows\distribution\policies.json", false)]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe", false)]
    [InlineData(@"C:\Windows\distribution\policies.json", false)]
    public void Only_Firefoxs_policies_json_can_be_removed(string path, bool allowed)
    {
        var change = new SettingChange { Key = "file", Kind = ChangeKind.File, Target = path, From = SettingChange.Present, To = SettingChange.Absent };

        Assert.Equal(allowed, ProtectionGuard.Allows(change));
    }

    [Fact]
    public async Task A_refused_key_is_never_backed_up_or_touched()
    {
        var run = ChromeKey() with { RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" };

        var result = await RemoveAsync(run);

        Assert.Equal(FixOutcome.Failed, result.Outcome);
        Assert.Empty(_changer.Backups);
        Assert.Empty(_changer.Applied);
    }

    [Fact]
    public void Removal_details_survive_the_trip_through_the_undo_list()
    {
        var change = ChromeKey() with { BackupFile = @"C:\ProgramData\AMSoftware\KeepSafe\backups\copy.json", Contents = ["A = 1", "B = two"] };

        var back = SettingChange.FromUndoData(change.ToUndoData())!;

        Assert.Equal(change.BackupFile, back.BackupFile);
        Assert.Equal(change.Contents, back.Contents);
        Assert.Equal(ChangeKind.RegistryKey, back.Kind);
        Assert.Equal(Chrome, back.RegistryKey);
        Assert.True(back.IsRemoval);
        Assert.False(back.Reversed().IsRemoval);
        Assert.Equal(change.BackupFile, back.Reversed().BackupFile);
    }

    [Fact]
    public void Putting_something_back_isnt_a_removal()
    {
        Assert.False((ChromeKey() with { From = SettingChange.Absent, To = SettingChange.Present }).IsRemoval);
        Assert.False(new SettingChange { Key = "k", Kind = ChangeKind.RegistryValue, Target = "x", To = SettingChange.Absent }.IsRemoval);
    }
}

public class RegistryKeyBackupTests
{
    [Fact]
    public void Round_trips_every_kind_of_value_and_sub_keys_exactly()
    {
        var backup = new RegistryKeyBackup
        {
            Root = RegistryRoot.LocalMachine,
            Path = @"SOFTWARE\Policies\Google\Chrome",
            Values =
            [
                new RegistryValueBackup("HomepageLocation", "String", "https://hijack.example"),
                new RegistryValueBackup("Path", "ExpandString", @"%ProgramFiles%\x"),
                new RegistryValueBackup("Lines", "MultiString", null, ["one", "two"]),
                new RegistryValueBackup("DefaultSearchProviderEnabled", "DWord", "1"),
                new RegistryValueBackup("Big", "QWord", "9223372036854775807"),
                new RegistryValueBackup("Blob", "Binary", Convert.ToBase64String([0, 1, 254, 255])),
            ],
            SubKeys =
            [
                new RegistryKeyBackup
                {
                    Path = "ExtensionInstallForcelist",
                    Values = [new RegistryValueBackup("1", "String", "aaaabbbbccccddddeeeeffffgggghhhh;https://clients2.google.com/service/update2/crx")],
                },
            ],
        };

        var back = RegistryKeyBackup.FromJson(backup.ToJson());

        Assert.Equal(backup.Root, back.Root);
        Assert.Equal(backup.Path, back.Path);
        Assert.Equal(backup.Values.Count, back.Values.Count);
        for (var i = 0; i < backup.Values.Count; i++)
        {
            Assert.Equal(backup.Values[i].Name, back.Values[i].Name);
            Assert.Equal(backup.Values[i].Kind, back.Values[i].Kind);
            Assert.Equal(backup.Values[i].Data, back.Values[i].Data);
            Assert.Equal(backup.Values[i].Strings ?? [], back.Values[i].Strings ?? []);
        }

        var sub = Assert.Single(back.SubKeys);
        Assert.Equal("ExtensionInstallForcelist", sub.Path);
        Assert.Equal(backup.SubKeys[0].Values[0], Assert.Single(sub.Values));
    }

    [Fact]
    public void The_copy_is_readable_text()
    {
        var json = new RegistryKeyBackup { Root = RegistryRoot.CurrentUser, Path = @"SOFTWARE\Policies\Microsoft\Edge" }.ToJson();

        Assert.Contains("\"root\": \"CurrentUser\"", json, StringComparison.Ordinal);
        Assert.Contains("\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_copy_is_reported()
    {
        Assert.ThrowsAny<Exception>(() => RegistryKeyBackup.FromJson("null"));
    }

    [Fact]
    public void File_names_start_with_the_time_and_contain_only_safe_characters()
    {
        var name = RegistryKeyBackup.FileNameFor(new DateTimeOffset(2026, 10, 25, 9, 30, 15, TimeSpan.Zero), @"HKLM\SOFTWARE\Policies\Google\Chrome");

        Assert.Matches(@"^\d{8}-\d{6}-HKLM-SOFTWARE-Policies-Google-Chrome$", name);
    }

    [Fact]
    public void Very_long_names_are_shortened()
    {
        var name = RegistryKeyBackup.FileNameFor(HealthyPc.Now, new string('a', 300));

        Assert.Equal(16 + 80, name.Length);
    }
}

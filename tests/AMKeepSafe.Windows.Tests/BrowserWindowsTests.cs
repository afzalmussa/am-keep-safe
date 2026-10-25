using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Windows.Browsers;
using AMKeepSafe.Windows.Fixes;
using AMKeepSafe.Windows.Platform;
using Microsoft.Win32;

namespace AMKeepSafe.Windows.Tests;

/// <summary>The Browser check's Windows parts, against a real PC. Reads only.</summary>
public class BrowserReadOnlyTests
{
    private readonly ListLog _log = new();

    [Fact]
    public async Task Works_out_who_is_signed_in_and_whether_this_is_a_work_PC()
    {
        var context = await new PcContextInfo(new PowerShellRunner(_log)).GetAsync(CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(context.RunningAs));
        Assert.Contains(Environment.UserName, context.RunningAs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Can_tell_whether_each_browser_is_open()
    {
        var processes = new WindowsBrowserProcesses(_log);

        foreach (var browser in Enum.GetValues<Browser>())
        {
            _ = processes.IsRunning(browser);
        }

        Assert.Equal("msedge", WindowsBrowserProcesses.ProcessName(Browser.Edge));
        Assert.Equal("chrome", WindowsBrowserProcesses.ProcessName(Browser.Chrome));
        Assert.Equal("firefox", WindowsBrowserProcesses.ProcessName(Browser.Firefox));
    }

    /// <summary>The Browser check as the app builds it, for this account on this PC.</summary>
    internal static BrowserCheckRunner Runner(ListLog log)
    {
        var locations = BrowserLocations.ForCurrentUser();
        return new BrowserCheckRunner(
            new ChromiumReader(locations),
            new FirefoxReader(locations),
            new BrowserPolicyReader(new WindowsRegistryReader(), locations),
            new PcContextInfo(new PowerShellRunner(log)),
            SearchEngineList.LoadEmbedded(),
            KnownBadExtensionList.LoadEmbedded(),
            BrowserText.LoadEmbedded(),
            log);
    }

    [Fact]
    public async Task The_whole_browser_check_runs_on_a_real_PC()
    {
        var report = await Runner(_log).RunAsync();

        // GitHub's Windows runners have Edge and Chrome installed, but they may never have been opened.
        Assert.All(report.AllResults, r => Assert.False(string.IsNullOrWhiteSpace(r.Summary)));
        Assert.True(report.NoBrowsers ? report.Note is not null : report.Sections.All(s => s.Results.Count > 0));
    }
}

/// <summary>
/// Removing and restoring a whole key or file, for real. Works only on a scratch key under this
/// account's own registry and a temporary folder, both removed at the end: nothing the PC uses is touched.
/// </summary>
public sealed class RemoveAndRestoreTests : IDisposable
{
    private readonly string _scratchKey = $@"Software\AMKeepSafe.Tests\{Guid.NewGuid():N}";
    private readonly string _folder = Directory.CreateTempSubdirectory("amkeepsafe-windows-tests-").FullName;
    private readonly ListLog _log = new();

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_scratchKey, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\AMKeepSafe.Tests", writable: true))
        {
            if (parent is { SubKeyCount: 0, ValueCount: 0 })
            {
                Registry.CurrentUser.DeleteSubKey(@"Software\AMKeepSafe.Tests", throwOnMissingSubKey: false);
            }
        }

        Directory.Delete(_folder, recursive: true);
    }

    private WindowsSystemChanger Changer() => new(new PowerShellRunner(_log), new WindowsRegistryReader());

    private SettingChange RemoveKey => new()
    {
        Key = "account", Kind = ChangeKind.RegistryKey, Root = RegistryRoot.CurrentUser, RegistryKey = _scratchKey, Target = "*",
        From = SettingChange.Present, To = SettingChange.Absent, Description = "Remove", Undo = UndoSupport.InApp,
    };

    [Fact]
    public async Task A_removed_key_comes_back_exactly_with_every_kind_of_value()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_scratchKey, writable: true))
        {
            key.SetValue("Text", "https://hijack.example", RegistryValueKind.String);
            key.SetValue("Expand", @"%ProgramFiles%\Thing", RegistryValueKind.ExpandString);
            key.SetValue("Lines", new[] { "one", "two" }, RegistryValueKind.MultiString);
            key.SetValue("Number", unchecked((int)0xFFFFFFFE), RegistryValueKind.DWord);
            key.SetValue("Big", long.MaxValue, RegistryValueKind.QWord);
            key.SetValue("Blob", new byte[] { 0, 1, 254, 255 }, RegistryValueKind.Binary);
            using var sub = key.CreateSubKey(@"ExtensionInstallForcelist\Deeper", writable: true);
            sub.SetValue("1", "aaaabbbbccccddddeeeeffffgggghhhh");
        }

        var changer = Changer();
        var backup = await changer.BackupAsync(RemoveKey, _folder, CancellationToken.None);
        await changer.ApplyAsync(RemoveKey, CancellationToken.None);
        Assert.Equal(SettingChange.Absent, await changer.ReadAsync(RemoveKey, CancellationToken.None));

        var restore = RemoveKey.Reversed() with { BackupFile = backup };
        await changer.ApplyAsync(restore, CancellationToken.None);

        Assert.Equal(SettingChange.Present, await changer.ReadAsync(RemoveKey, CancellationToken.None));
        using var back = Registry.CurrentUser.OpenSubKey(_scratchKey)!;
        Assert.Equal("https://hijack.example", back.GetValue("Text"));
        Assert.Equal(RegistryValueKind.ExpandString, back.GetValueKind("Expand"));
        Assert.Equal(@"%ProgramFiles%\Thing", back.GetValue("Expand", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(new[] { "one", "two" }, (string[])back.GetValue("Lines")!);
        Assert.Equal(unchecked((int)0xFFFFFFFE), back.GetValue("Number"));
        Assert.Equal(long.MaxValue, back.GetValue("Big"));
        Assert.Equal(new byte[] { 0, 1, 254, 255 }, (byte[])back.GetValue("Blob")!);
        using var deeper = back.OpenSubKey(@"ExtensionInstallForcelist\Deeper")!;
        Assert.Equal("aaaabbbbccccddddeeeeffffgggghhhh", deeper.GetValue("1"));
    }

    [Fact]
    public async Task A_copy_of_one_key_is_never_restored_over_another()
    {
        using (Registry.CurrentUser.CreateSubKey(_scratchKey, writable: true))
        {
        }

        var changer = Changer();
        var backup = await changer.BackupAsync(RemoveKey, _folder, CancellationToken.None);
        var elsewhere = RemoveKey.Reversed() with { RegistryKey = _scratchKey + @"\Other", BackupFile = backup };

        await Assert.ThrowsAsync<InvalidDataException>(() => changer.ApplyAsync(elsewhere, CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_to_copy_means_an_error_not_an_empty_copy()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Changer().BackupAsync(RemoveKey, _folder, CancellationToken.None));
    }

    [Fact]
    public async Task A_removed_file_comes_back_byte_for_byte()
    {
        var file = Path.Combine(_folder, "Mozilla Firefox", "distribution", "policies.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, """{ "policies": { "Homepage": { "URL": "https://hijack.example" } } }""");
        var original = File.ReadAllBytes(file);
        var remove = new SettingChange { Key = "file", Kind = ChangeKind.File, Target = file, From = SettingChange.Present, To = SettingChange.Absent, Undo = UndoSupport.InApp };
        var changer = Changer();

        var backup = await changer.BackupAsync(remove, Path.Combine(_folder, "backups"), CancellationToken.None);
        await changer.ApplyAsync(remove, CancellationToken.None);
        Assert.False(File.Exists(file));

        await changer.ApplyAsync(remove.Reversed() with { BackupFile = backup }, CancellationToken.None);

        Assert.Equal(original, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task Restoring_without_the_copy_fails_and_changes_nothing()
    {
        var restore = RemoveKey.Reversed() with { BackupFile = Path.Combine(_folder, "gone.json") };

        await Assert.ThrowsAsync<FileNotFoundException>(() => Changer().ApplyAsync(restore, CancellationToken.None));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_scratchKey));
    }
}

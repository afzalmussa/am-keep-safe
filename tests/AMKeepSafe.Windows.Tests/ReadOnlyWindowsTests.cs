using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Windows.Checks;
using AMKeepSafe.Windows.Platform;

namespace AMKeepSafe.Windows.Tests;

/// <summary>
/// Runs the real Windows code on the CI runner: real registry, real PowerShell. Read only.
/// These prove the scripts run and their output parses; the rules are tested in AMKeepSafe.Tests.
/// </summary>
public class ReadOnlyWindowsTests
{
    private readonly ListLog _log = new();

    private PowerShellRunner PowerShell() => new(_log);

    [Fact]
    public void Registry_reader_reads_the_Windows_build()
    {
        var facts = WindowsVersionFacts.Read(new WindowsRegistryReader());

        Assert.True(facts.Build >= 17763, $"Unexpected build {facts.Build}");
        Assert.False(string.IsNullOrWhiteSpace(facts.EditionId));
    }

    [Fact]
    public void Registry_reader_lists_keys_and_returns_nothing_for_missing_ones()
    {
        var registry = new WindowsRegistryReader();

        Assert.NotEmpty(registry.GetSubKeyNames(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft"));
        Assert.Null(registry.GetValue(RegistryRoot.LocalMachine, @"SOFTWARE\AMKeepSafe\DoesNotExist", "Nothing"));
        Assert.Empty(registry.GetSubKeyNames(RegistryRoot.LocalMachine, @"SOFTWARE\AMKeepSafe\DoesNotExist"));
        Assert.Empty(registry.GetValueNames(RegistryRoot.LocalMachine, @"SOFTWARE\AMKeepSafe\DoesNotExist"));
    }

    [Fact]
    public void Process_list_includes_this_test_run()
    {
        var running = new WindowsProcessList().RunningProgramNames();

        Assert.Contains(running, n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(running, n => n.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) || n.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Firewall_script_reads_all_three_network_kinds()
    {
        var facts = await new FirewallInfo(PowerShell()).GetAsync(CancellationToken.None);

        Assert.Equal(3, facts.Profiles.Count);
    }

    [Fact]
    public async Task Windows_Update_status_script_reads_the_service()
    {
        var status = await new WindowsUpdateInfo(PowerShell()).GetStatusAsync(CancellationToken.None);

        Assert.NotEqual(ServiceStart.Unknown, status.ServiceStart);
    }

    // Each of these must finish and parse. What they find depends on the runner, so only "didn't break" is checked.
    [Fact]
    public async Task Antivirus_script_runs_and_parses() =>
        await new AntivirusInfo(PowerShell()).GetAsync(CancellationToken.None);

    [Fact]
    public async Task Encryption_script_runs_and_parses() =>
        await new EncryptionInfo(PowerShell()).GetAsync(CancellationToken.None);

    [Fact]
    public async Task Secure_Boot_and_TPM_script_runs_and_parses() =>
        await new SecureBootTpmInfo(PowerShell()).GetAsync(CancellationToken.None);

    [Fact]
    public async Task SMB1_script_runs_and_parses()
    {
        var facts = await new Smb1Info(PowerShell()).GetAsync(CancellationToken.None);

        Assert.NotEqual(FeatureState.Unknown, facts.Feature);
    }

    [Fact]
    public async Task The_whole_safety_check_runs_on_a_real_PC_with_a_result_for_every_check()
    {
        var powerShell = PowerShell();
        var registry = new WindowsRegistryReader();
        var checks = SafetyCheckSet.Create(new CheckSources(
            registry, new WindowsProcessList(), new WindowsUpdateInfo(powerShell), new AntivirusInfo(powerShell), new FirewallInfo(powerShell),
            new EncryptionInfo(powerShell), new SecureBootTpmInfo(powerShell), new Smb1Info(powerShell), RemoteToolList.LoadEmbedded(), TimeProvider.System,
            BrowserReadOnlyTests.Runner(_log)));
        var catalog = CheckCatalog.LoadEmbedded();

        var results = await new SafetyCheckRunner(checks, catalog, _log).RunAsync();

        Assert.Equal(catalog.Checks.Count, results.Count);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Summary)));
        // An error means a check crashed or produced an outcome with no wording. Neither should happen.
        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Check '", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_settings_page_has_somewhere_to_go()
    {
        foreach (var page in Enum.GetValues<WindowsSettingsPage>())
        {
            var (target, isProgram) = WindowsShellLauncher.SettingsTarget(page);

            if (isProgram)
            {
                Assert.True(File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), target)), $"{page}: {target} isn't in the Windows system folder");
            }
            else
            {
                Assert.Matches("^(ms-settings:|windowsdefender://)", target);
            }
        }
    }
}

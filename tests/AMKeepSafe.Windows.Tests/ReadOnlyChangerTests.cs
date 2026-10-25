using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Windows.Fixes;
using AMKeepSafe.Windows.Platform;

namespace AMKeepSafe.Windows.Tests;

/// <summary>
/// The changer's reads, against a real PC. Nothing here applies a change: fixes are planned, which
/// only reads, and the plans are checked. Applying is tested with fakes in AMKeepSafe.Tests.
/// </summary>
public class ReadOnlyChangerTests
{
    private readonly ListLog _log = new();

    private WindowsSystemChanger Changer() => new(new PowerShellRunner(_log), new WindowsRegistryReader());

    [Theory]
    [InlineData("Domain")]
    [InlineData("Private")]
    [InlineData("Public")]
    public async Task Reads_each_firewall_profile_as_on_or_off(string profile)
    {
        var value = await Changer().ReadAsync(new SettingChange { Key = "k", Kind = ChangeKind.FirewallProfile, Target = profile }, CancellationToken.None);

        Assert.Contains(value, new[] { "on", "off" });
    }

    [Fact]
    public async Task Reads_the_Windows_Update_service_start_setting()
    {
        var value = await Changer().ReadAsync(new SettingChange { Key = "k", Kind = ChangeKind.ServiceStart, Target = "wuauserv" }, CancellationToken.None);

        Assert.Contains(value, new[] { "Automatic", "Manual", "Disabled" });
    }

    [Fact]
    public async Task Reads_a_registry_number_and_says_null_for_one_that_isnt_set()
    {
        var changer = Changer();
        var build = new SettingChange { Key = "k", Kind = ChangeKind.RegistryValue, Root = RegistryRoot.LocalMachine, RegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", Target = "CurrentMajorVersionNumber" };
        var missing = build with { RegistryKey = @"SOFTWARE\AMKeepSafe\DoesNotExist", Target = "Nothing" };

        Assert.Equal("10", await changer.ReadAsync(build, CancellationToken.None));
        Assert.Null(await changer.ReadAsync(missing, CancellationToken.None));
    }

    [Fact]
    public async Task Reads_the_SMB1_feature()
    {
        var value = await Changer().ReadAsync(new SettingChange { Key = "k", Kind = ChangeKind.WindowsFeature, Target = "SMB1Protocol" }, CancellationToken.None);

        Assert.Contains(value, new[] { "Enabled", "Disabled", null });
    }

    [Fact]
    public async Task Every_fix_can_be_planned_on_a_real_PC_without_changing_anything()
    {
        var catalog = FixCatalog.LoadEmbedded();
        foreach (var fix in FixSet.Create(Changer(), Environment.GetEnvironmentVariable("SystemDrive") ?? "C:"))
        {
            IReadOnlyList<SettingChange> plan;
            try
            {
                plan = await fix.PlanAsync(catalog[fix.Id], CancellationToken.None);
            }
            catch (InvalidOperationException ex) when (fix.Id == DefenderRealTimeOnFix.FixId)
            {
                // Windows Server runners may not have Microsoft Defender's commands. That's the runner, not the app.
                _log.Write(AMKeepSafe.Core.Logging.LogLevel.Info, $"Skipped {fix.Id}: {ex.Message}");
                continue;
            }

            Assert.All(plan, c => Assert.False(ProtectionGuard.WouldWeaken(c), $"{fix.Id}: {c.TechnicalDetail}"));
        }

        // Planning must never have run a command that changes anything.
        Assert.DoesNotContain(_log.Entries, e => e.Message.StartsWith("PowerShell: Changing", StringComparison.Ordinal));
    }
}

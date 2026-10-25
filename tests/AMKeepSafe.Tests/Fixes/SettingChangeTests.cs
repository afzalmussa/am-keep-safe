using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Tests.Fixes;

public class SettingChangeTests
{
    private static readonly SettingChange RemoteDesktop = new()
    {
        Key = "remoteDesktopOff",
        Kind = ChangeKind.RegistryValue,
        Root = RegistryRoot.LocalMachine,
        RegistryKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server",
        Target = "fDenyTSConnections",
        From = "0",
        To = "1",
        Description = "Turn off Remote Desktop",
        UndoDescription = "Turn Remote Desktop back on",
        Undo = UndoSupport.InApp,
    };

    [Theory]
    [InlineData(ChangeKind.RegistryValue, @"Registry value HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections: from 0 to 1")]
    [InlineData(ChangeKind.FirewallProfile, "Windows Firewall, fDenyTSConnections networks: from 0 to 1")]
    [InlineData(ChangeKind.Task, "Runs \"fDenyTSConnections\" once. No settings change.")]
    public void Technical_detail_says_exactly_what_changes(ChangeKind kind, string expected)
    {
        Assert.Equal(expected, (RemoteDesktop with { Kind = kind }).TechnicalDetail);
    }

    [Fact]
    public void A_value_that_isnt_set_is_described_as_not_set()
    {
        Assert.EndsWith("from not set to 1", (RemoteDesktop with { From = null }).TechnicalDetail);
    }

    [Fact]
    public void Reversing_swaps_the_values_and_the_wording()
    {
        var back = RemoteDesktop.Reversed();

        Assert.Equal("1", back.From);
        Assert.Equal("0", back.To);
        Assert.Equal("Turn Remote Desktop back on", back.Description);
        Assert.Equal(RemoteDesktop, back.Reversed());
    }

    [Fact]
    public void Survives_the_trip_through_the_undo_journal()
    {
        var withPage = RemoteDesktop with { UndoPage = WindowsSettingsPage.RemoteDesktop, NeedsRestart = true, From = null };

        Assert.Equal(withPage, SettingChange.FromUndoData(withPage.ToUndoData()));
    }

    [Fact]
    public void A_damaged_journal_record_gives_null_rather_than_a_wrong_change()
    {
        var data = RemoteDesktop.ToUndoData().Where(kv => kv.Key != "kind").ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Null(SettingChange.FromUndoData(data));
        Assert.Null(SettingChange.FromUndoData(new Dictionary<string, string> { ["key"] = "x", ["target"] = "y", ["kind"] = "Nonsense", ["undo"] = "InApp" }));
    }
}

using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Checks.Rules;

namespace AMKeepSafe.Tests.Checks;

/// <summary>Sample output in the shape the Windows project's PowerShell scripts print.</summary>
public class FactParsersTests
{
    [Fact]
    public void Windows_Update_status()
    {
        var status = FactParsers.WindowsUpdateStatus("""{"serviceStart":"Manual","lastInstalled":"2026-10-20T09:15:00.0000000Z","noAutoUpdate":null,"pausedUntil":null}""");

        Assert.Equal(ServiceStart.Manual, status.ServiceStart);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 9, 15, 0, TimeSpan.Zero), status.LastInstalled);
        Assert.False(status.TurnedOffByPolicy);
        Assert.Null(status.PausedUntil);
    }

    [Theory]
    [InlineData("Automatic", ServiceStart.Automatic)]
    [InlineData("Disabled", ServiceStart.Disabled)]
    [InlineData("Something new", ServiceStart.Unknown)]
    public void Service_start_settings(string text, ServiceStart expected)
    {
        Assert.Equal(expected, FactParsers.WindowsUpdateStatus($$"""{"serviceStart":"{{text}}"}""").ServiceStart);
    }

    [Fact]
    public void Windows_Update_paused_and_turned_off()
    {
        var status = FactParsers.WindowsUpdateStatus("""{"serviceStart":null,"lastInstalled":null,"noAutoUpdate":1,"pausedUntil":"2026-11-08T08:00:00.0000000Z"}""");

        Assert.Equal(ServiceStart.Unknown, status.ServiceStart);
        Assert.Null(status.LastInstalled);
        Assert.True(status.TurnedOffByPolicy);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero), status.PausedUntil);
    }

    [Fact]
    public void A_nonsense_old_date_counts_as_unknown()
    {
        Assert.Null(FactParsers.WindowsUpdateStatus("""{"lastInstalled":"1601-01-01T00:00:00Z"}""").LastInstalled);
    }

    [Theory]
    [InlineData("""{"count":4,"error":null}""", 4)]
    [InlineData("""{"count":null,"error":"0x8024402c"}""", null)]
    public void Waiting_updates(string json, int? expected)
    {
        Assert.Equal(expected, FactParsers.WaitingUpdates(json));
    }

    [Fact]
    public void Antivirus_with_Defender_and_Security_Center()
    {
        var facts = FactParsers.Antivirus("""
            {"defender":{"antivirusEnabled":true,"realTimeProtectionEnabled":true,"signaturesUpdated":"2026-10-25T03:00:00.0000000Z","amRunningMode":"Normal"},
             "products":[{"name":"Windows Defender","state":397568},{"name":"Norton 360","state":262144}]}
            """);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.Zero), facts.Defender!.VirusInfoUpdated);
        Assert.True(facts.Defender.RealTimeProtectionOn);
        Assert.Equal("Normal", facts.Defender.RunningMode);
        Assert.Equal(2, facts.Products!.Count);
        Assert.True(facts.Products[0].IsOn);
        Assert.False(facts.Products[1].IsOn);
    }

    [Fact]
    public void Antivirus_where_neither_source_answered()
    {
        var facts = FactParsers.Antivirus("""{"defender":null,"products":null}""");

        Assert.Null(facts.Defender);
        Assert.Null(facts.Products);
    }

    [Fact]
    public void A_single_product_still_parses_and_broken_entries_are_skipped()
    {
        var facts = FactParsers.Antivirus("""{"products":[{"name":"Windows Defender","state":397568},{"name":"","state":1},{"name":"X"}]}""");

        Assert.Single(facts.Products!);
    }

    [Fact]
    public void Firewall_profiles_and_other_firewalls()
    {
        var facts = FactParsers.Firewall("""
            {"profiles":[{"name":"Domain","enabled":true},{"name":"Private","enabled":true},{"name":"Public","enabled":false}],
             "others":[{"name":"ESET Firewall","state":266256},{"name":"Old Firewall","state":262144}]}
            """);

        Assert.Equal(3, facts.Profiles.Count);
        Assert.False(facts.Profiles[NetworkProfile.Public]);
        Assert.Equal(["ESET Firewall"], facts.OtherFirewalls);
    }

    [Fact]
    public void Firewall_with_no_answer()
    {
        var facts = FactParsers.Firewall("""{"profiles":null,"others":[]}""");

        Assert.Empty(facts.Profiles);
        Assert.Empty(facts.OtherFirewalls);
    }

    [Theory]
    [InlineData("""{"available":true,"protectionStatus":1,"conversionStatus":1}""", true, EncryptionState.Encrypted, true)]
    [InlineData("""{"available":true,"protectionStatus":0,"conversionStatus":1}""", true, EncryptionState.Encrypted, false)]
    [InlineData("""{"available":true,"protectionStatus":0,"conversionStatus":0}""", true, EncryptionState.NotEncrypted, false)]
    [InlineData("""{"available":true,"protectionStatus":0,"conversionStatus":2}""", true, EncryptionState.Encrypting, false)]
    [InlineData("""{"available":true,"protectionStatus":0,"conversionStatus":4}""", true, EncryptionState.Encrypting, false)]
    [InlineData("""{"available":true,"protectionStatus":0,"conversionStatus":3}""", true, EncryptionState.Decrypting, false)]
    [InlineData("""{"available":false,"error":"Invalid namespace"}""", false, EncryptionState.Unknown, false)]
    public void Encryption(string json, bool available, EncryptionState state, bool protectionOn)
    {
        Assert.Equal(new EncryptionFacts(available, state, protectionOn), FactParsers.Encryption(json));
    }

    [Fact]
    public void Secure_Boot_and_TPM()
    {
        var facts = FactParsers.SecureBootTpm("""{"secureBoot":"on","tpmPresent":true,"tpmReady":true,"tpmVersion":"2.0, 0, 1.59"}""");

        Assert.Equal(new SecureBootTpmFacts(SecureBootState.On, true, true, "2.0"), facts);
    }

    [Theory]
    [InlineData("off", SecureBootState.Off)]
    [InlineData("notSupported", SecureBootState.NotSupported)]
    [InlineData("unknown", SecureBootState.Unknown)]
    public void Secure_Boot_states(string text, SecureBootState expected)
    {
        Assert.Equal(expected, FactParsers.SecureBootTpm($$"""{"secureBoot":"{{text}}"}""").SecureBoot);
    }

    [Theory]
    [InlineData("Enabled", FeatureState.Enabled)]
    [InlineData("Disabled", FeatureState.Disabled)]
    [InlineData("DisabledWithPayloadRemoved", FeatureState.NotPresent)]
    [InlineData("", FeatureState.Unknown)]
    public void SMB1_feature_states(string state, FeatureState expected)
    {
        Assert.Equal(expected, FactParsers.Smb1($$"""{"feature":"{{state}}","serverSmb1":false}""").Feature);
    }

    [Fact]
    public void Broken_output_throws_so_the_check_shows_as_couldnt_check()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => FactParsers.Firewall("{ not json"));
    }
}

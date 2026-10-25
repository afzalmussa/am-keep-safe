using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;

namespace AMKeepSafe.Tests.Checks.Rules;

public class EncryptionCheckTests
{
    [Theory]
    [InlineData(true, EncryptionState.Encrypted, true, EncryptionCheck.On)]
    [InlineData(true, EncryptionState.Encrypted, false, EncryptionCheck.Suspended)]
    [InlineData(true, EncryptionState.Encrypting, false, EncryptionCheck.TurningOn)]
    [InlineData(true, EncryptionState.NotEncrypted, false, EncryptionCheck.Off)]
    [InlineData(true, EncryptionState.Decrypting, false, EncryptionCheck.Off)]
    [InlineData(false, EncryptionState.Unknown, false, EncryptionCheck.NotAvailable)]
    [InlineData(true, EncryptionState.Unknown, false, CheckCatalog.CouldNotCheckCode)]
    public void Rules(bool available, EncryptionState state, bool protectionOn, string expected)
    {
        Assert.Equal(expected, EncryptionCheck.Evaluate(new EncryptionFacts(available, state, protectionOn)).Code);
    }

    [Fact]
    public void Encryption_on_is_information_not_a_pass_and_points_to_the_recovery_key()
    {
        // The brief: knowing where the recovery key is matters more than whether encryption is on.
        var on = CheckCatalog.LoadEmbedded()[EncryptionCheck.CheckId].Outcomes[EncryptionCheck.On];

        Assert.Equal(CheckStatus.Info, on.Status);
        Assert.Contains("recovery key", on.Summary);
        Assert.Contains(on.Actions, a => a.Url == "https://aka.ms/myrecoverykey");
    }
}

public class SecureBootTpmCheckTests
{
    [Theory]
    [InlineData(SecureBootState.On, true, true, SecureBootTpmCheck.BothOn)]
    [InlineData(SecureBootState.Off, true, true, SecureBootTpmCheck.SecureBootOff)]
    [InlineData(SecureBootState.NotSupported, true, true, SecureBootTpmCheck.SecureBootOff)]
    [InlineData(SecureBootState.On, false, null, SecureBootTpmCheck.NoTpm)]
    [InlineData(SecureBootState.On, true, false, SecureBootTpmCheck.NoTpm)]
    [InlineData(SecureBootState.NotSupported, false, null, SecureBootTpmCheck.NeitherOn)]
    [InlineData(SecureBootState.Unknown, null, null, CheckCatalog.CouldNotCheckCode)]
    public void Rules(SecureBootState secureBoot, bool? tpmPresent, bool? tpmReady, string expected)
    {
        Assert.Equal(expected, SecureBootTpmCheck.Evaluate(new SecureBootTpmFacts(secureBoot, tpmPresent, tpmReady, "2.0")).Code);
    }

    [Fact]
    public void Every_outcome_is_information_only()
    {
        var check = CheckCatalog.LoadEmbedded()[SecureBootTpmCheck.CheckId];

        Assert.All(check.Outcomes.Values, o => Assert.Equal(CheckStatus.Info, o.Status));
    }
}

public class Smb1CheckTests
{
    [Theory]
    [InlineData(FeatureState.Enabled, false, Smb1Check.On)]
    [InlineData(FeatureState.Enabled, null, Smb1Check.On)]
    [InlineData(FeatureState.Disabled, false, Smb1Check.Off)]
    [InlineData(FeatureState.NotPresent, null, Smb1Check.Off)]
    [InlineData(FeatureState.Unknown, true, Smb1Check.On)]
    [InlineData(FeatureState.Unknown, false, Smb1Check.Off)]
    [InlineData(FeatureState.Unknown, null, CheckCatalog.CouldNotCheckCode)]
    public void Rules(FeatureState feature, bool? server, string expected)
    {
        Assert.Equal(expected, Smb1Check.Evaluate(new Smb1Facts(feature, server)).Code);
    }

    [Fact]
    public void A_leftover_server_setting_doesnt_count_when_the_feature_is_removed()
    {
        Assert.Equal(Smb1Check.Off, Smb1Check.Evaluate(new Smb1Facts(FeatureState.Disabled, true)).Code);
    }
}

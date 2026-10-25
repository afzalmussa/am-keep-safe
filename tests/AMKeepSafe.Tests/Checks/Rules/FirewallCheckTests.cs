using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;

namespace AMKeepSafe.Tests.Checks.Rules;

public class FirewallCheckTests
{
    private static readonly FirewallCheck.Settings Settings =
        CheckCatalog.LoadEmbedded()[FirewallCheck.CheckId].ParametersAs<FirewallCheck.Settings>();

    private static CheckOutcome Evaluate(bool domain, bool @private, bool @public, params string[] others) =>
        FirewallCheck.Evaluate(new FirewallFacts(
            new Dictionary<NetworkProfile, bool> { [NetworkProfile.Domain] = domain, [NetworkProfile.Private] = @private, [NetworkProfile.Public] = @public },
            others), Settings);

    [Fact]
    public void All_on()
    {
        Assert.Equal(FirewallCheck.AllOn, Evaluate(true, true, true).Code);
    }

    [Fact]
    public void One_off_names_the_network_in_everyday_words()
    {
        var outcome = Evaluate(true, true, false);

        Assert.Equal(FirewallCheck.SomeOff, outcome.Code);
        Assert.Equal("public networks, like in cafés", outcome.Values["networks"]);
    }

    [Fact]
    public void Two_off_are_listed_together()
    {
        Assert.Equal("work networks and home networks", Evaluate(false, false, true).Values["networks"]);
    }

    [Fact]
    public void All_off()
    {
        Assert.Equal(FirewallCheck.AllOff, Evaluate(false, false, false).Code);
    }

    [Fact]
    public void Another_firewall_in_charge_is_fine()
    {
        var outcome = Evaluate(false, false, false, "ESET Firewall");

        Assert.Equal(FirewallCheck.OtherFirewall, outcome.Code);
        Assert.Equal("ESET Firewall", outcome.Values["name"]);
    }

    [Fact]
    public void Another_firewall_doesnt_matter_when_Windows_Firewall_is_on_anyway()
    {
        Assert.Equal(FirewallCheck.AllOn, Evaluate(true, true, true, "ESET Firewall").Code);
    }

    [Fact]
    public void No_profiles_read_is_couldnt_check()
    {
        Assert.Equal(CheckCatalog.CouldNotCheckCode, FirewallCheck.Evaluate(new FirewallFacts(new Dictionary<NetworkProfile, bool>(), []), Settings).Code);
    }
}

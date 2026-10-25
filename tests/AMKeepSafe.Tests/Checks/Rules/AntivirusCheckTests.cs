using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks.Rules;

public class AntivirusCheckTests
{
    private static readonly DateTimeOffset Now = HealthyPc.Now;
    private static readonly AntivirusCheck.Settings Settings = new() { AmberAfterDays = 3, RedAfterDays = 14 };

    private static readonly AntivirusProduct DefenderOn = new("Windows Defender", true, true);
    private static readonly AntivirusProduct DefenderOff = new("Windows Defender", false, true);

    private static DefenderStatus Defender(bool enabled = true, bool realTime = true, double? infoDaysOld = 0.5, string mode = "Normal") =>
        new(enabled, realTime, infoDaysOld is { } d ? Now.AddDays(-d) : null, mode);

    private static string Code(IReadOnlyList<AntivirusProduct>? products, DefenderStatus? defender) =>
        AntivirusCheck.Evaluate(new AntivirusFacts(products, defender), Now, Settings).Code;

    [Fact]
    public void Defender_on_and_up_to_date()
    {
        Assert.Equal(AntivirusCheck.DefenderOn, Code([DefenderOn], Defender()));
    }

    [Theory]
    [InlineData(3.0, AntivirusCheck.DefenderOn)]
    [InlineData(3.5, AntivirusCheck.DefenderInfoOld)]
    [InlineData(14.0, AntivirusCheck.DefenderInfoOld)]
    [InlineData(15.0, AntivirusCheck.DefenderInfoVeryOld)]
    public void Age_of_virus_information_sets_the_colour(double daysOld, string expected)
    {
        Assert.Equal(expected, Code([DefenderOn], Defender(infoDaysOld: daysOld)));
    }

    [Fact]
    public void Real_time_protection_off()
    {
        Assert.Equal(AntivirusCheck.DefenderRealTimeOff, Code([DefenderOn], Defender(realTime: false)));
    }

    [Fact]
    public void Defender_antivirus_switched_off_and_nothing_else()
    {
        Assert.Equal(AntivirusCheck.NoneOn, Code([DefenderOff], Defender(enabled: false, realTime: false)));
    }

    [Fact]
    public void Another_antivirus_in_charge_is_fine_and_named()
    {
        var outcome = AntivirusCheck.Evaluate(
            new AntivirusFacts([DefenderOff, new AntivirusProduct("Norton 360", true, true)], Defender(enabled: false, realTime: false, mode: "Passive Mode")),
            Now, Settings);

        Assert.Equal(AntivirusCheck.OtherOn, outcome.Code);
        Assert.Equal("Norton 360", outcome.Values["name"]);
    }

    [Fact]
    public void Another_antivirus_with_old_virus_information()
    {
        Assert.Equal(AntivirusCheck.OtherOutOfDate, Code([new AntivirusProduct("McAfee", true, false)], null));
    }

    [Fact]
    public void An_expired_antivirus_that_is_switched_off_doesnt_count()
    {
        Assert.Equal(AntivirusCheck.NoneOn, Code([new AntivirusProduct("McAfee", false, false), DefenderOff], null));
    }

    [Fact]
    public void Security_Center_vouching_for_Defender_is_enough_when_Defender_details_are_missing()
    {
        Assert.Equal(AntivirusCheck.DefenderOn, Code([DefenderOn], null));
    }

    [Fact]
    public void No_answer_from_either_source_is_couldnt_check()
    {
        Assert.Equal(CheckCatalog.CouldNotCheckCode, Code(null, null));
    }

    [Fact]
    public void Defender_on_without_a_date_is_still_on()
    {
        Assert.Equal(AntivirusCheck.DefenderOn, Code([DefenderOn], Defender(infoDaysOld: null)));
    }

    [Theory]
    [InlineData("Windows Defender", true)]
    [InlineData("Microsoft Defender Antivirus", true)]
    [InlineData("Norton 360", false)]
    public void Recognises_Microsoft_Defender_by_name(string name, bool isDefender)
    {
        Assert.Equal(isDefender, new AntivirusProduct(name, true, true).IsMicrosoftDefender);
    }
}

public class SecurityCenterStateTests
{
    [Theory]
    [InlineData(0x061100, true, true)]   // Defender on, up to date (Windows reports it as 397568)
    [InlineData(0x060100, false, true)]  // Defender off
    [InlineData(0x061110, true, false)]  // on, out of date
    [InlineData(0x041000, true, true)]   // a third-party product on
    [InlineData(0x040000, false, true)]
    public void Decodes_on_and_up_to_date(int state, bool on, bool upToDate)
    {
        Assert.Equal(on, SecurityCenterState.IsOn(state));
        Assert.Equal(upToDate, SecurityCenterState.IsUpToDate(state));
    }
}

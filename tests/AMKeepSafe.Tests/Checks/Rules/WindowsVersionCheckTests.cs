using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks.Rules;

public class WindowsVersionCheckTests
{
    // The real dates from checks.json, so these tests also prove the shipped dates behave as intended.
    private static readonly WindowsVersionCheck.Settings Shipped =
        CheckCatalog.LoadEmbedded()[WindowsVersionCheck.CheckId].ParametersAs<WindowsVersionCheck.Settings>();

    private static readonly DateTimeOffset Now = HealthyPc.Now;

    private static CheckOutcome Evaluate(int build, string edition = "Core", string? displayVersion = null, double? lastUpdateDaysAgo = 5, DateTimeOffset? now = null, string product = "Windows 10 Home") =>
        WindowsVersionCheck.Evaluate(
            new WindowsVersionFacts(product, edition, displayVersion, build),
            lastUpdateDaysAgo is { } d ? (now ?? Now).AddDays(-d) : null,
            now ?? Now,
            Shipped);

    [Fact]
    public void Windows_11_25H2_is_supported()
    {
        var outcome = Evaluate(26200, displayVersion: "25H2");

        Assert.Equal(WindowsVersionCheck.Win11Supported, outcome.Code);
        Assert.Equal("25H2", outcome.Values["version"]);
    }

    [Fact]
    public void Windows_11_24H2_Home_ended_on_13_October_2026()
    {
        var outcome = Evaluate(26100, displayVersion: "24H2");

        Assert.Equal(WindowsVersionCheck.Win11Ended, outcome.Code);
        Assert.Equal("13 October 2026", outcome.Values["endDate"]);
    }

    [Fact]
    public void Windows_11_24H2_Enterprise_runs_another_year()
    {
        var outcome = Evaluate(26100, edition: "Enterprise");

        Assert.Equal(WindowsVersionCheck.Win11Supported, outcome.Code);
        Assert.Equal("12 October 2027", outcome.Values["endDate"]);
    }

    [Fact]
    public void Shortly_before_a_version_ends_it_says_when_but_stays_calm()
    {
        var outcome = Evaluate(26200, now: new DateTimeOffset(2027, 9, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(WindowsVersionCheck.Win11EndingSoon, outcome.Code);
        Assert.Equal("12 October 2027", outcome.Values["endDate"]);
    }

    [Theory]
    [InlineData("2027-10-12", WindowsVersionCheck.Win11EndingSoon)]
    [InlineData("2027-10-13", WindowsVersionCheck.Win11Ended)]
    public void The_last_day_of_support_still_counts_as_supported(string date, string expected)
    {
        var now = new DateTimeOffset(DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero).AddHours(12);

        Assert.Equal(expected, Evaluate(26200, now: now).Code);
    }

    [Fact]
    public void A_build_between_known_versions_uses_the_version_below_it()
    {
        // 22631.xxxx is 23H2. A made-up 22700 would still be a 23H2-era build.
        Assert.Equal("23H2", Evaluate(22700).Values["version"]);
    }

    [Fact]
    public void A_build_newer_than_anything_listed_is_treated_as_supported()
    {
        var outcome = Evaluate(28000, displayVersion: "26H1");

        Assert.Equal(WindowsVersionCheck.Win11Supported, outcome.Code);
        Assert.Equal("26H1", outcome.Values["version"]);
    }

    [Fact]
    public void Windows_10_with_recent_updates_looks_signed_up_for_extended_updates()
    {
        var outcome = Evaluate(19045, lastUpdateDaysAgo: 10);

        Assert.Equal(WindowsVersionCheck.Win10GettingUpdates, outcome.Code);
        Assert.Equal("12 October 2027", outcome.Values["esuEnds"]);
        Assert.Equal("14 October 2025", outcome.Values["windows10SupportEnded"]);
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(null)]
    public void Windows_10_without_recent_updates_explains_extended_updates(double? daysAgo)
    {
        Assert.Equal(WindowsVersionCheck.Win10NotGettingUpdates, Evaluate(19045, lastUpdateDaysAgo: daysAgo).Code);
    }

    [Fact]
    public void Windows_10_after_extended_updates_end()
    {
        Assert.Equal(WindowsVersionCheck.Win10EsuEnded, Evaluate(19045, now: new DateTimeOffset(2027, 10, 13, 12, 0, 0, TimeSpan.Zero)).Code);
    }

    [Fact]
    public void Windows_10_older_than_22H2_cant_get_extended_updates()
    {
        Assert.Equal(WindowsVersionCheck.Win10OldVersion, Evaluate(19044).Code);
    }

    [Theory]
    [InlineData("Enterprise")]
    [InlineData("Education")]
    public void Windows_10_business_editions_point_to_whoever_looks_after_the_PC(string edition)
    {
        Assert.Equal(WindowsVersionCheck.Win10Organisation, Evaluate(19045, edition: edition).Code);
    }

    [Fact]
    public void Long_term_editions_have_their_own_dates()
    {
        Assert.Equal(WindowsVersionCheck.LongTermServicing, Evaluate(19044, edition: "EnterpriseS", product: "Windows 10 Enterprise LTSC 2021").Code);
    }

    [Fact]
    public void Anything_older_than_Windows_10()
    {
        Assert.Equal(WindowsVersionCheck.OlderWindows, Evaluate(9600).Code);
    }

    [Fact]
    public void Windows_11_reports_itself_as_Windows_10_in_the_registry_but_the_build_decides()
    {
        // ProductName really does say "Windows 10" on Windows 11.
        Assert.Equal(WindowsVersionCheck.Win11Supported, Evaluate(26200, product: "Windows 10 Pro", edition: "Professional").Code);
    }

    [Fact]
    public async Task A_missing_build_number_is_couldnt_check()
    {
        var check = new WindowsVersionCheck(new FakeRegistry(), new FakeWindowsUpdate(), TimeProvider.System);

        await Assert.ThrowsAsync<CouldNotReadException>(() => check.RunAsync(CheckCatalog.LoadEmbedded()[WindowsVersionCheck.CheckId], CancellationToken.None));
    }

    [Fact]
    public void Shipped_Windows_11_dates_are_in_order_and_enterprise_never_ends_first()
    {
        var versions = Shipped.Windows11;

        Assert.Equal(versions.OrderBy(v => v.Build).Select(v => v.Build), versions.Select(v => v.Build));
        Assert.All(versions, v => Assert.True(v.EnterpriseEnds >= v.HomeProEnds, $"{v.Version}: Enterprise ends before Home and Pro"));
        Assert.Equal(new DateOnly(2027, 10, 12), Shipped.Windows10.ConsumerEsuEnds);
    }
}

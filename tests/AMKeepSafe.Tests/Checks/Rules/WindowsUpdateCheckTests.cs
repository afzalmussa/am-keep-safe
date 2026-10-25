using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks.Rules;

public class WindowsUpdateCheckTests
{
    private static readonly DateTimeOffset Now = HealthyPc.Now;
    private static readonly WindowsUpdateCheck.Settings Settings = new() { AmberAfterDays = 45, RedAfterDays = 90 };

    private static CheckOutcome Evaluate(ServiceStart start = ServiceStart.Manual, double? daysAgo = 5, int? waiting = 0, bool policyOff = false, DateTimeOffset? pausedUntil = null) =>
        WindowsUpdateCheck.Evaluate(new WindowsUpdateStatus(start, daysAgo is { } d ? Now.AddDays(-d) : null, policyOff, pausedUntil), waiting, Now, Settings);

    [Theory]
    [InlineData(ServiceStart.Manual)]
    [InlineData(ServiceStart.Automatic)]
    public void Recent_updates_with_the_service_on_is_working(ServiceStart start)
    {
        Assert.Equal(WindowsUpdateCheck.Working, Evaluate(start).Code);
    }

    [Fact]
    public void Updates_waiting_after_a_recent_install_is_still_working()
    {
        Assert.Equal(WindowsUpdateCheck.WorkingWithWaiting, Evaluate(waiting: 3).Code);
    }

    [Fact]
    public void Not_knowing_whats_waiting_doesnt_count_against_the_PC()
    {
        Assert.Equal(WindowsUpdateCheck.Working, Evaluate(waiting: null).Code);
    }

    [Theory]
    [InlineData(45, WindowsUpdateCheck.Working)]
    [InlineData(46, WindowsUpdateCheck.AWhile)]
    [InlineData(90, WindowsUpdateCheck.AWhile)]
    [InlineData(91, WindowsUpdateCheck.NotForAWhile)]
    [InlineData(400, WindowsUpdateCheck.NotForAWhile)]
    public void How_long_since_updates_last_installed_sets_the_colour(double daysAgo, string expected)
    {
        Assert.Equal(expected, Evaluate(daysAgo: daysAgo).Code);
    }

    [Fact]
    public void A_disabled_service_comes_first_whatever_else_is_true()
    {
        Assert.Equal(WindowsUpdateCheck.ServiceDisabled, Evaluate(ServiceStart.Disabled, daysAgo: 1, policyOff: true).Code);
    }

    [Fact]
    public void Automatic_updates_switched_off_by_policy()
    {
        Assert.Equal(WindowsUpdateCheck.TurnedOff, Evaluate(policyOff: true).Code);
    }

    [Fact]
    public void Paused_updates_say_until_when()
    {
        var outcome = Evaluate(pausedUntil: new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(WindowsUpdateCheck.Paused, outcome.Code);
        Assert.Equal("8 November 2026", outcome.Values["pausedUntil"]);
    }

    [Fact]
    public void A_pause_that_has_already_ended_is_ignored()
    {
        Assert.Equal(WindowsUpdateCheck.Working, Evaluate(pausedUntil: Now.AddDays(-1)).Code);
    }

    [Fact]
    public void Not_knowing_when_updates_last_installed_is_couldnt_check_not_a_problem()
    {
        Assert.Equal(CheckCatalog.CouldNotCheckCode, Evaluate(daysAgo: null).Code);
    }

    [Fact]
    public void Fills_in_when_updates_last_installed()
    {
        var outcome = Evaluate(daysAgo: 3);

        Assert.Equal("3 days ago", outcome.Values["lastInstalledAgo"]);
        Assert.Equal("22 October 2026", outcome.Values["lastInstalledDate"]);
    }

    [Fact]
    public async Task Skips_the_slow_lookup_when_the_service_is_disabled()
    {
        var updates = new FakeWindowsUpdate { Status = new WindowsUpdateStatus(ServiceStart.Disabled, null, false, null) };
        var check = new WindowsUpdateCheck(updates, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));

        var outcome = await check.RunAsync(CheckCatalog.LoadEmbedded()[WindowsUpdateCheck.CheckId], CancellationToken.None);

        Assert.Equal(WindowsUpdateCheck.ServiceDisabled, outcome.Code);
        Assert.Equal(0, updates.WaitingCalls);
    }
}

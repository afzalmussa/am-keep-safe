using AMKeepSafe.Core.Text;

namespace AMKeepSafe.Tests;

public class FriendlyTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Dates_are_written_the_UK_way()
    {
        Assert.Equal("13 October 2026", FriendlyText.Date(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal("12 October 2027", FriendlyText.Date(new DateOnly(2027, 10, 12)));
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "yesterday")]
    [InlineData(5, "5 days ago")]
    [InlineData(13, "13 days ago")]
    [InlineData(14, "2 weeks ago")]
    [InlineData(59, "8 weeks ago")]
    [InlineData(60, "2 months ago")]
    [InlineData(400, "13 months ago")]
    [InlineData(800, "2 years ago")]
    public void How_long_ago(int days, string expected)
    {
        Assert.Equal(expected, FriendlyText.Ago(Now.AddDays(-days), Now));
    }

    [Fact]
    public void A_date_in_the_future_reads_as_today_rather_than_negative_days()
    {
        Assert.Equal("today", FriendlyText.Ago(Now.AddDays(2), Now));
    }

    [Fact]
    public void Date_and_time_reads_naturally()
    {
        Assert.Matches(@"^\d{1,2} [A-Z][a-z]+ \d{4} at \d{1,2}:\d{2}(am|pm)$", FriendlyText.DateAndTime(Now));
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A and B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B and C")]
    public void Lists_read_naturally(string[] items, string expected)
    {
        Assert.Equal(expected, FriendlyText.List(items));
    }
}

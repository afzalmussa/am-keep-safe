using AMKeepSafe.Core.Checks;

namespace AMKeepSafe.Tests.Checks;

public class SafetyCheckSummaryTests
{
    private static readonly SummaryText Text = new()
    {
        AllClear = "No problems found in the things we checked.",
        SomeToLookAt = "Some things are worth a look.",
        SomeNotChecked = "A few things couldn't be checked.",
        WhatWeCheckedLink = "See what we checked",
    };

    private static CheckResult Result(string id, CheckStatus status) => new(id, id, "x", status, "s", [], [], []);

    [Theory]
    [InlineData(CheckStatus.Good)]
    [InlineData(CheckStatus.Info)]
    public void Green_and_information_only_get_the_all_clear(CheckStatus status)
    {
        Assert.Equal(Text.AllClear, SafetyCheckSummary.Headline([Result("a", CheckStatus.Good), Result("b", status)], Text));
    }

    [Theory]
    [InlineData(CheckStatus.Attention)]
    [InlineData(CheckStatus.Problem)]
    public void Red_or_amber_gets_the_calm_worth_a_look_headline(CheckStatus status)
    {
        Assert.Equal(Text.SomeToLookAt, SafetyCheckSummary.Headline([Result("a", CheckStatus.Good), Result("b", status)], Text));
    }

    [Fact]
    public void Couldnt_check_is_mentioned_without_claiming_all_clear_on_its_own()
    {
        var headline = SafetyCheckSummary.Headline([Result("a", CheckStatus.Good), Result("b", CheckStatus.Unknown)], Text);

        Assert.Equal($"{Text.AllClear} {Text.SomeNotChecked}", headline);
    }

    [Fact]
    public void The_headline_never_contains_a_count()
    {
        var many = Enumerable.Range(0, 7).Select(i => Result($"c{i}", CheckStatus.Problem)).ToList();

        Assert.DoesNotMatch(@"\d", SafetyCheckSummary.Headline(many, Text));
    }

    [Fact]
    public void Display_order_puts_what_needs_doing_first_and_keeps_the_file_order_otherwise()
    {
        var ordered = SafetyCheckSummary.InDisplayOrder(
        [
            Result("update", CheckStatus.Good),
            Result("backup", CheckStatus.Attention),
            Result("tpm", CheckStatus.Info),
            Result("firewall", CheckStatus.Problem),
            Result("uac", CheckStatus.Good),
            Result("smb", CheckStatus.Unknown),
            Result("rdp", CheckStatus.Attention),
        ]);

        Assert.Equal(["firewall", "backup", "rdp", "smb", "tpm", "update", "uac"], ordered.Select(r => r.CheckId));
    }
}

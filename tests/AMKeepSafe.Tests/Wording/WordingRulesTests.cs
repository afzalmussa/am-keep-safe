using AMKeepSafe.Core.Wording;

namespace AMKeepSafe.Tests.Wording;

public class WordingRulesTests
{
    [Theory]
    // Trust rule 3: the app is not an antivirus and must never say the PC is safe or protected.
    [InlineData("Your PC is safe.", WordingProblem.OverPromise)]
    [InlineData("Your computer is protected", WordingProblem.OverPromise)]
    [InlineData("You're safe now", WordingProblem.OverPromise)]
    [InlineData("You are protected from scams", WordingProblem.OverPromise)]
    [InlineData("Everything is secure", WordingProblem.OverPromise)]
    [InlineData("Your PC is now fully protected", WordingProblem.OverPromise)]
    [InlineData("AM Keep Safe keeps your PC safe", WordingProblem.OverPromise)]
    [InlineData("Keeps you protected all year", WordingProblem.OverPromise)]
    [InlineData("100% secure", WordingProblem.OverPromise)]
    [InlineData("We guarantee your files", WordingProblem.OverPromise)]
    // Trust rule 1: calm and factual, never scary.
    [InlineData("Threats found", WordingProblem.ScareWord)]
    [InlineData("Your PC may be infected", WordingProblem.ScareWord)]
    [InlineData("This is dangerous", WordingProblem.ScareWord)]
    [InlineData("Urgent: act now", WordingProblem.ScareWord)]
    [InlineData("Fix this immediately", WordingProblem.ScareWord)]
    [InlineData("Critical problem", WordingProblem.ScareWord)]
    [InlineData("Your PC is at risk", WordingProblem.ScareWord)]
    [InlineData("You've been hacked", WordingProblem.ScareWord)]
    [InlineData("3 problems found", WordingProblem.Counter)]
    [InlineData("12 issues detected", WordingProblem.Counter)]
    [InlineData("{count} problems found", WordingProblem.Counter)]
    [InlineData("Act fast!", WordingProblem.Exclamation)]
    // House style.
    [InlineData("Updates \u2014 working", WordingProblem.Dash)]
    [InlineData("Updates \u2013 working", WordingProblem.Dash)]
    // No jargon in front of nervous users.
    [InlineData("A PowerShell command failed", WordingProblem.Jargon)]
    [InlineData("Changed a registry value", WordingProblem.Jargon)]
    [InlineData("WMI didn't answer", WordingProblem.Jargon)]
    [InlineData("Error 0x80070005", WordingProblem.Jargon)]
    [InlineData("An exception happened", WordingProblem.Jargon)]
    public void Catches_wording_that_breaks_the_rules(string text, WordingProblem expected)
    {
        Assert.Contains(WordingRules.Check(text), v => v.Problem == expected);
        Assert.False(WordingRules.Passes(text));
    }

    [Theory]
    [InlineData("No problems found in the things we checked")]
    [InlineData("Is your stuff backed up?")]
    [InlineData("Microsoft Defender real-time protection is on.")]
    [InlineData("The firewall is on for every type of network.")]
    [InlineData("Device encryption is on. Make sure you know where your recovery key is saved.")]
    [InlineData("If you didn't install this, or a stranger asked you to, tell us.")]
    [InlineData("This is a fake web page, not a real virus warning. Never call the number on the screen.")]
    [InlineData("Scam texts can be forwarded free to 7726.")]
    [InlineData("Old software is a common way in for attackers.")]
    [InlineData("Remote Desktop is turned on. If you don't know why, it's best to turn it off.")]
    [InlineData("AM Keep Safe")]
    [InlineData("Is it safe to use AM Keep Safe?")]
    [InlineData("Show safe fixes")]
    [InlineData("1 thing to look at")]
    public void Lets_calm_factual_wording_through(string text)
    {
        Assert.Empty(WordingRules.Check(text));
    }

    [Fact]
    public void Technical_details_may_use_technical_words_but_must_still_be_calm()
    {
        const string details = @"Registry value HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections changes from 0 to 1";

        Assert.True(WordingRules.Passes(details, includeJargon: false));
        Assert.False(WordingRules.Passes(details));
        Assert.False(WordingRules.Passes("Registry value changed. Your PC is protected.", includeJargon: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_text_passes(string? text)
    {
        Assert.Empty(WordingRules.Check(text));
    }

    [Fact]
    public void Reports_every_problem_in_a_sentence_not_just_the_first()
    {
        var problems = WordingRules.Check("Urgent! Your PC is at risk \u2014 3 threats found").Select(v => v.Problem).ToHashSet();

        Assert.Superset(new HashSet<WordingProblem> { WordingProblem.ScareWord, WordingProblem.Exclamation, WordingProblem.Dash, WordingProblem.Counter }, problems);
    }
}

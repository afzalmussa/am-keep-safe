using AMKeepSafe.Core.Wording;

namespace AMKeepSafe.Tests.Wording;

/// <summary>
/// Trust rules 1 and 3, enforced on the real files: every screen, data file and string in the app
/// is checked, and the build fails on scary or over-promising wording.
/// </summary>
public class RepositoryWordingTests
{
    [Fact]
    public void Screens_are_calm_and_jargon_free() => AssertAllPass(UserFacingText.FromScreens());

    [Fact]
    public void Data_files_are_calm_and_jargon_free() => AssertAllPass(UserFacingText.FromDataFiles());

    [Fact]
    public void Strings_in_the_code_are_calm() => AssertAllPass(UserFacingText.FromCode());

    [Fact]
    public void The_scanner_really_finds_the_text_on_screen()
    {
        // Guards against the check passing because it quietly read nothing.
        var screens = UserFacingText.FromScreens().Select(s => s.Text).ToList();

        Assert.Contains("Safety check", screens);
        Assert.Contains("View log", screens);
        Assert.Contains(screens, t => t.StartsWith("Test mode is on", StringComparison.Ordinal));
    }

    [Fact]
    public void The_scanner_really_finds_strings_in_the_code()
    {
        var code = UserFacingText.FromCode().Select(s => s.Text).ToList();

        Assert.Contains("Couldn't open the log", code);
    }

    private static void AssertAllPass(IEnumerable<TextSample> samples)
    {
        var failures = samples
            .Select(s => (Sample: s, Problems: WordingRules.Check(s.Text, includeJargon: !s.AllowJargon)))
            .Where(r => r.Problems.Count > 0)
            .Select(r => $"{r.Sample.Source}: \"{r.Sample.Text}\" -> {string.Join(", ", r.Problems)}")
            .ToList();

        Assert.True(failures.Count == 0, "Wording that breaks the trust rules:\n" + string.Join('\n', failures));
    }
}

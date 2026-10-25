using System.Text;
using AMKeepSafe.Core.Checks;

namespace AMKeepSafe.Tests.Checks;

public class CheckCatalogTests
{
    private const string Minimal = """
        {
          "statusLabels": { "good": "G", "attention": "A", "problem": "P", "info": "I", "unknown": "U" },
          "summary": { "allClear": "All clear.", "someToLookAt": "Look.", "someNotChecked": "Some not checked.", "whatWeCheckedLink": "See" },
          "couldNotCheck": { "status": "unknown", "summary": "Couldn't check." },
          "checks": [
            {
              "id": "demo",
              "title": "Demo",
              "whatWeCheck": "A demo.",
              "parameters": { "days": 7 },
              "outcomes": {
                "ok": { "status": "good", "summary": "Fine since {when}." },
                "bad": { "status": "problem", "summary": "Not fine.", "howToFix": [ "Step for {when}." ],
                         "actions": [ { "label": "Open", "settingsPage": "windowsUpdate" }, { "label": "Read", "url": "https://example.com" } ] }
              }
            }
          ]
        }
        """;

    private static CheckCatalog Parse(string json) => CheckCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private sealed record DemoSettings
    {
        public int Days { get; init; }
    }

    [Fact]
    public void Loads_checks_outcomes_actions_and_parameters()
    {
        var catalog = Parse(Minimal);
        var demo = catalog["demo"];

        Assert.Equal("Demo", demo.Title);
        Assert.Equal(CheckStatus.Problem, demo.Outcomes["bad"].Status);
        Assert.Equal(new CheckAction("Open", WindowsSettingsPage.WindowsUpdate, null), demo.Outcomes["bad"].Actions[0]);
        Assert.Equal(new CheckAction("Read", null, "https://example.com"), demo.Outcomes["bad"].Actions[1]);
        Assert.Equal(7, demo.ParametersAs<DemoSettings>().Days);
        Assert.Empty(catalog.Validate());
    }

    [Fact]
    public void Rendering_fills_placeholders_in_the_summary_and_steps()
    {
        var catalog = Parse(Minimal);

        var result = catalog.Render(catalog["demo"], new CheckOutcome("bad", new Dictionary<string, string> { ["when"] = "Tuesday" }));

        Assert.Equal(CheckStatus.Problem, result.Status);
        Assert.Equal("Not fine.", result.Summary);
        Assert.Equal(["Step for Tuesday."], result.HowToFix);
        Assert.Equal(2, result.Actions.Count);
    }

    [Fact]
    public void An_outcome_with_no_wording_shows_as_couldnt_check_rather_than_crashing()
    {
        var catalog = Parse(Minimal);

        var result = catalog.Render(catalog["demo"], new CheckOutcome("mystery"));

        Assert.Equal(CheckStatus.Unknown, result.Status);
        Assert.Equal("Couldn't check.", result.Summary);
        Assert.Equal("Demo", result.Title);
    }

    [Fact]
    public void Unknown_placeholders_are_left_visible_so_tests_can_catch_them()
    {
        Assert.Equal("Hello {name}", CheckCatalog.Fill("Hello {name}", new Dictionary<string, string>()));
        Assert.Equal(["a", "b"], CheckCatalog.PlaceholdersIn("{a} and {b}"));
    }

    [Fact]
    public void Missing_parameters_give_the_defaults()
    {
        var check = new CheckDefinition { Id = "x", Title = "X", WhatWeCheck = "X." };

        Assert.Equal(0, check.ParametersAs<DemoSettings>().Days);
    }

    [Fact]
    public void Asking_for_a_check_that_doesnt_exist_says_so()
    {
        Assert.Throws<KeyNotFoundException>(() => Parse(Minimal)["nope"]);
    }

    [Theory]
    [InlineData("\"info\": \"I\", ", "", "statusLabels has no label for Info")]
    [InlineData("\"summary\": \"Not fine.\"", "\"summary\": \"\"", "demo.bad has no summary")]
    [InlineData("\"settingsPage\": \"windowsUpdate\"", "\"settingsPage\": \"windowsUpdate\", \"url\": \"https://example.com\"", "not both or neither")]
    [InlineData("\"url\": \"https://example.com\"", "\"url\": \"http://example.com\"", "isn't a secure https address")]
    [InlineData("\"title\": \"Demo\"", "\"title\": \"\"", "needs an id, a title and whatWeCheck")]
    public void Validation_catches_mistakes(string find, string replace, string expected)
    {
        var problems = Parse(Minimal.Replace(find, replace, StringComparison.Ordinal)).Validate();

        Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_catches_a_duplicate_check_id()
    {
        var doubled = Minimal.Replace("\"checks\": [", "\"checks\": [ { \"id\": \"demo\", \"title\": \"Again\", \"whatWeCheck\": \"x\", \"outcomes\": { \"ok\": { \"status\": \"good\", \"summary\": \"x\" } } },", StringComparison.Ordinal);

        Assert.Contains(Parse(doubled).Validate(), p => p.Contains("used more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void A_settings_page_that_isnt_on_the_fixed_list_fails_to_load()
    {
        // A data file must not be able to make the app open anything else.
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse(Minimal.Replace("windowsUpdate", "cmdExe", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_missing_section_fails_loudly()
    {
        Assert.Throws<InvalidDataException>(() => Parse("""{ "checks": [] }"""));
    }
}

using System.Text.RegularExpressions;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks;

/// <summary>Checks on the real data/checks.json and data/remote-tools.json that ship in the app.</summary>
public partial class ShippedCatalogTests
{
    private static readonly CheckCatalog Catalog = CheckCatalog.LoadEmbedded();
    private static readonly IReadOnlyList<ISafetyCheck> Checks = SafetyCheckSet.Create(HealthyPc.Sources());

    public static TheoryData<string> CheckIds() => [.. Catalog.Checks.Select(c => c.Id)];

    [Fact]
    public void The_shipped_catalog_loads_and_has_no_problems()
    {
        Assert.Empty(Catalog.Validate());
    }

    [Fact]
    public void Every_check_in_the_app_has_wording_and_every_check_in_the_file_is_in_the_app()
    {
        Assert.Equal(Checks.Select(c => c.Id).Order(), Catalog.Checks.Select(c => c.Id).Order());
    }

    [Theory]
    [MemberData(nameof(CheckIds))]
    public void Every_outcome_a_check_can_give_has_wording_and_there_is_no_unused_wording(string id)
    {
        var check = Checks.Single(c => c.Id == id);

        Assert.Equal(check.OutcomeCodes.Order(), Catalog[id].Outcomes.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(CheckIds))]
    public void The_samples_reach_every_outcome(string id)
    {
        // Guards the next test: it only proves anything if every outcome is sampled.
        var reached = OutcomeSamples.All(Catalog)[id].Select(o => o.Code).Where(c => c != CheckCatalog.CouldNotCheckCode).ToHashSet();

        Assert.Equal(Checks.Single(c => c.Id == id).OutcomeCodes.Order(), reached.Order());
    }

    [Theory]
    [MemberData(nameof(CheckIds))]
    public void Every_placeholder_in_the_wording_gets_filled_in(string id)
    {
        foreach (var outcome in OutcomeSamples.All(Catalog)[id])
        {
            var result = Catalog.Render(Catalog[id], outcome);

            foreach (var text in result.HowToFix.Prepend(result.Summary).Concat(result.Items))
            {
                Assert.False(Placeholder().IsMatch(text), $"{id}.{outcome.Code} still has a placeholder: \"{text}\"");
            }
        }
    }

    // Trust rule 2: when a result is red or amber, the fix is free and there's no Pro prompt next to it.
    [Fact]
    public void No_result_wording_mentions_Pro_or_paying()
    {
        var outcomes = Catalog.Checks.SelectMany(c => c.Outcomes.Values).Append(Catalog.CouldNotCheck);

        foreach (var o in outcomes)
        {
            foreach (var text in o.HowToFix.Prepend(o.Summary).Concat(o.Actions.Select(a => a.Label)))
            {
                Assert.False(ProOrPaying().IsMatch(text), $"Result wording mentions Pro or paying: \"{text}\"");
            }
        }
    }

    [Fact]
    public void Every_red_or_amber_result_says_what_to_do_about_it()
    {
        var needingHelp = Catalog.Checks
            .SelectMany(c => c.Outcomes.Select(o => (Id: $"{c.Id}.{o.Key}", Outcome: o.Value)))
            .Where(x => x.Outcome.Status.NeedsAction())
            .ToList();

        Assert.NotEmpty(needingHelp);
        foreach (var (id, outcome) in needingHelp)
        {
            // A short result can carry its advice in the summary itself, as the Windows 10 end-of-updates one does.
            Assert.True(outcome.HowToFix.Count > 0 || outcome.Actions.Count > 0 || outcome.Summary.Length > 120, $"{id} is red or amber but offers no help");
        }
    }

    // Trust rule 3: never over-promise.
    [Fact]
    public void The_all_clear_headline_is_the_wording_from_the_brief()
    {
        Assert.Equal("No problems found in the things we checked.", Catalog.Summary.AllClear);
    }

    [Fact]
    public void Status_labels_are_read_from_the_file()
    {
        Assert.Equal("Looks fine", Catalog.StatusLabels[CheckStatus.Good]);
        Assert.Equal("Couldn't check", Catalog.StatusLabels[CheckStatus.Unknown]);
    }

    [Fact]
    public void Couldnt_check_is_shown_as_unknown_not_as_a_problem()
    {
        Assert.Equal(CheckStatus.Unknown, Catalog.CouldNotCheck.Status);
    }

    [Fact]
    public void Firewall_network_names_are_read_from_the_file()
    {
        var settings = Catalog[FirewallCheck.CheckId].ParametersAs<FirewallCheck.Settings>();

        Assert.Equal("public networks, like in cafés", settings.NetworkNames[NetworkProfile.Public]);
    }

    [Fact]
    public void The_shipped_remote_tools_list_loads_and_has_no_problems()
    {
        var list = RemoteToolList.LoadEmbedded();

        Assert.Empty(list.Validate());
        Assert.Contains(list.Tools, t => t.Name == "AnyDesk");
    }

    [Fact]
    public async Task A_healthy_PC_gets_the_all_clear_headline()
    {
        var results = await new SafetyCheckRunner(Checks, Catalog, new MemoryLog()).RunAsync();

        Assert.DoesNotContain(results, r => r.Status.NeedsAction() || r.Status == CheckStatus.Unknown);
        Assert.Equal(Catalog.Summary.AllClear, SafetyCheckSummary.Headline(results, Catalog.Summary));
    }

    [GeneratedRegex(@"\{[A-Za-z]")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\bPro\b|\bupgrade to\b|\bbuy\b|\bpurchase\b|\blicen[cs]e\b|£", RegexOptions.IgnoreCase)]
    private static partial Regex ProOrPaying();
}

using System.Text.RegularExpressions;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Fixes;

/// <summary>Checks on the real data/fixes.json that ships in the app.</summary>
public partial class ShippedFixCatalogTests
{
    private static readonly FixCatalog Catalog = FixCatalog.LoadEmbedded();
    private static readonly CheckCatalog Checks = CheckCatalog.LoadEmbedded();
    private static readonly BrowserPolicyReader Policies = new(new FakeRegistry(), new BrowserLocations("", "", []));
    private static readonly IReadOnlyList<IFix> Fixes = FixSet.Create(new FakeChanger(), "C:", Policies);

    /// <summary>The wording a fix's result comes from: checks.json, or browser-check.json for the browser fixes ("chrome-managed").</summary>
    private static OutcomeDefinition OutcomeFor(string checkId, string code) =>
        checkId.EndsWith("-managed", StringComparison.Ordinal)
            ? BrowserKit.Text.Catalog[BrowserFindings.Managed].Outcomes[code]
            : Checks[checkId].Outcomes[code];

    private static bool HasOutcome(string checkId, string code) =>
        checkId.EndsWith("-managed", StringComparison.Ordinal)
            ? BrowserKit.Text.Catalog[BrowserFindings.Managed].Outcomes.ContainsKey(code)
            : Checks[checkId].Outcomes.ContainsKey(code);

    public static TheoryData<string> FixIds() => [.. Catalog.Fixes.Select(f => f.Id)];

    [Fact]
    public void Loads_with_no_problems()
    {
        Assert.Empty(Catalog.Validate());
    }

    [Fact]
    public void Every_fix_in_the_app_has_wording_and_every_fix_in_the_file_is_in_the_app()
    {
        Assert.Equal(Fixes.Select(f => f.Id).Order(), Catalog.Fixes.Select(f => f.Id).Order());
    }

    [Theory]
    [MemberData(nameof(FixIds))]
    public void Every_change_a_fix_can_make_has_wording(string id)
    {
        var fix = Fixes.Single(f => f.Id == id);

        Assert.Equal(fix.ChangeKeys.Order(), Catalog[id].Changes.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(FixIds))]
    public void Fixes_are_offered_only_for_outcomes_that_exist(string id)
    {
        var fix = Catalog[id];

        Assert.All(fix.ForOutcomes, o => Assert.True(HasOutcome(fix.CheckId, o), $"{id} is offered for {fix.CheckId}.{o}, which has no wording"));
    }

    [Theory]
    [MemberData(nameof(FixIds))]
    public void Fixes_are_offered_for_things_that_need_doing_apart_from_the_free_quick_scan(string id)
    {
        if (id == QuickScanFix.FixId)
        {
            return;
        }

        var fix = Catalog[id];
        Assert.All(fix.ForOutcomes, o => Assert.True(OutcomeFor(fix.CheckId, o).Status.NeedsAction(), $"{id} is offered on a result that doesn't need it: {o}"));
    }

    [Fact]
    public async Task Changes_that_can_be_undone_in_the_app_say_what_undoing_does()
    {
        var changer = new FakeChanger();
        foreach (var fix in FixSet.Create(changer, "C:", Policies))
        {
            foreach (var change in (await fix.PlanAsync(Catalog[fix.Id], CancellationToken.None)).Where(c => c.Undo == UndoSupport.InApp))
            {
                Assert.False(string.IsNullOrWhiteSpace(change.UndoDescription), $"{fix.Id}.{change.Key} can be undone but has no undoText");
            }
        }
    }

    [Fact]
    public void The_quick_scan_is_offered_whenever_Defender_is_the_antivirus()
    {
        Assert.Equal(["defenderOn", "defenderInfoOld", "defenderInfoVeryOld", "defenderRealTimeOff"], Catalog[QuickScanFix.FixId].ForOutcomes);
        Assert.Equal("Run a quick scan with Microsoft Defender", Catalog[QuickScanFix.FixId].ButtonLabel);
    }

    // Trust rule 2: fixes are free, and nothing near them mentions Pro or paying.
    [Fact]
    public void No_fix_wording_mentions_Pro_or_paying()
    {
        var texts = Catalog.Fixes.SelectMany(f => new[] { f.ButtonLabel, f.Title, f.DoneText, f.FailedText }
            .Concat(f.Changes.Values.SelectMany(c => new[] { c.Text, c.UndoText ?? "" })));

        Assert.All(texts, t => Assert.False(ProOrPaying().IsMatch(t), $"Mentions Pro or paying: \"{t}\""));
    }

    [Fact]
    public void Preview_says_how_each_change_can_be_undone()
    {
        var common = Catalog.Common;

        Assert.Equal(common.UndoInApp, FixPreview.UndoNote(common, UndoSupport.InApp));
        Assert.Equal(common.UndoViaSettings, FixPreview.UndoNote(common, UndoSupport.ViaSettings));
        Assert.Equal(common.UndoNotNeeded, FixPreview.UndoNote(common, UndoSupport.NotNeeded));
        Assert.Contains("never switch it off", common.UndoViaSettings);
    }

    [Fact]
    public void Preview_in_test_mode_says_so_and_its_button_says_nothing_will_change()
    {
        var change = new SettingChange { Key = "smb1Off", Kind = ChangeKind.WindowsFeature, Target = "SMB1Protocol", From = "Enabled", To = "Disabled", NeedsRestart = true, Undo = UndoSupport.InApp, Description = "Switch off SMB1" };

        var normal = FixPreview.ForFix(Catalog.Common, Catalog["smb1-off"], [change], isDryRun: false);
        var test = FixPreview.ForFix(Catalog.Common, Catalog["smb1-off"], [change], isDryRun: true);

        Assert.Equal(Catalog.Common.ConfirmButton, normal.ConfirmButton);
        Assert.Equal(Catalog.Common.DryRunConfirmButton, test.ConfirmButton);
        Assert.Equal(Catalog.Common.DryRunIntro, test.Intro);
        Assert.Equal(Catalog.Common.RestartNeeded, normal.RestartNote);
        Assert.Equal("Switch off SMB1", normal.Items[0].Description);
        Assert.Contains("SMB1Protocol", normal.Items[0].TechnicalDetail);
        Assert.False(normal.IsConfirmed);
    }

    [GeneratedRegex(@"\bPro\b|\bupgrade\b|\bbuy\b|\bpurchase\b|\blicen[cs]e\b|£", RegexOptions.IgnoreCase)]
    private static partial Regex ProOrPaying();
}

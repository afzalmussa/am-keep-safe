using AMKeepSafe.Core.Features;

namespace AMKeepSafe.Tests.Features;

/// <summary>Trust rule 10: safety features are never behind the paywall.</summary>
public class FeatureCatalogTests
{
    public static TheoryData<Feature> SafetyFeatures() => [.. FeatureCatalog.SafetyFeatures];

    public static TheoryData<Feature> AllFeatures() => [.. Enum.GetValues<Feature>()];

    [Theory]
    [MemberData(nameof(SafetyFeatures))]
    public void Safety_features_never_need_Pro(Feature feature)
    {
        Assert.False(FeatureCatalog.NeedsPro(feature));
        Assert.True(FeatureCatalog.IsAvailable(feature, isPro: false));
    }

    [Fact]
    public void The_safety_features_named_in_the_brief_are_all_on_the_free_list()
    {
        Feature[] fromBrief =
        [
            Feature.SafetyCheck, Feature.IndividualFixes, Feature.Undo, Feature.ScamHelp,
            Feature.BrowserCheck, Feature.BrowserCleanup, Feature.SendToAmSoftware,
        ];

        Assert.Subset(FeatureCatalog.SafetyFeatures.ToHashSet(), fromBrief.ToHashSet());
    }

    [Fact]
    public void Only_the_five_Pro_conveniences_need_Pro()
    {
        var pro = Enum.GetValues<Feature>().Where(FeatureCatalog.NeedsPro).ToArray();

        Assert.Equal(
            [Feature.ScheduledWeeklyCheck, Feature.CheckHistory, Feature.FixEverythingRecommended, Feature.OutdatedApps, Feature.PrintableReport],
            pro);
    }

    [Theory]
    [MemberData(nameof(AllFeatures))]
    public void Every_feature_is_either_a_safety_feature_or_Pro_never_both_or_neither(Feature feature)
    {
        Assert.NotEqual(FeatureCatalog.SafetyFeatures.Contains(feature), FeatureCatalog.NeedsPro(feature));
    }

    [Theory]
    [MemberData(nameof(AllFeatures))]
    public void Pro_unlocks_everything(Feature feature)
    {
        Assert.True(FeatureCatalog.IsAvailable(feature, isPro: true));
    }
}

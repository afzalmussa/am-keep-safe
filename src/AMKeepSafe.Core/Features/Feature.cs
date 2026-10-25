namespace AMKeepSafe.Core.Features;

/// <summary>Everything the app offers, so which ones need Pro is decided in one place and can be tested.</summary>
public enum Feature
{
    // Safety features. Always free (trust rule 10).
    SafetyCheck,
    IndividualFixes,
    DefenderQuickScan,
    Undo,
    BrowserCheck,
    BrowserCleanup,
    ScamHelp,
    SendToAmSoftware,
    ViewLog,

    // Pro conveniences.
    ScheduledWeeklyCheck,
    CheckHistory,
    FixEverythingRecommended,
    OutdatedApps,
    PrintableReport,
}

/// <summary>Which features need Pro. Anything that helps someone fix a problem or get help stays free.</summary>
public static class FeatureCatalog
{
    /// <summary>The features trust rule 10 says must never be behind the paywall.</summary>
    public static IReadOnlyList<Feature> SafetyFeatures { get; } =
    [
        Feature.SafetyCheck,
        Feature.IndividualFixes,
        Feature.DefenderQuickScan,
        Feature.Undo,
        Feature.BrowserCheck,
        Feature.BrowserCleanup,
        Feature.ScamHelp,
        Feature.SendToAmSoftware,
        Feature.ViewLog,
    ];

    public static bool NeedsPro(Feature feature) => feature switch
    {
        Feature.ScheduledWeeklyCheck => true,
        Feature.CheckHistory => true,
        Feature.FixEverythingRecommended => true,
        Feature.OutdatedApps => true,
        Feature.PrintableReport => true,
        _ => false,
    };

    /// <summary>
    /// The one question the UI asks before running a feature. A safety feature answers yes whatever
    /// the licence says, so a licence problem can never stop someone fixing their PC.
    /// </summary>
    public static bool IsAvailable(Feature feature, bool isPro) => isPro || !NeedsPro(feature);
}

namespace AMKeepSafe.Core.Checks;

/// <summary>The calm headline above the results. No counts, no alarm (trust rules 1 and 3).</summary>
public static class SafetyCheckSummary
{
    public static string Headline(IReadOnlyCollection<CheckResult> results, SummaryText text)
    {
        var headline = results.Any(r => r.Status.NeedsAction()) ? text.SomeToLookAt : text.AllClear;
        return results.Any(r => r.Status == CheckStatus.Unknown) ? $"{headline} {text.SomeNotChecked}" : headline;
    }

    /// <summary>Most important first, so nobody has to scroll to find what needs doing. Ties keep the catalog order.</summary>
    public static IReadOnlyList<CheckResult> InDisplayOrder(IEnumerable<CheckResult> results) =>
        results.Select((r, i) => (Result: r, Index: i))
            .OrderBy(x => x.Result.Status)
            .ThenBy(x => x.Index)
            .Select(x => x.Result)
            .ToList();
}

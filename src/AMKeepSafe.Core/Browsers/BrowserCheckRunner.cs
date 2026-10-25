using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Logging;

namespace AMKeepSafe.Core.Browsers;

/// <summary>One profile of one browser, with its results.</summary>
/// <param name="Label">For example "Google Chrome" or "Google Chrome: Work".</param>
public sealed record BrowserSection(Browser Browser, string Label, IReadOnlyList<CheckResult> Results);

/// <param name="Note">A note to show above the results, for example that a different account's browsers were checked.</param>
public sealed record BrowserCheckReport(IReadOnlyList<BrowserSection> Sections, string? Note)
{
    public bool NoBrowsers => Sections.Count == 0;

    public IEnumerable<CheckResult> AllResults => Sections.SelectMany(s => s.Results);
}

/// <summary>
/// Checks Edge, Chrome and Firefox for the account the app runs as. Only reads. Every finding goes
/// in the log. The last report is kept, so the Browser check tab can show what the Safety check found.
/// </summary>
public sealed class BrowserCheckRunner(
    ChromiumReader chromium,
    FirefoxReader firefox,
    BrowserPolicyReader policies,
    IPcContextInfo pcContext,
    SearchEngineList engines,
    KnownBadExtensionList knownBad,
    BrowserText text,
    IAppLog log)
{
    public BrowserText Text => text;

    public BrowserCheckReport? LastReport { get; private set; }

    public event Action<BrowserCheckReport>? Finished;

    public async Task<BrowserCheckReport> RunAsync(CancellationToken cancellationToken = default)
    {
        log.Info("Browser check started.");

        PcContext context;
        try
        {
            context = await pcContext.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Warning("Couldn't tell whether this is a work PC or which account is signed in.", ex);
            context = PcContext.Unknown;
        }

        // File reading only, but it can be slow on a busy disk, so keep it off the screen's thread.
        var sections = await Task.Run(() => Scan(context), cancellationToken).ConfigureAwait(false);

        string? note = null;
        if (context.IsDifferentAccount)
        {
            note = CheckCatalog.Fill(text.Notes.DifferentAccount, new Dictionary<string, string> { ["runningAs"] = context.RunningAs, ["signedIn"] = context.SignedInUser! });
            log.Warning($"Running as {context.RunningAs}, but {context.SignedInUser} is signed in, so {context.RunningAs}'s browsers were checked.");
        }
        else if (sections.Count == 0)
        {
            note = text.Notes.NoBrowsers;
        }

        var report = new BrowserCheckReport(sections, note);
        log.Info($"Browser check finished: {sections.Count} browser profiles. {string.Join(", ", report.AllResults.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"))}");
        LastReport = report;
        Finished?.Invoke(report);
        return report;
    }

    private List<BrowserSection> Scan(PcContext context)
    {
        var sections = new List<BrowserSection>();
        foreach (var browser in new[] { Browser.Edge, Browser.Chrome, Browser.Firefox })
        {
            var installed = browser == Browser.Firefox ? firefox.IsInstalledForThisAccount : chromium.IsInstalledForThisAccount(browser);
            if (!installed)
            {
                log.Info($"{text.NameOf(browser)}: not used on this account.");
                continue;
            }

            var found = policies.Read(browser);
            var overrides = policies.Overrides(browser);
            var profiles = browser == Browser.Firefox
                ? firefox.ReadProfiles(overrides)
                : chromium.ReadProfiles(browser, overrides, policies.ForcedExtensionIds(browser));

            var name = text.NameOf(browser);
            var managed = Render(browser, BrowserFindings.Managed, $"{Id(browser)}-managed",
                BrowserFindings.EvaluateManaged(found, context.IsWorkOrSchoolPc, Settings<BrowserFindings.ManagedSettings>(BrowserFindings.Managed)));

            if (profiles.Count == 0)
            {
                sections.Add(new BrowserSection(browser, name, [managed]));
                continue;
            }

            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                var prefix = $"{Id(browser)}-{profile.ProfileKey.ToLowerInvariant().Replace(' ', '-')}";
                var results = new List<CheckResult>();
                if (i == 0)
                {
                    results.Add(managed);
                }

                if (profile.ReadProblem is not null)
                {
                    log.Warning($"{name} profile {profile.ProfileKey} couldn't be read: {profile.ReadProblem}");
                    results.Add(Render(browser, BrowserFindings.Unreadable, $"{prefix}-unreadable", new CheckOutcome(BrowserFindings.Codes.CouldNotRead)));
                }
                else
                {
                    results.Add(Render(browser, BrowserFindings.Search, $"{prefix}-search", BrowserFindings.EvaluateSearch(profile.Search, engines)));
                    results.Add(Render(browser, BrowserFindings.StartPages, $"{prefix}-start-pages", BrowserFindings.EvaluateStartPages(profile.StartPages)));
                    results.Add(Render(browser, BrowserFindings.Extensions, $"{prefix}-extensions",
                        BrowserFindings.EvaluateExtensions(profile.Extensions, knownBad, Settings<BrowserFindings.ExtensionSettings>(BrowserFindings.Extensions))));
                    results.Add(Render(browser, BrowserFindings.Notifications, $"{prefix}-notifications", BrowserFindings.EvaluateNotifications(profile.NotificationSites)));
                }

                var label = profiles.Count > 1 ? $"{name}: {profile.ProfileName}" : name;
                sections.Add(new BrowserSection(browser, label, results));
            }
        }

        return sections;
    }

    /// <summary>Words a finding, gives it an id unique to this browser and profile, and points its buttons at this browser.</summary>
    private CheckResult Render(Browser browser, string finding, string id, CheckOutcome outcome)
    {
        var values = new Dictionary<string, string>(outcome.Values) { ["browser"] = text.NameOf(browser) };
        var result = text.Catalog.Render(text.Catalog[finding], outcome with { Values = values });
        log.Write(result.Status.NeedsAction() ? LogLevel.Warning : LogLevel.Info, $"Browser check '{id}': {result.Status} ({result.OutcomeCode}). {outcome.Detail}");
        return result with
        {
            CheckId = id,
            Title = CheckCatalog.Fill(result.Title, values),
            Actions = result.Actions.Select(a => a.BrowserPage is null ? a : a with { Browser = browser }).ToList(),
        };
    }

    private T Settings<T>(string finding) where T : new() => text.Catalog[finding].ParametersAs<T>();

    private static string Id(Browser browser) => browser.ToString().ToLowerInvariant();
}

/// <summary>The Browser check's one line on the Safety check tab.</summary>
public sealed class BrowserSummaryCheck(BrowserCheckRunner runner) : ISafetyCheck
{
    public const string CheckId = "browsers";

    public const string AllClear = "allClear";
    public const string SomethingToLook = "somethingToLook";
    public const string NoBrowsers = "noBrowsers";

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [AllClear, SomethingToLook, NoBrowsers];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Evaluate(await runner.RunAsync(cancellationToken).ConfigureAwait(false));

    public static CheckOutcome Evaluate(BrowserCheckReport report)
    {
        var results = report.AllResults.ToList();
        var detail = $"{report.Sections.Count} browser profiles checked.";
        if (report.NoBrowsers)
        {
            return new CheckOutcome(NoBrowsers, Detail: detail);
        }

        if (results.Any(r => r.Status.NeedsAction()))
        {
            return new CheckOutcome(SomethingToLook, Detail: detail);
        }

        return results.All(r => r.Status == CheckStatus.Unknown)
            ? new CheckOutcome(CheckCatalog.CouldNotCheckCode, Detail: detail)
            : new CheckOutcome(AllClear, Detail: detail);
    }
}

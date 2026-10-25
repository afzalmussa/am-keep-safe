using AMKeepSafe.Core.Platform;
using AMKeepSafe.Core.Text;

namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>What Windows says about itself, from the registry.</summary>
public sealed record WindowsVersionFacts(string? ProductName, string? EditionId, string? DisplayVersion, int Build)
{
    public const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static WindowsVersionFacts Read(IRegistryReader registry)
    {
        var build = registry.GetString(RegistryRoot.LocalMachine, CurrentVersionKey, "CurrentBuild");
        return new WindowsVersionFacts(
            registry.GetString(RegistryRoot.LocalMachine, CurrentVersionKey, "ProductName"),
            registry.GetString(RegistryRoot.LocalMachine, CurrentVersionKey, "EditionID"),
            registry.GetString(RegistryRoot.LocalMachine, CurrentVersionKey, "DisplayVersion"),
            int.TryParse(build, out var b) ? b : 0);
    }

    public bool IsWindows11 => Build >= 22000;

    public bool IsOrganisationEdition =>
        Contains(EditionId, "Enterprise") || Contains(EditionId, "Education") || Contains(EditionId, "IoT") || Contains(EditionId, "Server");

    public bool IsLongTermServicing =>
        Contains(EditionId, "EnterpriseS") || Contains(ProductName, "LTSC") || Contains(ProductName, "LTSB");

    /// <summary>Windows 10 and 11 Home editions, which can't accept Remote Desktop connections.</summary>
    public bool IsHomeEdition => EditionId?.StartsWith("Core", StringComparison.OrdinalIgnoreCase) == true;

    private static bool Contains(string? text, string part) => text?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Whether this version of Windows still gets security updates. The dates live in checks.json,
/// because Microsoft changes them (it extended Windows 10's consumer updates in 2026).
/// </summary>
public sealed class WindowsVersionCheck(IRegistryReader registry, IWindowsUpdateInfo updates, TimeProvider time) : ISafetyCheck
{
    public const string CheckId = "windows-version";

    public const string Win11Supported = "win11Supported";
    public const string Win11EndingSoon = "win11EndingSoon";
    public const string Win11Ended = "win11Ended";
    public const string Win10GettingUpdates = "win10GettingUpdates";
    public const string Win10NotGettingUpdates = "win10NotGettingUpdates";
    public const string Win10OldVersion = "win10OldVersion";
    public const string Win10EsuEnded = "win10EsuEnded";
    public const string Win10Organisation = "win10Organisation";
    public const string LongTermServicing = "longTermServicing";
    public const string OlderWindows = "olderWindows";

    public sealed record Windows11Version
    {
        public int Build { get; init; }

        public string Version { get; init; } = "";

        public DateOnly HomeProEnds { get; init; }

        public DateOnly EnterpriseEnds { get; init; }
    }

    public sealed record Windows10Dates
    {
        /// <summary>22H2, the only Windows 10 version that can get Extended Security Updates.</summary>
        public int LastVersionBuild { get; init; } = 19045;

        public DateOnly SupportEnded { get; init; } = new(2025, 10, 14);

        public DateOnly ConsumerEsuEnds { get; init; } = new(2027, 10, 12);
    }

    public sealed record Settings
    {
        /// <summary>An update installed this recently counts as "still getting updates".</summary>
        public int RecentUpdateDays { get; init; } = 45;

        public int WarnDaysBeforeEnd { get; init; } = 60;

        public Windows10Dates Windows10 { get; init; } = new();

        public IReadOnlyList<Windows11Version> Windows11 { get; init; } = [];
    }

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes =>
    [
        Win11Supported, Win11EndingSoon, Win11Ended, Win10GettingUpdates, Win10NotGettingUpdates,
        Win10OldVersion, Win10EsuEnded, Win10Organisation, LongTermServicing, OlderWindows,
    ];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken)
    {
        var facts = WindowsVersionFacts.Read(registry);
        if (facts.Build == 0)
        {
            throw new CouldNotReadException("Windows didn't say which build it is.");
        }

        DateTimeOffset? lastInstalled = null;
        if (!facts.IsWindows11)
        {
            // Only Windows 10 needs this: recent updates are the best sign it's signed up for Extended Security Updates.
            lastInstalled = (await updates.GetStatusAsync(cancellationToken).ConfigureAwait(false)).LastInstalled;
        }

        return Evaluate(facts, lastInstalled, time.GetUtcNow(), definition.ParametersAs<Settings>());
    }

    public static CheckOutcome Evaluate(WindowsVersionFacts facts, DateTimeOffset? lastInstalled, DateTimeOffset now, Settings settings)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var detail = $"{facts.ProductName} ({facts.EditionId}) {facts.DisplayVersion}, build {facts.Build}. " +
                     $"Last update installed: {lastInstalled?.ToString("u") ?? "not checked"}.";
        var w10 = settings.Windows10;
        var values = new Dictionary<string, string>
        {
            ["version"] = string.IsNullOrWhiteSpace(facts.DisplayVersion) ? $"build {facts.Build}" : facts.DisplayVersion.Trim(),
            ["windows10SupportEnded"] = FriendlyText.Date(w10.SupportEnded),
            ["esuEnds"] = FriendlyText.Date(w10.ConsumerEsuEnds),
        };

        if (facts.IsLongTermServicing)
        {
            return new CheckOutcome(LongTermServicing, values, Detail: detail);
        }

        if (facts.IsWindows11)
        {
            // The newest listed version at or below this build. A build newer than anything listed is a newer version.
            var known = settings.Windows11.OrderBy(v => v.Build).ToList();
            var match = known.LastOrDefault(v => v.Build <= facts.Build);
            if (match is null || (match == known[^1] && facts.Build > match.Build))
            {
                return new CheckOutcome(Win11Supported, values, Detail: detail + " Newer than the versions listed.");
            }

            var ends = facts.IsOrganisationEdition ? match.EnterpriseEnds : match.HomeProEnds;
            values["version"] = match.Version;
            values["endDate"] = FriendlyText.Date(ends);

            if (today > ends)
            {
                return new CheckOutcome(Win11Ended, values, Detail: detail);
            }

            return new CheckOutcome(today > ends.AddDays(-settings.WarnDaysBeforeEnd) ? Win11EndingSoon : Win11Supported, values, Detail: detail);
        }

        if (facts.Build < 10240)
        {
            return new CheckOutcome(OlderWindows, values, Detail: detail);
        }

        if (facts.Build < w10.LastVersionBuild)
        {
            return new CheckOutcome(Win10OldVersion, values, Detail: detail);
        }

        if (facts.IsOrganisationEdition)
        {
            return new CheckOutcome(Win10Organisation, values, Detail: detail);
        }

        if (today > w10.ConsumerEsuEnds)
        {
            return new CheckOutcome(Win10EsuEnded, values, Detail: detail);
        }

        var recent = lastInstalled is { } last && (now - last).TotalDays <= settings.RecentUpdateDays;
        return new CheckOutcome(recent ? Win10GettingUpdates : Win10NotGettingUpdates, values, Detail: detail);
    }
}

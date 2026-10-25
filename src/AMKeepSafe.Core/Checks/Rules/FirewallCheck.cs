namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>Windows keeps separate firewall settings for each kind of network.</summary>
public enum NetworkProfile
{
    Domain,
    Private,
    Public,
}

/// <param name="Profiles">Whether Windows Firewall is on for each kind of network. Missing means unknown.</param>
/// <param name="OtherFirewalls">Firewalls from other companies that Security Center says are switched on.</param>
public sealed record FirewallFacts(IReadOnlyDictionary<NetworkProfile, bool> Profiles, IReadOnlyList<string> OtherFirewalls);

public interface IFirewallInfo
{
    Task<FirewallFacts> GetAsync(CancellationToken cancellationToken);
}

public sealed class FirewallCheck(IFirewallInfo info) : ISafetyCheck
{
    public const string CheckId = "firewall";

    public const string AllOn = "allOn";
    public const string OtherFirewall = "otherFirewall";
    public const string SomeOff = "someOff";
    public const string AllOff = "allOff";

    /// <summary>How each kind of network is described to the user. From checks.json.</summary>
    public sealed record Settings
    {
        public Dictionary<NetworkProfile, string> NetworkNames { get; init; } = new()
        {
            [NetworkProfile.Domain] = "work networks",
            [NetworkProfile.Private] = "home networks",
            [NetworkProfile.Public] = "public networks",
        };
    }

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [AllOn, OtherFirewall, SomeOff, AllOff];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Evaluate(await info.GetAsync(cancellationToken).ConfigureAwait(false), definition.ParametersAs<Settings>());

    public static CheckOutcome Evaluate(FirewallFacts facts, Settings settings)
    {
        var detail = $"Windows Firewall: {string.Join(", ", facts.Profiles.Select(p => $"{p.Key} {(p.Value ? "on" : "off")}"))}. " +
                     $"Other firewalls on: {(facts.OtherFirewalls.Count == 0 ? "none" : string.Join(", ", facts.OtherFirewalls))}.";

        if (facts.Profiles.Count == 0)
        {
            return new CheckOutcome(CheckCatalog.CouldNotCheckCode, Detail: detail);
        }

        var off = facts.Profiles.Where(p => !p.Value).Select(p => p.Key).Order().ToList();
        if (off.Count == 0)
        {
            return new CheckOutcome(AllOn, Detail: detail);
        }

        if (facts.OtherFirewalls.Count > 0)
        {
            return new CheckOutcome(OtherFirewall, new Dictionary<string, string> { ["name"] = facts.OtherFirewalls[0] }, Detail: detail);
        }

        var names = off.Select(p => settings.NetworkNames.TryGetValue(p, out var n) ? n : p.ToString()).ToList();
        var values = new Dictionary<string, string> { ["networks"] = Text.FriendlyText.List(names) };
        return new CheckOutcome(off.Count == facts.Profiles.Count ? AllOff : SomeOff, values, Detail: detail);
    }
}

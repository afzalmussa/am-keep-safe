using AMKeepSafe.Core.Text;

namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>An antivirus registered with Windows Security Center.</summary>
public sealed record AntivirusProduct(string Name, bool IsOn, bool IsUpToDate)
{
    public bool IsMicrosoftDefender =>
        Name.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase);
}

/// <param name="RealTimeProtectionOn">Checking files as they're opened.</param>
/// <param name="RunningMode">"Normal" when Defender is the main antivirus, "Passive Mode" when another one is.</param>
public sealed record DefenderStatus(bool AntivirusEnabled, bool RealTimeProtectionOn, DateTimeOffset? VirusInfoUpdated, string? RunningMode);

/// <param name="Products">Null if Security Center couldn't be asked.</param>
/// <param name="Defender">Null if Defender's status couldn't be read, for example it's switched off by another antivirus.</param>
public sealed record AntivirusFacts(IReadOnlyList<AntivirusProduct>? Products, DefenderStatus? Defender);

public interface IAntivirusInfo
{
    Task<AntivirusFacts> GetAsync(CancellationToken cancellationToken);
}

public sealed class AntivirusCheck(IAntivirusInfo info, TimeProvider time) : ISafetyCheck
{
    public const string CheckId = "antivirus";

    public const string DefenderOn = "defenderOn";
    public const string DefenderInfoOld = "defenderInfoOld";
    public const string DefenderInfoVeryOld = "defenderInfoVeryOld";
    public const string DefenderRealTimeOff = "defenderRealTimeOff";
    public const string OtherOn = "otherOn";
    public const string OtherOutOfDate = "otherOutOfDate";
    public const string NoneOn = "noneOn";

    public sealed record Settings
    {
        public int AmberAfterDays { get; init; } = 3;

        public int RedAfterDays { get; init; } = 14;
    }

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [DefenderOn, DefenderInfoOld, DefenderInfoVeryOld, DefenderRealTimeOff, OtherOn, OtherOutOfDate, NoneOn];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Evaluate(await info.GetAsync(cancellationToken).ConfigureAwait(false), time.GetUtcNow(), definition.ParametersAs<Settings>());

    public static CheckOutcome Evaluate(AntivirusFacts facts, DateTimeOffset now, Settings settings)
    {
        var products = facts.Products ?? [];
        var detail = $"Security Center: {(facts.Products is null ? "no answer" : string.Join(", ", products.Select(p => $"{p.Name} (on: {p.IsOn}, up to date: {p.IsUpToDate})")))}. " +
                     (facts.Defender is { } d
                         ? $"Defender: antivirus {d.AntivirusEnabled}, real-time {d.RealTimeProtectionOn}, mode {d.RunningMode}, virus information {d.VirusInfoUpdated?.ToString("u") ?? "unknown"}."
                         : "Defender: no answer.");

        if (facts.Products is null && facts.Defender is null)
        {
            return new CheckOutcome(CheckCatalog.CouldNotCheckCode, Detail: detail);
        }

        // Another antivirus that's switched on is in charge, and Defender steps back. That's normal.
        var others = products.Where(p => !p.IsMicrosoftDefender && p.IsOn).ToList();
        if (others.Count > 0)
        {
            var current = others.FirstOrDefault(p => p.IsUpToDate);
            var name = (current ?? others[0]).Name;
            return new CheckOutcome(current is null ? OtherOutOfDate : OtherOn, new Dictionary<string, string> { ["name"] = name }, Detail: detail);
        }

        var defender = facts.Defender;
        var defenderOnInSecurityCenter = products.Any(p => p.IsMicrosoftDefender && p.IsOn);
        if (defender is null)
        {
            // No Defender details, but Security Center may still vouch for it.
            return new CheckOutcome(defenderOnInSecurityCenter ? DefenderOn : NoneOn, Detail: detail);
        }

        if (!defender.AntivirusEnabled)
        {
            return new CheckOutcome(NoneOn, Detail: detail);
        }

        if (!defender.RealTimeProtectionOn)
        {
            return new CheckOutcome(DefenderRealTimeOff, Detail: detail);
        }

        if (defender.VirusInfoUpdated is not { } updated)
        {
            return new CheckOutcome(DefenderOn, Detail: detail);
        }

        var values = new Dictionary<string, string>
        {
            ["updatedAgo"] = FriendlyText.Ago(updated, now),
            ["updatedDate"] = FriendlyText.Date(updated),
        };
        var age = (now - updated).TotalDays;
        var code = age > settings.RedAfterDays ? DefenderInfoVeryOld : age > settings.AmberAfterDays ? DefenderInfoOld : DefenderOn;
        return new CheckOutcome(code, values, Detail: detail);
    }
}

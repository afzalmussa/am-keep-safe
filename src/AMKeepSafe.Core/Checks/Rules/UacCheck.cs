using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>
/// User Account Control: the "Do you want to allow this app to make changes?" prompt.
/// </summary>
public sealed class UacCheck(IRegistryReader registry) : ISafetyCheck
{
    public const string CheckId = "user-account-control";
    public const string PolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    public const string On = "on";
    public const string NeverNotify = "neverNotify";
    public const string Off = "off";

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [On, NeverNotify, Off];

    public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(
            registry.GetInt(RegistryRoot.LocalMachine, PolicyKey, "EnableLUA"),
            registry.GetInt(RegistryRoot.LocalMachine, PolicyKey, "ConsentPromptBehaviorAdmin")));

    /// <param name="enableLua">0 turns UAC off completely. Missing means Windows' default, which is on.</param>
    /// <param name="consentPromptBehaviorAdmin">0 means "never notify": changes happen without asking.</param>
    public static CheckOutcome Evaluate(int? enableLua, int? consentPromptBehaviorAdmin)
    {
        var detail = $"EnableLUA {enableLua?.ToString() ?? "not set"}, ConsentPromptBehaviorAdmin {consentPromptBehaviorAdmin?.ToString() ?? "not set"}.";
        if (enableLua == 0)
        {
            return new CheckOutcome(Off, Detail: detail);
        }

        return new CheckOutcome(consentPromptBehaviorAdmin == 0 ? NeverNotify : On, Detail: detail);
    }
}

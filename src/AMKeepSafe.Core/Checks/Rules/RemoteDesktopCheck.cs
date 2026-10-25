using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>Remote Desktop lets other computers connect to and control this one.</summary>
public sealed class RemoteDesktopCheck(IRegistryReader registry) : ISafetyCheck
{
    public const string CheckId = "remote-desktop";
    public const string TerminalServerKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";

    public const string Off = "off";
    public const string On = "on";

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [Off, On];

    public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(
            registry.GetInt(RegistryRoot.LocalMachine, TerminalServerKey, "fDenyTSConnections"),
            WindowsVersionFacts.Read(registry).IsHomeEdition));

    /// <param name="denyConnections">1 (or missing) means connections are refused. 0 means Remote Desktop is on.</param>
    /// <param name="isHomeEdition">Home editions can't accept Remote Desktop connections at all.</param>
    public static CheckOutcome Evaluate(int? denyConnections, bool isHomeEdition)
    {
        var detail = $"fDenyTSConnections {denyConnections?.ToString() ?? "not set"}, Home edition: {isHomeEdition}.";
        return new CheckOutcome(!isHomeEdition && denyConnections == 0 ? On : Off, Detail: detail);
    }
}

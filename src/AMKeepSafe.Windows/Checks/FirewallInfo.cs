using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Checks;

public sealed class FirewallInfo(IPowerShellRunner powerShell) : IFirewallInfo
{
    private const string Script = """
        $result = [ordered]@{ profiles = $null; others = @() }
        try {
            $result.profiles = @(Get-NetFirewallProfile -ErrorAction Stop |
                ForEach-Object { [ordered]@{ name = [string]$_.Name; enabled = ([string]$_.Enabled -eq 'True') } })
        } catch {}
        try {
            $result.others = @(Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName 'FirewallProduct' -ErrorAction Stop |
                ForEach-Object { [ordered]@{ name = [string]$_.displayName; state = [int]$_.productState } })
        } catch {}
        $result | ConvertTo-Json -Compress -Depth 4
        """;

    public async Task<FirewallFacts> GetAsync(CancellationToken cancellationToken) =>
        FactParsers.Firewall(await PowerShellJson.RunAsync(powerShell, Script, "Checking the firewall", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
}

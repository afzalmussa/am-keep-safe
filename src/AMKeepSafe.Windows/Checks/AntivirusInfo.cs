using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Checks;

/// <summary>Microsoft Defender's status, and every antivirus registered with Windows Security Center.</summary>
public sealed class AntivirusInfo(IPowerShellRunner powerShell) : IAntivirusInfo
{
    private const string Script = """
        $result = [ordered]@{ defender = $null; products = $null }
        try {
            $status = Get-MpComputerStatus -ErrorAction Stop
            $updated = $null
            if ($status.AntivirusSignatureLastUpdated) { $updated = ([datetime]$status.AntivirusSignatureLastUpdated).ToUniversalTime().ToString('o') }
            $result.defender = [ordered]@{
                antivirusEnabled = [bool]$status.AntivirusEnabled
                realTimeProtectionEnabled = [bool]$status.RealTimeProtectionEnabled
                signaturesUpdated = $updated
                amRunningMode = [string]$status.AMRunningMode
            }
        } catch {}
        try {
            $result.products = @(Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName 'AntiVirusProduct' -ErrorAction Stop |
                ForEach-Object { [ordered]@{ name = [string]$_.displayName; state = [int]$_.productState } })
        } catch {}
        $result | ConvertTo-Json -Compress -Depth 4
        """;

    public async Task<AntivirusFacts> GetAsync(CancellationToken cancellationToken) =>
        FactParsers.Antivirus(await PowerShellJson.RunAsync(powerShell, Script, "Checking Microsoft Defender and other antivirus", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
}

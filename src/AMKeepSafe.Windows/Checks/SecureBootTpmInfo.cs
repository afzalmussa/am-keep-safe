using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Checks;

public sealed class SecureBootTpmInfo(IPowerShellRunner powerShell) : ISecureBootTpmInfo
{
    private const string Script = """
        $result = [ordered]@{ secureBoot = 'unknown'; tpmPresent = $null; tpmReady = $null; tpmVersion = $null }
        try {
            if (Confirm-SecureBootUEFI -ErrorAction Stop) { $result.secureBoot = 'on' } else { $result.secureBoot = 'off' }
        } catch [System.PlatformNotSupportedException] {
            $result.secureBoot = 'notSupported'
        } catch {
            if ($_.Exception.Message -match 'not supported') { $result.secureBoot = 'notSupported' }
        }
        try {
            $tpm = Get-CimInstance -Namespace 'root/cimv2/Security/MicrosoftTpm' -ClassName 'Win32_Tpm' -ErrorAction Stop
            if ($tpm) {
                $result.tpmPresent = $true
                $result.tpmReady = ([bool]$tpm.IsEnabled_InitialValue -and [bool]$tpm.IsActivated_InitialValue)
                $result.tpmVersion = [string]$tpm.SpecVersion
            } else {
                $result.tpmPresent = $false
            }
        } catch {}
        $result | ConvertTo-Json -Compress
        """;

    public async Task<SecureBootTpmFacts> GetAsync(CancellationToken cancellationToken) =>
        FactParsers.SecureBootTpm(await PowerShellJson.RunAsync(powerShell, Script, "Checking Secure Boot and the TPM chip", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
}

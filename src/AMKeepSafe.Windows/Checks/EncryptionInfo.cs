using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Checks;

/// <summary>Device encryption or BitLocker on the Windows drive. Reads only: never asks for or shows the recovery key.</summary>
public sealed class EncryptionInfo(IPowerShellRunner powerShell) : IEncryptionInfo
{
    private const string Script = """
        $result = [ordered]@{ available = $false; protectionStatus = $null; conversionStatus = $null; error = $null }
        try {
            $volume = Get-CimInstance -Namespace 'root/cimv2/Security/MicrosoftVolumeEncryption' -ClassName 'Win32_EncryptableVolume' `
                -Filter "DriveLetter='$($env:SystemDrive)'" -ErrorAction Stop
            if ($volume) {
                $result.available = $true
                $result.protectionStatus = [int]$volume.ProtectionStatus
                $result.conversionStatus = [int](Invoke-CimMethod -InputObject $volume -MethodName 'GetConversionStatus' -ErrorAction Stop).ConversionStatus
            }
        } catch { $result.error = $_.Exception.Message }
        $result | ConvertTo-Json -Compress
        """;

    public async Task<EncryptionFacts> GetAsync(CancellationToken cancellationToken) =>
        FactParsers.Encryption(await PowerShellJson.RunAsync(powerShell, Script, "Checking drive encryption", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
}

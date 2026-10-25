using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Checks;

public sealed class Smb1Info(IPowerShellRunner powerShell) : ISmb1Info
{
    private const string Script = """
        $result = [ordered]@{ feature = $null; serverSmb1 = $null }
        try { $result.feature = [string](Get-WindowsOptionalFeature -Online -FeatureName 'SMB1Protocol' -ErrorAction Stop).State } catch {}
        try { $result.serverSmb1 = [bool](Get-SmbServerConfiguration -ErrorAction Stop).EnableSMB1Protocol } catch {}
        $result | ConvertTo-Json -Compress
        """;

    public async Task<Smb1Facts> GetAsync(CancellationToken cancellationToken) =>
        FactParsers.Smb1(await PowerShellJson.RunAsync(powerShell, Script, "Checking the old SMB1 file-sharing setting", TimeSpan.FromMinutes(1.5), cancellationToken).ConfigureAwait(false));
}

using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Checks;

public sealed class WindowsUpdateInfo(IPowerShellRunner powerShell) : IWindowsUpdateInfo
{
    private const string StatusScript = """
        $result = [ordered]@{ serviceStart = $null; lastInstalled = $null; noAutoUpdate = $null; pausedUntil = $null }
        try { $result.serviceStart = [string](Get-Service -Name wuauserv -ErrorAction Stop).StartType } catch {}

        # Windows Update's own record, in UTC. Falls back to the newest installed update if it's missing.
        try {
            $last = (New-Object -ComObject Microsoft.Update.AutoUpdate).Results.LastInstallationSuccessDate
            if ($last -and ([datetime]$last).Year -gt 2000) { $result.lastInstalled = [datetime]::SpecifyKind([datetime]$last, 'Utc').ToString('o') }
        } catch {}
        if (-not $result.lastInstalled) {
            try {
                $newest = Get-HotFix -ErrorAction Stop | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending | Select-Object -First 1
                if ($newest) { $result.lastInstalled = ([datetime]$newest.InstalledOn).ToUniversalTime().ToString('o') }
            } catch {}
        }

        $policy = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -ErrorAction SilentlyContinue
        if ($policy -and $null -ne $policy.NoAutoUpdate) { $result.noAutoUpdate = [int]$policy.NoAutoUpdate }

        $ux = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings' -ErrorAction SilentlyContinue
        if ($ux -and $ux.PauseUpdatesExpiryTime) {
            try { $result.pausedUntil = [datetime]::Parse([string]$ux.PauseUpdatesExpiryTime, [Globalization.CultureInfo]::InvariantCulture, 'RoundtripKind').ToUniversalTime().ToString('o') } catch {}
        }

        $result | ConvertTo-Json -Compress
        """;

    private const string WaitingScript = """
        $result = [ordered]@{ count = $null; error = $null }
        try {
            $searcher = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher()
            $result.count = [int]$searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'").Updates.Count
        } catch { $result.error = $_.Exception.Message }
        $result | ConvertTo-Json -Compress
        """;

    public async Task<WindowsUpdateStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        FactParsers.WindowsUpdateStatus(await PowerShellJson.RunAsync(powerShell, StatusScript, "Reading Windows Update settings and history", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));

    public async Task<int?> CountWaitingUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Can go online, so it gets longer than most. Not knowing the count is fine.
            var json = await PowerShellJson.RunAsync(powerShell, WaitingScript, "Asking Windows Update what's waiting to install", TimeSpan.FromMinutes(2.5), cancellationToken).ConfigureAwait(false);
            return FactParsers.WaitingUpdates(json);
        }
        catch (AMKeepSafe.Core.Checks.CouldNotReadException)
        {
            return null;
        }
    }
}

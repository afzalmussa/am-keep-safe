using System.Security.Principal;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks.Parsing;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Windows.Checks;

namespace AMKeepSafe.Windows.Browsers;

/// <summary>Whether this PC is looked after by a workplace or school, and who's signed in at the screen.</summary>
public sealed class PcContextInfo(IPowerShellRunner powerShell) : IPcContextInfo
{
    private const string Script = """
        $result = [ordered]@{ partOfDomain = $false; userName = $null; azureAdJoined = $false; mdmEnrolled = $false }
        try {
            $computer = Get-CimInstance -ClassName 'Win32_ComputerSystem' -ErrorAction Stop
            $result.partOfDomain = [bool]$computer.PartOfDomain
            $result.userName = [string]$computer.UserName
        } catch {}
        $result.azureAdJoined = @(Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo' -ErrorAction SilentlyContinue).Count -gt 0
        $result.mdmEnrolled = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Enrollments' -ErrorAction SilentlyContinue |
            Where-Object { (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).ProviderID }).Count -gt 0
        $result | ConvertTo-Json -Compress
        """;

    public async Task<PcContext> GetAsync(CancellationToken cancellationToken)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var json = await PowerShellJson.RunAsync(powerShell, Script, "Checking whether this is a work or school PC", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        return FactParsers.PcContext(json, identity.Name);
    }
}

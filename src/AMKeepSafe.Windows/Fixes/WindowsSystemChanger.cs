using System.Globalization;
using System.Text.Json;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;
using Microsoft.Win32;

namespace AMKeepSafe.Windows.Fixes;

/// <summary>
/// Reads and changes Windows settings for the fixes and undo. Every value passed to PowerShell is
/// quoted as a literal, and each kind of change only accepts its own fixed set of values.
/// </summary>
public sealed class WindowsSystemChanger(IPowerShellRunner powerShell, IRegistryReader registry) : ISystemChanger
{
    private static readonly TimeSpan Quick = TimeSpan.FromMinutes(2);

    public async Task<string?> ReadAsync(SettingChange change, CancellationToken cancellationToken)
    {
        switch (change.Kind)
        {
            case ChangeKind.RegistryValue:
                return registry.GetValue(change.Root!.Value, change.RegistryKey!, change.Target) switch
                {
                    int i => i.ToString(CultureInfo.InvariantCulture),
                    long l => l.ToString(CultureInfo.InvariantCulture),
                    null => null,
                    var other => other.ToString(),
                };

            case ChangeKind.ServiceStart:
                return registry.GetValue(RegistryRoot.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{change.Target}", "Start") switch
                {
                    2 => "Automatic",
                    3 => "Manual",
                    4 => "Disabled",
                    _ => null,
                };

            case ChangeKind.FirewallProfile:
                return OnOff(await RunAsync(
                    $"$result.value = [string](Get-NetFirewallProfile -Name {PowerShellQuoting.Literal(change.Target)} -ErrorAction Stop).Enabled",
                    $"Reading the firewall for {change.Target} networks", Quick, cancellationToken).ConfigureAwait(false), "True");

            case ChangeKind.DefenderRealTime:
                return OnOff(await RunAsync(
                    "$result.value = [string](Get-MpPreference -ErrorAction Stop).DisableRealtimeMonitoring",
                    "Reading Microsoft Defender real-time protection", Quick, cancellationToken).ConfigureAwait(false), "False");

            case ChangeKind.WindowsFeature:
                var state = await RunAsync(
                    $"$result.value = [string](Get-WindowsOptionalFeature -Online -FeatureName {PowerShellQuoting.Literal(change.Target)} -ErrorAction Stop).State",
                    $"Reading the Windows feature {change.Target}", Quick, cancellationToken).ConfigureAwait(false);
                return state switch
                {
                    "Enabled" or "EnablePending" => "Enabled",
                    "Disabled" or "DisablePending" or "DisabledWithPayloadRemoved" => "Disabled",
                    _ => null,
                };

            case ChangeKind.SystemRestore:
                return BackupCheck.Read(registry).SystemRestoreOn switch
                {
                    true => "on",
                    false => "off",
                    null => null,
                };

            default:
                return null;
        }
    }

    public async Task ApplyAsync(SettingChange change, CancellationToken cancellationToken)
    {
        switch (change.Kind)
        {
            case ChangeKind.RegistryValue:
                SetRegistryValue(change);
                break;

            case ChangeKind.ServiceStart:
                await RunAsync(
                    $"Set-Service -Name {PowerShellQuoting.Literal(change.Target)} -StartupType {OneOf(change.To, "Automatic", "Manual", "Disabled")} -ErrorAction Stop",
                    $"Changing the start setting of the {change.Target} service", Quick, cancellationToken).ConfigureAwait(false);
                break;

            case ChangeKind.FirewallProfile:
                await RunAsync(
                    $"Set-NetFirewallProfile -Name {PowerShellQuoting.Literal(change.Target)} -Enabled {(OneOf(change.To, "on", "off") == "on" ? "True" : "False")} -ErrorAction Stop",
                    $"Changing the firewall for {change.Target} networks", Quick, cancellationToken).ConfigureAwait(false);
                break;

            case ChangeKind.DefenderRealTime:
                await RunAsync(
                    $"Set-MpPreference -DisableRealtimeMonitoring {(OneOf(change.To, "on", "off") == "on" ? "$false" : "$true")} -ErrorAction Stop",
                    "Changing Microsoft Defender real-time protection", Quick, cancellationToken).ConfigureAwait(false);
                break;

            case ChangeKind.WindowsFeature:
                var feature = PowerShellQuoting.Literal(change.Target);
                var command = OneOf(change.To, "Enabled", "Disabled") == "Enabled"
                    ? $"Enable-WindowsOptionalFeature -Online -FeatureName {feature} -All -NoRestart -ErrorAction Stop | Out-Null"
                    : $"Disable-WindowsOptionalFeature -Online -FeatureName {feature} -NoRestart -ErrorAction Stop | Out-Null";
                await RunAsync(command, $"Changing the Windows feature {change.Target}", TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
                break;

            case ChangeKind.SystemRestore:
                var drive = PowerShellQuoting.Literal(change.Target);
                await RunAsync(
                    OneOf(change.To, "on", "off") == "on" ? $"Enable-ComputerRestore -Drive {drive} -ErrorAction Stop" : $"Disable-ComputerRestore -Drive {drive} -ErrorAction Stop",
                    $"Changing System Restore on {change.Target}", TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                break;

            case ChangeKind.Task when change.Target == QuickScanFix.TaskName:
                await RunAsync("Start-MpScan -ScanType QuickScan -ErrorAction Stop", "Running a Microsoft Defender quick scan", TimeSpan.FromMinutes(60), cancellationToken).ConfigureAwait(false);
                break;

            case ChangeKind.Task when change.Target == UpdateVirusInfoFix.TaskName:
                await RunAsync("Update-MpSignature -ErrorAction Stop", "Updating Microsoft Defender's virus information", TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException($"Don't know how to make this change: {change.TechnicalDetail}");
        }
    }

    private static void SetRegistryValue(SettingChange change)
    {
        var hive = change.Root == RegistryRoot.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        if (change.To is null)
        {
            using var existing = baseKey.OpenSubKey(change.RegistryKey!, writable: true);
            existing?.DeleteValue(change.Target, throwOnMissingValue: false);
            return;
        }

        if (!int.TryParse(change.To, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new ArgumentException($"Only number values are set in the registry, not \"{change.To}\".");
        }

        using var key = baseKey.CreateSubKey(change.RegistryKey!, writable: true)
            ?? throw new InvalidOperationException($"Couldn't open {change.RegistryKey} to change it.");
        key.SetValue(change.Target, number, RegistryValueKind.DWord);
    }

    /// <summary>Runs a script inside a wrapper that reports success or the error as JSON. Throws on failure.</summary>
    private async Task<string?> RunAsync(string body, string purpose, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var script = $$"""
            $result = [ordered]@{ ok = $false; value = $null; error = $null }
            try {
                {{body}}
                $result.ok = $true
            } catch { $result.error = $_.Exception.Message }
            $result | ConvertTo-Json -Compress
            """;

        var run = await powerShell.RunAsync(script, purpose, timeout, cancellationToken).ConfigureAwait(false);
        if (run.TimedOut)
        {
            throw new TimeoutException($"Windows took too long: {purpose}.");
        }

        var json = PowerShellOutput.LastJsonLine(run.Output) ?? throw new InvalidOperationException($"No answer from Windows: {purpose}. {run.Errors}");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : "unknown error";
            throw new InvalidOperationException($"Windows refused: {purpose}. {error}");
        }

        return root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? OnOff(string? value, string onWhen) =>
        value is null ? null : string.Equals(value, onWhen, StringComparison.OrdinalIgnoreCase) ? "on" : "off";

    private static string OneOf(string? value, params string[] allowed) =>
        allowed.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"\"{value}\" isn't one of: {string.Join(", ", allowed)}.");
}

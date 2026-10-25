using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Checks;

internal static class PowerShellJson
{
    /// <summary>Runs a script that ends with ConvertTo-Json and returns that JSON, or says why it couldn't.</summary>
    public static async Task<string> RunAsync(IPowerShellRunner powerShell, string script, string purpose, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var run = await powerShell.RunAsync(script, purpose, timeout, cancellationToken).ConfigureAwait(false);
        if (run.TimedOut)
        {
            throw new CouldNotReadException($"Windows took too long to answer: {purpose}.");
        }

        return PowerShellOutput.LastJsonLine(run.Output)
            ?? throw new CouldNotReadException($"No answer from Windows: {purpose}. Exit code {run.ExitCode}. {run.Errors}");
    }
}

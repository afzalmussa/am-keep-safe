using System.Diagnostics;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Platform;

public sealed class WindowsProcessList : IProcessList
{
    public IReadOnlyCollection<string> RunningProgramNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                // ProcessName leaves off ".exe". Put it back so it matches the names in remote-tools.json.
                names.Add(process.ProcessName + ".exe");
            }
        }

        return names;
    }
}

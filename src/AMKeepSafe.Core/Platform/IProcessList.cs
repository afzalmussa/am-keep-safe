namespace AMKeepSafe.Core.Platform;

/// <summary>The names of programs running right now, such as "AnyDesk.exe".</summary>
public interface IProcessList
{
    IReadOnlyCollection<string> RunningProgramNames();
}

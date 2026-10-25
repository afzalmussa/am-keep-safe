namespace AMKeepSafe.Core.Fixes;

/// <summary>
/// Reads and changes Windows settings. The only place in the app that changes anything, so it can be
/// faked in tests. Values use the same words as <see cref="SettingChange.From"/> and <see cref="SettingChange.To"/>.
/// </summary>
public interface ISystemChanger
{
    /// <summary>The setting's value now. Null for a registry value that isn't set, or if it can't be read.</summary>
    Task<string?> ReadAsync(SettingChange change, CancellationToken cancellationToken);

    /// <summary>Sets the value to <see cref="SettingChange.To"/>, or runs the task. Throws if Windows refuses.</summary>
    Task ApplyAsync(SettingChange change, CancellationToken cancellationToken);
}

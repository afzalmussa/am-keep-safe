using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Fixes;

/// <summary>
/// Removes the policy settings controlling a browser ("Managed by your organisation"): the registry
/// keys for the whole PC and for this account, and Firefox's policies.json. Each is backed up first
/// and can be put back from the undo list.
/// </summary>
public sealed class RemoveBrowserPoliciesFix(Browser browser, BrowserPolicyReader policies) : IFix
{
    public static string IdFor(Browser browser) => $"remove-policies-{browser.ToString().ToLowerInvariant()}";

    public string Id => IdFor(browser);

    public IReadOnlyCollection<string> ChangeKeys => browser == Browser.Firefox ? ["machine", "account", "file"] : ["machine", "account"];

    public Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken)
    {
        var changes = new List<SettingChange>();
        foreach (var location in policies.Read(browser))
        {
            var key = location.Place == PolicyPlace.File ? "file" : location.Root == RegistryRoot.LocalMachine ? "machine" : "account";
            var words = text.TextFor(key);
            changes.Add(new SettingChange
            {
                Key = key,
                Kind = location.Place == PolicyPlace.File ? ChangeKind.File : ChangeKind.RegistryKey,
                Root = location.Root,
                RegistryKey = location.Place == PolicyPlace.Registry ? location.Path : null,
                Target = location.Place == PolicyPlace.File ? location.Path : "*",
                From = SettingChange.Present,
                To = SettingChange.Absent,
                Contents = location.Settings,
                Description = words.Text,
                UndoDescription = words.UndoText ?? "",
                Undo = UndoSupport.InApp,
            });
        }

        return Task.FromResult<IReadOnlyList<SettingChange>>(changes);
    }
}

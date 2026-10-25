using AMKeepSafe.Core.Fixes;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.Services;

/// <summary>Shows windows and messages. Behind an interface so view models can be tested without a screen.</summary>
public interface IDialogService
{
    void ShowMessage(string title, string message);

    /// <summary>
    /// Shows exactly what will change. Returns true, and confirms the preview, only if the person
    /// clicks the confirm button (trust rule 4). Cancel is the default button.
    /// </summary>
    bool ConfirmPreview(FixPreview preview);

    void ShowUndo(UndoViewModel undo);
}

using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IDialogService
{
    bool Confirm(string message, string title);

    void ShowMessage(string message, string title);

    void OpenChangelog(IEnumerable<UpdateStage> stages, UpdateStage? selectedStage);

    void OpenExternal(string pathOrUrl, string errorTitle = "AI Updater");
}

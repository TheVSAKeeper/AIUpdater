using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IUpdaterDialogs
{
    Task<bool> ConfirmAsync(string message, string title, string acceptText = "Подтвердить");
    void ShowMessage(string message, string title);
    void OpenChangelog(UpdateStage? selectedStage = null);
    void OpenExternal(string pathOrUrl, string errorTitle = "AI Updater");
}

using AIUpdater.Models;
using AIUpdater.ViewModels;
using KeepShell.Services.Platform;

namespace AIUpdater.Services;

public sealed class UpdaterDialogs(KeepShell.Services.IDialogService dialogs, IShellLauncher shell, IAppNavigator navigator) : IUpdaterDialogs
{
    public Task<bool> ConfirmAsync(string message, string title, string acceptText = "Подтвердить") => dialogs.ShowAsync(new MessageDialogViewModel(title, message, true, acceptText));
    public void ShowMessage(string message, string title) => _ = dialogs.ShowAsync(new MessageDialogViewModel(title, message, false));
    public void OpenChangelog(UpdateStage? selectedStage = null) => navigator.ShowChangelog(selectedStage);
    public void OpenSettings() => navigator.ShowSettings();
    public void OpenExternal(string pathOrUrl, string errorTitle = "AI Updater")
    {
        if (!shell.Open(pathOrUrl)) ShowMessage("Не удалось открыть путь или ссылку. Подробности записаны в журнал приложения.", errorTitle);
    }
}

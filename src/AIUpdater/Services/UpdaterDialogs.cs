using AIUpdater.Bootstrap;
using AIUpdater.Models;
using AIUpdater.ViewModels;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUpdater.Services;

public sealed class UpdaterDialogs(
    KeepShell.Services.IDialogService dialogs,
    IShellLauncher shell,
    IAppNavigator navigator,
    ILogger<UpdaterDialogs>? logger = null) : IUpdaterDialogs
{
    private readonly ILogger _logger = logger ?? NullLogger<UpdaterDialogs>.Instance;

    public Task<bool> ConfirmAsync(string message, string title, string acceptText = "Подтвердить") => dialogs.ShowAsync(new MessageDialogViewModel(title, message, true, acceptText));
    public void ShowMessage(string message, string title) => _ = dialogs.ShowAsync(new MessageDialogViewModel(title, message, false));
    public void OpenDotNetManager() => navigator.ShowDotNet();
    public void OpenChangelog(UpdateStage? selectedStage = null) => navigator.ShowChangelog(selectedStage);
    public void OpenExternal(string pathOrUrl, string errorTitle = "AI Updater")
    {
        if (shell.Open(pathOrUrl)) return;
        _logger.ExternalTargetOpenFailed(pathOrUrl);
        ShowMessage("Не удалось открыть путь или ссылку. Подробности записаны в журнал приложения.", errorTitle);
    }
}

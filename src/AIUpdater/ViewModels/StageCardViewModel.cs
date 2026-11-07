using AIUpdater.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIUpdater.ViewModels;

public sealed class StageCardViewModel : ObservableObject
{
    public StageCardViewModel(
        UpdateStage stage,
        Func<UpdateStage, Task> onSelectOrCheck,
        Func<UpdateStage, Task> onCheck,
        Func<UpdateStage, Task> onUpdate,
        Func<UpdateStage, Task> onInstall,
        Func<UpdateStage, Task> onUninstall,
        Action<UpdateStage> onOpenChangelog)
    {
        Stage = stage;
        SelectOrCheckCommand = new AsyncRelayCommand(() => onSelectOrCheck(stage));
        CheckCommand = new AsyncRelayCommand(() => onCheck(stage));
        UpdateCommand = new AsyncRelayCommand(() => onUpdate(stage));
        InstallCommand = new AsyncRelayCommand(() => onInstall(stage));
        UninstallCommand = new AsyncRelayCommand(() => onUninstall(stage));
        OpenChangelogCommand = new RelayCommand(() => onOpenChangelog(stage));
    }

    public UpdateStage Stage { get; }

    public IAsyncRelayCommand SelectOrCheckCommand { get; }

    public IAsyncRelayCommand CheckCommand { get; }

    public IAsyncRelayCommand UpdateCommand { get; }

    public IAsyncRelayCommand InstallCommand { get; }

    public IAsyncRelayCommand UninstallCommand { get; }

    public IRelayCommand OpenChangelogCommand { get; }
}

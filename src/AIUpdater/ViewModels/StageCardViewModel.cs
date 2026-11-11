using AIUpdater.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIUpdater.ViewModels;

public sealed class StageCardViewModel : ObservableObject
{
    private readonly Func<string?> _operationDetail;

    public StageCardViewModel(
        UpdateStage stage,
        Func<UpdateStage, Task> onSelectOrCheck,
        Func<UpdateStage, Task> onCheck,
        Func<UpdateStage, Task> onUpdate,
        Func<UpdateStage, Task> onInstall,
        Func<UpdateStage, Task> onUninstall,
        Action<UpdateStage> onOpenChangelog,
        Func<string?>? operationDetail = null)
    {
        Stage = stage;
        _operationDetail = operationDetail ?? (() => null);
        stage.PropertyChanged += (_, _) => RefreshOperationDetail();
        SelectOrCheckCommand = new AsyncRelayCommand(() => onSelectOrCheck(stage));
        CheckCommand = new AsyncRelayCommand(() => onCheck(stage));
        UpdateCommand = new AsyncRelayCommand(() => onUpdate(stage));
        InstallCommand = new AsyncRelayCommand(() => onInstall(stage));
        UninstallCommand = new AsyncRelayCommand(() => onUninstall(stage));
        OpenChangelogCommand = new RelayCommand(() => onOpenChangelog(stage));
    }

    public UpdateStage Stage { get; }

    public string OperationDetail
    {
        get
        {
            if (!Stage.IsActiveOperation || Stage.OperationState == OperationState.Queued) return "";
            var detail = _operationDetail() ?? "";
            if (detail.StartsWith(Stage.Name, StringComparison.Ordinal)) detail = detail[Stage.Name.Length..].TrimStart(' ', ':', '·');
            return detail;
        }
    }

    internal void RefreshOperationDetail() => OnPropertyChanged(nameof(OperationDetail));

    public IAsyncRelayCommand SelectOrCheckCommand { get; }

    public IAsyncRelayCommand CheckCommand { get; }

    public IAsyncRelayCommand UpdateCommand { get; }

    public IAsyncRelayCommand InstallCommand { get; }

    public IAsyncRelayCommand UninstallCommand { get; }

    public IRelayCommand OpenChangelogCommand { get; }
}

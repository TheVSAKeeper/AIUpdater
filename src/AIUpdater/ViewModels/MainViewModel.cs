using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace AIUpdater.ViewModels;

using KeepShell.ViewModels;

public sealed class MainViewModel : ObservableObject, IPageHeader, IPageStatus, IPageRefresh
{
    private readonly IUpdaterExecutionService _executionService;
    private readonly IUpdaterDialogs _dialogService;
    private CancellationTokenSource? _runCancellation;
    private UpdateStage[] _runStages = [];
    private string _action = "Update";
    private bool _checkOnly;
    private bool _stopRequested;
    private TaskCompletionSource? _runCompletion;

    [ActivatorUtilitiesConstructor]
    public MainViewModel(IUpdaterExecutionService executionService, IUpdaterDialogs dialogService)
    {
        _executionService = executionService;
        _dialogService = dialogService;
        LogPanel = new LogPanelViewModel(Vendors, _executionService, _dialogService);

        CheckAllCommand = new AsyncRelayCommand(() => RunUpdaterAsync(null), () => CanStartRun);
        UpdateSelectedCommand = new AsyncRelayCommand(UpdateSelectedStagesAsync, () => CanUpdateSelected);
        StopCommand = new RelayCommand(RequestStop, () => IsRunning);
        OpenChangelogCommand = new RelayCommand<object>(OpenChangelogFromTab);

        foreach (var stage in Stages)
        {
            stage.PropertyChanged += (_, e) =>
            {
                RefreshUpdateButtonState();
                RefreshOperationProgress();
                OnPropertyChanged(nameof(SummaryText));
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(UpdateStage.OperationState)) OnPropertyChanged(nameof(RunningCards));
            };
        }

        foreach (var vendor in Stages.Select(stage => stage.Vendor).Distinct())
        {
            var vendorStages = Stages.Where(stage => stage.Vendor == vendor).ToArray();
            var cards = vendorStages
                .Select(stage => new StageCardViewModel(
                    stage,
                    SelectOrCheckStageAsync,
                    s => RunUpdaterAsync(s),
                    s => RunUpdaterAsync(s, checkOnly: false),
                    s => ExecuteComponentActionAsync(s, "Install"),
                    s => ExecuteComponentActionAsync(s, "Uninstall"),
                    s => _dialogService.OpenChangelog(s),
                    () => Vendors.FirstOrDefault(vendorStatus => vendorStatus.Name == vendor)?.Detail))
                .ToArray();

            var status = new VendorStatus(vendor, _executionService.GetVendorLogPath(vendor)) { Stages = vendorStages, Cards = cards };
            status.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(VendorStatus.Detail)) return;
                OnPropertyChanged(nameof(ActiveOperationText));
                foreach (var card in cards) card.RefreshOperationDetail();
            };
            Vendors.Add(status);
            if (_executionService.ReadPreviousLog(status.LogPath, "История прошлого запуска\n") is { } previousVendorLog)
            {
                status.Log = previousVendorLog;
            }
        }

        LogPanel.LoadLastLog();
        LoadResults();
        RefreshUpdateButtonState();
    }

    public ObservableCollection<VendorStatus> Vendors { get; } = [];
    public ObservableCollection<UpdateStage> Stages { get; } = new(UpdateStage.CreateCatalog());
    public IEnumerable<StageCardViewModel> RunningCards => Vendors.SelectMany(vendor => vendor.Cards).Cast<StageCardViewModel>()
        .Where(card => card.Stage.IsActiveOperation && card.Stage.OperationState != OperationState.Queued);
    public bool ShowActiveOnly { get; set => SetProperty(ref field, value); }
    public LogPanelViewModel LogPanel { get; }
    public string ScriptPath => _executionService.ScriptPath;
    public string LogPath => _executionService.LogPath;
    public string ResultsPath => _executionService.ResultsPath;
    public bool CanStartRun => !IsRunning;
    public IAsyncRelayCommand CheckAllCommand { get; }
    public IAsyncRelayCommand UpdateSelectedCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand<object> OpenChangelogCommand { get; }
    public string OverallStatusText
    {
        get;
        set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(StatusCaption)); }
    } = "Готов к работе";
    public string PageTitle => "Обновления";
    public string PageDescription => "Проверка и обновление инструментов";
    public string StatusCaption => IsRunning ? ProgressCaption : OverallStatusText;
    public bool IsBusy => IsRunning;
    public bool IsIndeterminate => false;
    public int RunTotal => _runStages.Length;
    public int QueuedCount => _runStages.Count(stage => stage.OperationState == OperationState.Queued);
    public int ActiveCount => _runStages.Count(stage => stage.IsActiveOperation && stage.OperationState != OperationState.Queued);
    public int CompletedCount => RunTotal - QueuedCount - ActiveCount;
    public double ProgressValue => CompletedCount;
    public double ProgressMax => Math.Max(1, RunTotal);
    public string ProgressCaption => $"Выполнено {CompletedCount} из {RunTotal} · В работе: {ActiveCount} · В очереди: {QueuedCount}";
    public string OperationTitle => _checkOnly ? "Проверка версий" : UpdaterExecutionService.GetOperationName(CurrentOperationState);
    public string ActiveOperationText => string.Join(Environment.NewLine,
        _runStages.Where(stage => stage.IsActiveOperation && stage.OperationState != OperationState.Queued).Select(stage =>
        {
            var detail = Vendors.FirstOrDefault(vendor => vendor.Name == stage.Vendor)?.Detail ?? stage.Name;
            return $"{stage.Vendor}: " + (detail.StartsWith(stage.Name, StringComparison.Ordinal) ? detail : $"{stage.Name} · {detail}");
        }));
    public string SummaryText => $"Компонентов: {Stages.Count} · Обновлений: {Stages.Count(stage => stage.HasUpdate)} · Выбрано: {Stages.Count(stage => stage.CanSelectForUpdate && stage.IsSelectedForUpdate)}";
    public System.Windows.Input.ICommand CancelCommand => StopCommand;
    public System.Windows.Input.ICommand RefreshCommand => CheckAllCommand;
    public string RefreshTooltip => "Проверить все инструменты";
    public string UpdateButtonText { get; private set => SetProperty(ref field, value); } = "Обновить выбранные (0)";

    public bool CanUpdateSelected
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                UpdateSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRunning
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                if (!value) ShowActiveOnly = false;
                OnPropertyChanged(nameof(CanStartRun));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsIndeterminate));
                CheckAllCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
                RefreshUpdateButtonState();
                RefreshOperationProgress();
            }
        }
    }

    private OperationState CurrentOperationState => UpdaterExecutionService.GetOperationState(_action, _checkOnly);

    private void RefreshOperationProgress()
    {
        foreach (var property in new[] { nameof(RunTotal), nameof(QueuedCount), nameof(ActiveCount), nameof(CompletedCount), nameof(ProgressValue), nameof(ProgressMax), nameof(ProgressCaption), nameof(ActiveOperationText), nameof(StatusCaption), nameof(OperationTitle) })
            OnPropertyChanged(property);
    }

    public static string CheckSummary(IEnumerable<UpdateStage> stages) => UpdaterOutputParser.BuildCheckSummary(stages);

    public void ConfigurePaths(string? scriptPath = null, string? logPath = null, string? resultsPath = null) =>
        _executionService.ConfigurePaths(scriptPath, logPath, resultsPath);

    public async Task SelectOrCheckStageAsync(UpdateStage? stage)
    {
        if (stage is null || IsRunning)
        {
            return;
        }

        if (stage.CanSelectForUpdate)
        {
            stage.IsSelectedForUpdate = !stage.IsSelectedForUpdate;
            return;
        }

        await RunUpdaterAsync(stage);
    }

    public async Task RunUpdaterAsync(
        UpdateStage? requestedStage,
        string action = "Update",
        bool checkOnly = true,
        UpdateStage[]? selected = null)
    {
        if (IsRunning)
        {
            return;
        }

        if (!_executionService.ScriptExists())
        {
            _dialogService.ShowMessage($"Скрипт не найден:\n{ScriptPath}", "AI Updater");
            return;
        }

        _action = action;
        _checkOnly = action == "Update" && checkOnly;
        _runStages = selected ?? (requestedStage is null ? Stages.ToArray() : [requestedStage]);
        _stopRequested = false;
        var sawError = false;
        var exitCode = -1;
        using var cts = _runCancellation = new CancellationTokenSource();
        _runCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            LogPanel.GeneralLogText = "";
            OverallStatusText = _executionService.PrepareRun(Stages, _runStages, requestedStage, _action, _checkOnly);
            IsRunning = true;
            (exitCode, sawError) = await _executionService.ExecuteRunAsync(
                Stages, Vendors, _runStages, requestedStage, _action, _checkOnly, selected is not null, () => _stopRequested, AppendLine, cts.Token);
        }
        catch (Exception exception)
        {
            AppendLine(exception.ToString());
            sawError = true;
        }
        finally
        {
            OverallStatusText = _executionService.FinishRun(
                Stages, Vendors, _runStages, requestedStage, _action, _checkOnly, exitCode, sawError, _stopRequested, error => AppendLine(error));
            IsRunning = false;
            if (ReferenceEquals(_runCancellation, cts))
            {
                _runCancellation = null;
            }
            _runCompletion.TrySetResult();
        }
    }

    public async Task<bool> RunDotNetPackageAsync(DotNetOperationRequest operation)
    {
        if (IsRunning)
        {
            return false;
        }

        if (!_executionService.ScriptExists())
        {
            _dialogService.ShowMessage($"Скрипт не найден:\n{ScriptPath}", "AI Updater");
            return false;
        }

        var dotNetStage = Stages.FirstOrDefault(stage => stage.Id == ".NET SDK 10");
        _action = operation.ActionScriptName;
        _checkOnly = false;
        _runStages = dotNetStage is not null ? [dotNetStage] : [];
        _stopRequested = false;
        var sawError = false;
        var exitCode = -1;
        using var cts = _runCancellation = new CancellationTokenSource();
        _runCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            LogPanel.GeneralLogText = "";
            dotNetStage?.Queue();
            OverallStatusText = operation.DisplayLabel;
            IsRunning = true;
            (exitCode, sawError) = await _executionService.ExecuteDotNetOperationAsync(
                Stages, Vendors, operation, () => _stopRequested, AppendLine, cts.Token);
        }
        catch (Exception exception)
        {
            AppendLine(exception.ToString());
            sawError = true;
        }
        finally
        {
            OverallStatusText = _executionService.FinishRun(
                Stages, Vendors, _runStages, dotNetStage, _action, false, exitCode, sawError, _stopRequested, error => AppendLine(error));
            IsRunning = false;
            if (ReferenceEquals(_runCancellation, cts))
            {
                _runCancellation = null;
            }
            _runCompletion.TrySetResult();
        }

        return exitCode == 0 && !sawError && !_stopRequested;
    }

    public VendorRun CreateVendorRun(string vendor, params UpdateStage[] stages)
    {
        _runStages = stages.Length > 0 ? stages : Stages.ToArray();
        return new VendorRun(vendor, _runStages);
    }

    public void HandleOutputChunk(string chunk, bool isError, VendorRun run) =>
        _executionService.HandleOutputChunk(chunk, isError, run, CurrentOperationState, Vendors, AppendLine);

    public void HandleOutputLine(string line, bool isError, VendorRun run) =>
        _executionService.HandleOutputLine(line, isError, run, CurrentOperationState, Vendors, AppendLine);

    public void RefreshVendorSummary(VendorStatus vendor) => _executionService.RefreshVendorSummary(vendor);

    public void LoadResults()
    {
        if (_executionService.LoadResults(Stages, Vendors, error => AppendLine(error)))
        {
            OverallStatusText = "Показаны сохранённые результаты проверки";
        }
    }

    public void SaveResults() => _executionService.SaveResults(Stages, error => AppendLine(error));

    private async Task UpdateSelectedStagesAsync()
    {
        var selected = Stages.Where(stage => stage.CanSelectForUpdate && stage.IsSelectedForUpdate).ToArray();
        if (selected.Length > 0)
        {
            await RunUpdaterAsync(null, checkOnly: false, selected: selected);
        }
    }

    private async Task ExecuteComponentActionAsync(UpdateStage? stage, string action)
    {
        if (IsRunning || stage is null || !stage.CanManageInstallation)
        {
            return;
        }

        if (stage.Id == ".NET SDK 10")
        {
            _dialogService.OpenDotNetManager();
            return;
        }

        if (action == "Uninstall" && !await _dialogService.ConfirmAsync($"Удалить {stage.Name}?", "Удаление компонента", "Удалить"))
        {
            return;
        }

        await RunUpdaterAsync(stage, action);
    }

    private void RequestStop()
    {
        if (!IsRunning)
        {
            return;
        }

        _stopRequested = true;
        try
        {
            _runCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        AppendLine("Остановка запрошена пользователем.");
    }

    public Task StopAsync()
    {
        var completion = _runCompletion?.Task ?? Task.CompletedTask;
        RequestStop();
        return completion;
    }

    private void OpenChangelogFromTab(object? parameter)
    {
        var vendor = parameter as VendorStatus ?? LogPanel.SelectedVendor;
        _dialogService.OpenChangelog(vendor?.Stages.FirstOrDefault());
    }

    private void RefreshUpdateButtonState()
    {
        var count = Stages.Count(stage => stage.CanSelectForUpdate && stage.IsSelectedForUpdate);
        UpdateButtonText = $"Обновить выбранные ({count})";
        CanUpdateSelected = !IsRunning && count > 0;
    }

    private void AppendLine(string line, string? vendorName = null) =>
        _executionService.AppendLog(line, vendorName, Vendors, IsRunning, text => LogPanel.GeneralLogText += text);
}

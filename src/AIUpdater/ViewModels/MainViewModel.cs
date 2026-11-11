using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace AIUpdater.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IUpdaterExecutionService _executionService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _runCancellation;
    private UpdateStage[] _runStages = [];
    private string _action = "Update";
    private bool _checkOnly;
    private bool _stopRequested;

    public MainViewModel()
        : this(
            new UpdaterExecutionService(
                new FileStageResultStore(),
                new PowerShellProcessRunner(),
                new UpdaterOutputParser()),
            new WpfDialogService(new ChangelogService()))
    {
    }

    [ActivatorUtilitiesConstructor]
    public MainViewModel(IUpdaterExecutionService executionService, IDialogService dialogService)
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
            stage.PropertyChanged += (_, _) => RefreshUpdateButtonState();
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
                    s => _dialogService.OpenChangelog(Stages, s)))
                .ToArray();

            var status = new VendorStatus(vendor, _executionService.GetVendorLogPath(vendor)) { Stages = vendorStages, Cards = cards };
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
    public LogPanelViewModel LogPanel { get; }
    public string ScriptPath => _executionService.ScriptPath;
    public string LogPath => _executionService.LogPath;
    public string ResultsPath => _executionService.ResultsPath;
    public bool CanStartRun => !IsRunning;
    public IAsyncRelayCommand CheckAllCommand { get; }
    public IAsyncRelayCommand UpdateSelectedCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand<object> OpenChangelogCommand { get; }
    public string OverallStatusText { get; set => SetProperty(ref field, value); } = "Готов к работе";
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
                OnPropertyChanged(nameof(CanStartRun));
                CheckAllCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
                RefreshUpdateButtonState();
            }
        }
    }

    private OperationState CurrentOperationState => UpdaterExecutionService.GetOperationState(_action, _checkOnly);

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
        }
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

        if (action == "Uninstall" && !_dialogService.Confirm($"Удалить {stage.Name}?", "Удаление компонента"))
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

    private void OpenChangelogFromTab(object? parameter)
    {
        var vendor = parameter as VendorStatus ?? LogPanel.SelectedVendor;
        _dialogService.OpenChangelog(Stages, vendor?.Stages.FirstOrDefault());
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

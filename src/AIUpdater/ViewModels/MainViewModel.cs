using AIUpdater.Bootstrap;
using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Bootstrap;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace AIUpdater.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable, IPageHeader, IPageStatus, IPageRefresh
{
    private readonly IUpdaterExecutionService _executionService;
    private readonly IUpdaterDialogs _dialogService;
    private readonly ISettingsStore? _settings;
    private readonly IChangelogService? _changelogService;
    private readonly ILogger _logger;
    private readonly Dictionary<UpdateStage, StageCardViewModel> _cardsByStage = [];
    private readonly Dictionary<UpdateStage, int> _catalogIndex = [];
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _prefetchCancellation;
    private Task _changelogPrefetchTask = Task.CompletedTask;
    private UpdateStage[] _runStages = [];
    private string _action = "Update";
    private bool _checkOnly;
    private bool _stopRequested;
    private TaskCompletionSource? _runCompletion;

    [ActivatorUtilitiesConstructor]
    public MainViewModel(
        IUpdaterExecutionService executionService,
        IUpdaterDialogs dialogService,
        ISettingsStore? settings = null,
        IChangelogService? changelogService = null,
        ILogger<MainViewModel>? logger = null,
        ILogger<LogPanelViewModel>? logPanelLogger = null)
    {
        _executionService = executionService;
        _dialogService = dialogService;
        _settings = settings;
        _changelogService = changelogService;
        _logger = logger ?? NullLogger<MainViewModel>.Instance;
        LogPanel = new LogPanelViewModel(Vendors, _executionService, _dialogService, logPanelLogger);

        CheckAllCommand = new AsyncRelayCommand(() => RunUpdaterAsync(null), () => CanStartRun);
        UpdateSelectedCommand = new AsyncRelayCommand(UpdateSelectedStagesAsync, () => CanUpdateSelected);
        StopCommand = new RelayCommand(RequestStop, () => IsRunning);
        OpenChangelogCommand = new RelayCommand<object>(OpenChangelogFromTab);

        for (var index = 0; index < Stages.Count; index++)
        {
            _catalogIndex[Stages[index]] = index;
        }

        RestorePreferences();

        foreach (var stage in Stages)
        {
            stage.PropertyChanged += (_, e) =>
            {
                RefreshUpdateButtonState();
                RefreshOperationProgress();
                OnPropertyChanged(nameof(SummaryText));
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(UpdateStage.OperationState))
                {
                    OnPropertyChanged(nameof(RunningCards));
                }

                if (e.PropertyName != nameof(UpdateStage.IsSelectedForUpdate))
                {
                    RebuildDisplayGroups();
                }
            };
        }

        foreach (var vendor in Stages.Select(stage => stage.Vendor).Distinct())
        {
            var vendorStages = Stages.Where(stage => stage.Vendor == vendor).ToArray();
            var cards = vendorStages
                .Select(stage =>
                {
                    var card = new StageCardViewModel(
                        stage,
                        SelectOrCheckStageAsync,
                        s => RunUpdaterAsync(s),
                        s => RunUpdaterAsync(s, checkOnly: false),
                        s => ExecuteComponentActionAsync(s, "Install"),
                        s => ExecuteComponentActionAsync(s, "Uninstall"),
                        s => _dialogService.OpenChangelog(s),
                        () => Vendors.FirstOrDefault(vendorStatus => vendorStatus.Name == vendor)?.Detail,
                        ToggleStageDisabled);
                    _cardsByStage[stage] = card;
                    return card;
                })
                .ToArray();

            var status = new VendorStatus(vendor, _executionService.GetVendorLogPath(vendor)) { Stages = vendorStages, Cards = cards };
            status.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(VendorStatus.Detail)) return;
                OnPropertyChanged(nameof(ActiveOperationText));
                OnPropertyChanged(nameof(StatusCaption));
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
        RebuildDisplayGroups();
    }

    public ObservableCollection<VendorStatus> Vendors { get; } = [];
    public ObservableCollection<StageCardGroup> DisplayGroups { get; } = [];
    public ObservableCollection<UpdateStage> Stages { get; } = new(UpdateStage.CreateCatalog());
    public IReadOnlyList<UpdateGroupOption> GroupOptions { get; } =
    [
        new(UpdateGroupMode.ByVendor, "По поставщику"),
        new(UpdateGroupMode.ByState, "По состоянию"),
        new(UpdateGroupMode.Ungrouped, "Без группировки"),
    ];
    public IReadOnlyList<UpdateSortOption> SortOptions { get; } =
    [
        new(UpdateSortMode.Catalog, "По каталогу"),
        new(UpdateSortMode.UpdatesFirst, "Сначала обновления"),
        new(UpdateSortMode.ByName, "По названию"),
    ];
    public IEnumerable<StageCardViewModel> RunningCards => Vendors.SelectMany(vendor => vendor.Cards).Cast<StageCardViewModel>()
        .Where(card => card.Stage.IsActiveOperation && card.Stage.OperationState != OperationState.Queued);

    public bool ShowActiveOnly
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ShowVendorSubtitle));
                OnPropertyChanged(nameof(ShowEmptyFilterHint));
            }
        }
    }

    public UpdateGroupMode GroupMode
    {
        get;
        set
        {
            var normalized = value == UpdateGroupMode.None ? UpdateGroupMode.ByVendor : value;
            if (SetProperty(ref field, normalized))
            {
                _settings?.SetEnum(SettingsKeys.UpdatesGroupMode, normalized);
                OnPropertyChanged(nameof(SelectedGroupOption));
                OnPropertyChanged(nameof(ShowVendorSubtitle));
                RebuildDisplayGroups();
            }
        }
    } = UpdateGroupMode.ByVendor;

    public UpdateGroupOption SelectedGroupOption
    {
        get => GroupOptions.FirstOrDefault(option => option.Mode == GroupMode) ?? GroupOptions[0];
        set
        {
            if (value is not null)
            {
                GroupMode = value.Mode;
            }
        }
    }

    public UpdateSortMode SortMode
    {
        get;
        set
        {
            var normalized = value == UpdateSortMode.None ? UpdateSortMode.Catalog : value;
            if (SetProperty(ref field, normalized))
            {
                _settings?.SetEnum(SettingsKeys.UpdatesSortMode, normalized);
                OnPropertyChanged(nameof(SelectedSortOption));
                RebuildDisplayGroups();
            }
        }
    } = UpdateSortMode.Catalog;

    public UpdateSortOption SelectedSortOption
    {
        get => SortOptions.FirstOrDefault(option => option.Mode == SortMode) ?? SortOptions[0];
        set
        {
            if (value is not null)
            {
                SortMode = value.Mode;
            }
        }
    }

    public bool HideUninstalled
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _settings?.SetBool(SettingsKeys.UpdatesHideUninstalled, value);
                RebuildDisplayGroups();
            }
        }
    }

    public bool HasVisibleCards => DisplayGroups.Count > 0;
    public bool ShowEmptyFilterHint => !IsRunning && !ShowActiveOnly && !HasVisibleCards;
    public bool ShowVendorSubtitle => ShowActiveOnly || GroupMode != UpdateGroupMode.ByVendor;
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
    public string StatusCaption => IsRunning ? BuildRunningStatusCaption() : OverallStatusText;
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
    public string SummaryText
    {
        get
        {
            var updates = Stages.Count(stage => !stage.IsDisabled && stage.HasUpdate);
            var selected = Stages.Count(stage => stage.CanSelectForUpdate && stage.IsSelectedForUpdate);
            var disabled = Stages.Count(stage => stage.IsDisabled);
            return $"Компонентов: {Stages.Count} · Обновлений: {updates} · Выбрано: {selected}"
                   + (disabled > 0 ? $" · Отключено: {disabled}" : "");
        }
    }

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
                OnPropertyChanged(nameof(ShowEmptyFilterHint));
                CheckAllCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
                RefreshUpdateButtonState();
                RefreshOperationProgress();
            }
        }
    }

    private OperationState CurrentOperationState => UpdaterExecutionService.GetOperationState(_action, _checkOnly);

    public void Dispose() => CancelPrefetch();

    public Task WaitForChangelogPrefetchAsync() => _changelogPrefetchTask;

    public UpdateStage[] GetCheckAllStages() => Stages
        .Where(stage => !stage.IsDisabled && (!HideUninstalled || stage.PackageStatus != PackageStatus.NotInstalled))
        .ToArray();

    private void RefreshOperationProgress()
    {
        foreach (var property in new[] { nameof(RunTotal), nameof(QueuedCount), nameof(ActiveCount), nameof(CompletedCount), nameof(ProgressValue), nameof(ProgressMax), nameof(ProgressCaption), nameof(ActiveOperationText), nameof(StatusCaption), nameof(OperationTitle) })
            OnPropertyChanged(property);
    }

    private string BuildRunningStatusCaption()
    {
        if (RunTotal != 1)
        {
            return ProgressCaption;
        }

        var stage = _runStages[0];
        if (!stage.IsActiveOperation || stage.OperationState == OperationState.Queued)
        {
            return ProgressCaption;
        }

        var rawDetail = Vendors.FirstOrDefault(vendor => vendor.Name == stage.Vendor)?.Detail;
        if (string.IsNullOrWhiteSpace(rawDetail))
        {
            return OverallStatusText;
        }

        var singleLine = rawDetail.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        if (singleLine.StartsWith(stage.Name, StringComparison.Ordinal))
        {
            singleLine = singleLine[stage.Name.Length..].TrimStart(' ', ':', '·');
        }

        if (singleLine.Length == 0 || string.Equals(singleLine, OverallStatusText, StringComparison.Ordinal))
        {
            return OverallStatusText;
        }

        return $"{OverallStatusText} · {singleLine}";
    }

    public static string CheckSummary(IEnumerable<UpdateStage> stages) => UpdaterOutputParser.BuildCheckSummary(stages);

    public void ConfigurePaths(string? scriptPath = null, string? logPath = null, string? resultsPath = null) =>
        _executionService.ConfigurePaths(scriptPath, logPath, resultsPath);

    public async Task SelectOrCheckStageAsync(UpdateStage? stage)
    {
        if (stage is null || IsRunning || stage.IsDisabled)
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
        if (IsRunning || requestedStage is { IsDisabled: true })
        {
            return;
        }

        var targetStages = selected is not null
            ? selected.Where(stage => !stage.IsDisabled).ToArray()
            : requestedStage is not null
                ? [requestedStage]
                : GetCheckAllStages();
        if (targetStages.Length == 0)
        {
            OverallStatusText = "Нет активных компонентов для проверки";
            return;
        }

        if (!_executionService.ScriptExists())
        {
            _logger.UpdaterScriptMissing(ScriptPath);
            _dialogService.ShowMessage($"Скрипт не найден:\n{ScriptPath}", "AI Updater");
            return;
        }

        _action = action;
        _checkOnly = action == "Update" && checkOnly;
        _runStages = targetStages;
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
            _logger.UpdaterUnhandledRunException(exception, _action);
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

        if (_changelogService is not null && !_stopRequested)
        {
            StartChangelogPrefetch(requestedStage is not null ? [requestedStage] : Stages.ToArray());
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
            _logger.UpdaterScriptMissing(ScriptPath);
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
            _logger.UpdaterUnhandledRunException(exception, _action);
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

    public void ToggleStageDisabled(UpdateStage? stage)
    {
        if (stage is null || IsRunning)
        {
            return;
        }

        stage.SetDisabled(!stage.IsDisabled);
        PersistDisabledStages();
        if (Vendors.FirstOrDefault(vendor => vendor.Name == stage.Vendor) is { } vendorStatus)
        {
            RefreshVendorSummary(vendorStatus);
        }

        _logger.ComponentDisabledChanged(stage.Id, stage.IsDisabled);
    }

    private async Task UpdateSelectedStagesAsync()
    {
        var selected = Stages.Where(stage => stage.CanSelectForUpdate && stage.IsSelectedForUpdate).ToArray();
        if (selected.Length > 0)
        {
            _logger.BulkUpdateRequested(selected.Length);
            await RunUpdaterAsync(null, checkOnly: false, selected: selected);
        }
    }

    private async Task ExecuteComponentActionAsync(UpdateStage? stage, string action)
    {
        if (IsRunning || stage is null || stage.IsDisabled || !stage.CanManageInstallation)
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
            _logger.ComponentUninstallCancelled(stage.Id);
            return;
        }

        _logger.ComponentActionRequested(stage.Id, action);
        await RunUpdaterAsync(stage, action);
    }

    private void RequestStop()
    {
        if (!IsRunning)
        {
            return;
        }

        _stopRequested = true;
        _logger.UpdaterStopRequested(ActiveCount, QueuedCount);
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

    private void RestorePreferences()
    {
        if (_settings is null)
        {
            return;
        }

        var savedGroup = _settings.GetEnum(SettingsKeys.UpdatesGroupMode, UpdateGroupMode.ByVendor);
        GroupMode = savedGroup == UpdateGroupMode.None ? UpdateGroupMode.ByVendor : savedGroup;
        var savedSort = _settings.GetEnum(SettingsKeys.UpdatesSortMode, UpdateSortMode.Catalog);
        SortMode = savedSort == UpdateSortMode.None ? UpdateSortMode.Catalog : savedSort;
        HideUninstalled = _settings.GetBool(SettingsKeys.UpdatesHideUninstalled);

        var disabledRaw = _settings.GetStringValue(SettingsKeys.UpdatesDisabledStages);
        if (!string.IsNullOrWhiteSpace(disabledRaw))
        {
            try
            {
                var disabledIds = JsonSerializer.Deserialize<string[]>(disabledRaw);
                if (disabledIds is { Length: > 0 })
                {
                    var disabledSet = new HashSet<string>(disabledIds, StringComparer.Ordinal);
                    foreach (var stage in Stages)
                    {
                        if (disabledSet.Contains(stage.Id))
                        {
                            stage.SetDisabled(true);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    private void PersistDisabledStages()
    {
        if (_settings is null)
        {
            return;
        }

        var disabledIds = Stages.Where(stage => stage.IsDisabled).Select(stage => stage.Id).ToArray();
        _settings.SetValue(SettingsKeys.UpdatesDisabledStages, JsonSerializer.Serialize(disabledIds));
    }

    private void RebuildDisplayGroups()
    {
        if (_cardsByStage.Count == 0)
        {
            return;
        }

        var visibleCards = Stages
            .Where(stage => !HideUninstalled || stage.IsDisabled || stage.PackageStatus != PackageStatus.NotInstalled)
            .Select(stage => _cardsByStage[stage])
            .ToArray();

        var nextGroups = GroupMode switch
        {
            UpdateGroupMode.ByState => BuildStateGroups(visibleCards),
            UpdateGroupMode.Ungrouped => visibleCards.Length == 0 ? [] : [new StageCardGroup("", false, OrderCards(visibleCards))],
            _ => BuildVendorGroups(visibleCards),
        };

        if (AreGroupsEqual(DisplayGroups, nextGroups))
        {
            return;
        }

        DisplayGroups.Clear();
        foreach (var group in nextGroups)
        {
            DisplayGroups.Add(group);
        }

        OnPropertyChanged(nameof(HasVisibleCards));
        OnPropertyChanged(nameof(ShowEmptyFilterHint));
    }

    private List<StageCardGroup> BuildVendorGroups(IReadOnlyList<StageCardViewModel> visibleCards)
    {
        var vendorOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < Vendors.Count; index++)
        {
            vendorOrder[Vendors[index].Name] = index;
        }

        var groups = Vendors
            .Select(vendor =>
            {
                var cards = OrderCards(visibleCards.Where(card => card.Stage.Vendor == vendor.Name));
                return new StageCardGroup(vendor.Name, true, cards);
            })
            .Where(group => group.Cards.Count > 0)
            .ToList();

        if (SortMode == UpdateSortMode.UpdatesFirst)
        {
            groups = groups
                .OrderBy(group => group.Cards.Min(card => GetSortPriority(card.Stage)))
                .ThenBy(group => vendorOrder.GetValueOrDefault(group.Name, int.MaxValue))
                .ToList();
        }

        return groups;
    }

    private List<StageCardGroup> BuildStateGroups(IReadOnlyList<StageCardViewModel> visibleCards) =>
        visibleCards
            .GroupBy(card => GetStateBucket(card.Stage))
            .OrderBy(bucket => bucket.Key.Order)
            .Select(bucket => new StageCardGroup(bucket.Key.Title, true, OrderCards(bucket)))
            .ToList();

    private IReadOnlyList<StageCardViewModel> OrderCards(IEnumerable<StageCardViewModel> cards) =>
        SortMode switch
        {
            UpdateSortMode.UpdatesFirst => cards
                .OrderBy(card => GetSortPriority(card.Stage))
                .ThenBy(card => _catalogIndex[card.Stage])
                .ToArray(),
            UpdateSortMode.ByName => cards
                .OrderBy(card => card.Stage.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(card => _catalogIndex[card.Stage])
                .ToArray(),
            _ => cards
                .OrderBy(card => _catalogIndex[card.Stage])
                .ToArray(),
        };

    private static int GetSortPriority(UpdateStage stage)
    {
        if (stage.IsDisabled) return 6;
        if (stage.HasUpdate) return 0;
        if (stage.OperationState == OperationState.Failed || stage.PackageStatus == PackageStatus.CheckFailed) return 1;
        if (stage.PackageStatus == PackageStatus.DirectUpdate) return 2;
        if (stage.PackageStatus == PackageStatus.UpToDate) return 3;
        if (stage.PackageStatus is PackageStatus.None or PackageStatus.Unknown) return 4;
        return 5;
    }

    private static (int Order, string Title) GetStateBucket(UpdateStage stage)
    {
        if (stage.IsDisabled) return (5, "Отключены");
        if (stage.HasUpdate || stage.PackageStatus == PackageStatus.DirectUpdate) return (0, "Доступны обновления");
        if (stage.OperationState == OperationState.Failed || stage.PackageStatus == PackageStatus.CheckFailed) return (1, "Требуют внимания");
        if (stage.PackageStatus == PackageStatus.UpToDate) return (2, "Актуальны");
        if (stage.PackageStatus == PackageStatus.NotInstalled) return (4, "Не установлены");
        return (3, "Не проверены");
    }

    private static bool AreGroupsEqual(IReadOnlyList<StageCardGroup> current, IReadOnlyList<StageCardGroup> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            var left = current[index];
            var right = next[index];
            if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || left.ShowHeader != right.ShowHeader
                || left.Cards.Count != right.Cards.Count)
            {
                return false;
            }

            for (var cardIndex = 0; cardIndex < left.Cards.Count; cardIndex++)
            {
                if (!ReferenceEquals(left.Cards[cardIndex], right.Cards[cardIndex]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void StartChangelogPrefetch(UpdateStage[] candidateStages)
    {
        if (_changelogService is null)
        {
            return;
        }

        var installedActive = candidateStages.Where(stage => stage.IsInstalled && !stage.IsDisabled).ToArray();
        if (installedActive.Length == 0)
        {
            return;
        }

        CancelPrefetch();
        var cts = _prefetchCancellation = new CancellationTokenSource();
        _changelogPrefetchTask = RunPrefetchCoreAsync(installedActive, cts);
    }

    private async Task RunPrefetchCoreAsync(UpdateStage[] stages, CancellationTokenSource cts)
    {
        try
        {
            await _changelogService!.PrefetchInstalledAsync(stages, cts.Token);
        }
        finally
        {
            if (ReferenceEquals(_prefetchCancellation, cts))
            {
                _prefetchCancellation = null;
            }

            cts.Dispose();
        }
    }

    private void CancelPrefetch()
    {
        var previous = Interlocked.Exchange(ref _prefetchCancellation, null);
        if (previous is null)
        {
            return;
        }

        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

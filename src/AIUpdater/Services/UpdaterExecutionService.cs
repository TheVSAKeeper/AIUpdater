using AIUpdater.Models;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace AIUpdater.Services;

public sealed class UpdaterExecutionService : IUpdaterExecutionService
{
    private readonly IStageResultStore _resultStore;
    private readonly IUpdaterProcessRunner _processRunner;
    private readonly UpdaterOutputParser _outputParser;

    public UpdaterExecutionService(
        IStageResultStore resultStore,
        IUpdaterProcessRunner processRunner,
        UpdaterOutputParser outputParser,
        AIUpdater.Bootstrap.AppPaths? paths = null)
    {
        _resultStore = resultStore;
        _processRunner = processRunner;
        _outputParser = outputParser;

        paths ??= AIUpdater.Bootstrap.AppPaths.Default;
        LogDirectory = paths.LogsDirectory;
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        ScriptPath = Path.Combine(paths.ScriptDirectory, "Update-Codex.ps1");
        LogPath = Path.Combine(LogDirectory, "Update-Codex-last.log");
        _resultStore.ResultsPath = Path.Combine(LogDirectory, "LastResults.json");
    }

    public string ScriptPath { get; set; }

    public string LogPath { get; set; }

    public string ResultsPath
    {
        get => _resultStore.ResultsPath;
        set => _resultStore.ResultsPath = value;
    }

    public string LogDirectory { get; }

    private static Brush AccentTextBrush =>
        Application.Current?.TryFindResource("Accent.Text") as Brush
        ?? new SolidColorBrush(Color.FromRgb(110, 231, 183));

    public static OperationState GetOperationState(string action, bool checkOnly) => action switch
    {
        "Install" => OperationState.Installing,
        "Uninstall" => OperationState.Uninstalling,
        _ => checkOnly ? OperationState.Checking : OperationState.Updating,
    };

    public static string GetOperationName(OperationState state) => state switch
    {
        OperationState.Installing => "Установка",
        OperationState.Uninstalling => "Удаление",
        OperationState.Checking => "Проверка",
        _ => "Обновление",
    };

    public string GetVendorLogPath(string vendor) =>
        Path.Combine(LogDirectory, $"Update-Codex-{vendor}-last.log");

    public bool ScriptExists() =>
        File.Exists(ScriptPath);

    public bool LogFileExists(string path) =>
        File.Exists(path);

    public string? ReadPreviousLog(string path, string prefix)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return prefix + File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void ConfigurePaths(string? scriptPath = null, string? logPath = null, string? resultsPath = null)
    {
        if (scriptPath is not null)
        {
            ScriptPath = scriptPath;
        }

        if (logPath is not null)
        {
            LogPath = logPath;
        }

        if (resultsPath is not null)
        {
            _resultStore.ResultsPath = resultsPath;
        }
    }

    public bool LoadResults(IEnumerable<UpdateStage> stages, IEnumerable<VendorStatus> vendors, Action<string> onError)
    {
        var stageList = stages.ToList();
        _resultStore.Load(stageList, onError);
        foreach (var vendor in vendors)
        {
            RefreshVendorSummary(vendor);
        }

        return stageList.Any(stage => stage.CheckedAt is not null);
    }

    public void SaveResults(IEnumerable<UpdateStage> stages, Action<string> onError)
    {
        _resultStore.Save(stages, onError);
    }

    public void RefreshVendorSummary(VendorStatus vendor)
    {
        var errors = vendor.Stages.Count(stage => stage.OperationState == OperationState.Failed || stage.PackageStatus == PackageStatus.CheckFailed);
        var stopped = vendor.Stages.Count(stage => stage.OperationState == OperationState.Stopped);
        var updates = vendor.Stages.Count(stage => stage.HasUpdate);
        vendor.SetStatus(
            errors > 0 ? $"Ошибок: {errors}" : stopped > 0 ? "Остановлено" : updates > 0 ? $"Обновлений: {updates}" : "",
            UpdaterOutputParser.BuildCheckSummary(vendor.Stages),
            errors > 0 ? Brushes.LightCoral : updates > 0 ? Brushes.Gold : Brushes.LightSlateGray);
    }

    public string PrepareRun(
        IEnumerable<UpdateStage> allStages,
        UpdateStage[] runStages,
        UpdateStage? requestedStage,
        string action,
        bool checkOnly)
    {
        foreach (var stage in allStages.Where(runStages.Contains))
        {
            stage.Queue();
        }

        var operationName = GetOperationName(GetOperationState(action, checkOnly));
        return action != "Update"
            ? $"{operationName}: {requestedStage?.Name}"
            : checkOnly
                ? requestedStage is null ? "Проверка версий без установки обновлений" : $"Только проверка: {requestedStage.Name}"
                : requestedStage is null
                    ? $"Обновление выбранных компонентов ({runStages.Length})"
                    : $"Обновление: {requestedStage.Name}";
    }

    public string FinishRun(
        IReadOnlyList<UpdateStage> allStages,
        IReadOnlyList<VendorStatus> vendors,
        UpdateStage[] runStages,
        UpdateStage? requestedStage,
        string action,
        bool checkOnly,
        int exitCode,
        bool sawError,
        bool stopRequested,
        Action<string> onError)
    {
        var operationName = GetOperationName(GetOperationState(action, checkOnly));
        var success = exitCode == 0 && !sawError && !stopRequested;
        var statusText = !success
            ? stopRequested
                ? "Обновление остановлено"
                : $"Завершено с ошибками (код {exitCode})"
            : checkOnly
                ? UpdaterOutputParser.BuildCheckSummary(allStages.Where(stage => requestedStage is null || stage == requestedStage))
                : $"{operationName}: завершено" + (requestedStage is null ? "" : $" – {requestedStage.Name}");

        foreach (var stage in runStages.Where(stage => stage.IsActiveOperation))
        {
            stage.SetFailed(stopRequested);
        }

        foreach (var vendor in vendors)
        {
            RefreshVendorSummary(vendor);
        }

        SaveResults(allStages, onError);
        return statusText;
    }

    public void AppendLog(
        string line,
        string? vendorName,
        IEnumerable<VendorStatus> vendors,
        bool isRunning,
        Action<string> appendGeneralLog)
    {
        if (vendorName is not null)
        {
            var vendor = vendors.First(item => item.Name == vendorName);
            vendor.Log += line + Environment.NewLine;
            if (isRunning)
            {
                try
                {
                    File.AppendAllText(vendor.LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            line = $"[{vendorName}] {line}";
        }

        if (isRunning)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        appendGeneralLog(line + Environment.NewLine);
    }

    public void HandleOutputChunk(
        string chunk,
        bool isError,
        VendorRun run,
        OperationState operationState,
        IEnumerable<VendorStatus> vendors,
        Action<string, string?> logLine)
    {
        var vendorStatus = vendors.FirstOrDefault(item => item.Name == run.Vendor);
        _outputParser.HandleChunk(chunk, isError, run, operationState, vendorStatus, logLine);
    }

    public void HandleOutputLine(
        string line,
        bool isError,
        VendorRun run,
        OperationState operationState,
        IEnumerable<VendorStatus> vendors,
        Action<string, string?> logLine)
    {
        var vendorStatus = vendors.FirstOrDefault(item => item.Name == run.Vendor);
        _outputParser.HandleLine(line, isError, run, operationState, vendorStatus, logLine);
    }

    public async Task<(int ExitCode, bool SawError)> ExecuteRunAsync(
        IReadOnlyList<UpdateStage> allStages,
        IReadOnlyList<VendorStatus> vendors,
        UpdateStage[] runStages,
        UpdateStage? requestedStage,
        string action,
        bool checkOnly,
        bool isBulkSelection,
        Func<bool> isStopRequested,
        Action<string, string?> appendLine,
        CancellationToken cancellationToken)
    {
        foreach (var vendor in vendors.Where(item => runStages.Any(stage => stage.Vendor == item.Name)))
        {
            vendor.Log = "";
            File.WriteAllText(vendor.LogPath, $"{vendor.Name}: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);
            vendor.SetStatus("Ожидание", "Подготовка к запуску", Brushes.LightSlateGray);
        }

        File.WriteAllText(LogPath, $"AI update run: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);

        var sawError = false;
        var activeVendors = runStages.Select(stage => stage.Vendor).Distinct().ToArray();
        var results = await Task.WhenAll(activeVendors.Select(async vendor =>
        {
            if (!isBulkSelection)
            {
                var (code, vendorErr) = await RunVendorAsync(
                    allStages,
                    vendors,
                    runStages,
                    vendor,
                    requestedStage,
                    action,
                    checkOnly,
                    isStopRequested,
                    appendLine,
                    cancellationToken);
                if (vendorErr)
                {
                    sawError = true;
                }

                return code;
            }

            var result = 0;
            foreach (var stage in runStages.Where(stage => stage.Vendor == vendor))
            {
                if (isStopRequested())
                {
                    break;
                }

                var (code, vendorErr) = await RunVendorAsync(
                    allStages,
                    vendors,
                    runStages,
                    vendor,
                    stage,
                    action,
                    checkOnly,
                    isStopRequested,
                    appendLine,
                    cancellationToken);
                if (vendorErr)
                {
                    sawError = true;
                }

                if (code != 0)
                {
                    result = 1;
                }
            }

            return result;
        }));

        return (results.All(code => code == 0) ? 0 : 1, sawError);
    }

    public async Task<(int ExitCode, bool SawError)> ExecuteDotNetOperationAsync(
        IReadOnlyList<UpdateStage> allStages,
        IReadOnlyList<VendorStatus> vendors,
        DotNetOperationRequest operation,
        Func<bool> isStopRequested,
        Action<string, string?> appendLine,
        CancellationToken cancellationToken)
    {
        const string vendor = "Разработка";
        var requestedStage = allStages.FirstOrDefault(stage => stage.Id == ".NET SDK 10");
        var runStages = requestedStage is not null ? new[] { requestedStage } : Array.Empty<UpdateStage>();
        var operationState = GetOperationState(operation.ActionScriptName, false);
        var operationName = GetOperationName(operationState);
        var vendorStatus = vendors.First(item => item.Name == vendor);
        vendorStatus.Log = "";
        try
        {
            File.WriteAllText(vendorStatus.LogPath, $"{vendorStatus.Name}: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);
            File.WriteAllText(LogPath, $"AI update run: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        requestedStage?.SetRunning(operationState);
        vendorStatus.SetStatus(operationName, operation.DisplayLabel, AccentTextBrush);
        var run = new VendorRun(vendor, runStages);
        var temporaryLog = Path.Combine(Path.GetTempPath(), $"AIUpdater-{Guid.NewGuid():N}.log");
        var request = new UpdaterProcessRequest(
            ScriptPath,
            vendor,
            temporaryLog,
            operation.ActionScriptName,
            false,
            requestedStage?.Id,
            DotNetPackageEntry.GetScriptKindName(operation.Kind),
            operation.Channel,
            operation.Version,
            operation.IncludePrerelease);

        var exitCode = -1;
        try
        {
            exitCode = await _processRunner.RunAsync(
                request,
                async (chunk, isError) =>
                {
                    if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    {
                        await dispatcher.InvokeAsync(() => _outputParser.HandleChunk(chunk, isError, run, operationState, vendorStatus, appendLine));
                    }
                    else
                    {
                        _outputParser.HandleChunk(chunk, isError, run, operationState, vendorStatus, appendLine);
                    }
                },
                async isError =>
                {
                    if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    {
                        await dispatcher.InvokeAsync(() => _outputParser.FlushBuffer(isError, run, operationState, vendorStatus, appendLine));
                    }
                    else
                    {
                        _outputParser.FlushBuffer(isError, run, operationState, vendorStatus, appendLine);
                    }
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            run.SawError = true;
            appendLine(exception.Message, vendor);
        }
        finally
        {
            var stopped = isStopRequested();
            var successful = exitCode == 0 && !run.SawError && !stopped;
            if (requestedStage is { IsActiveOperation: true })
            {
                if (successful)
                {
                    requestedStage.SetCompleted();
                }
                else
                {
                    requestedStage.SetFailed(stopped);
                }
            }

            vendorStatus.SetStatus(
                stopped ? "Остановлено" : successful ? "Готово" : "Ошибка",
                stopped
                    ? "Остановка запрошена пользователем"
                    : successful
                        ? $"{operation.DisplayLabel}: завершено"
                        : "Завершено с ошибками" + (exitCode == 0 ? "" : $" (код {exitCode})"),
                stopped ? Brushes.LightSlateGray : successful ? AccentTextBrush : Brushes.LightCoral);
            appendLine(vendorStatus.Detail, vendor);
            try
            {
                File.Delete(temporaryLog);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return (exitCode == 0 && run.SawError ? 1 : exitCode, run.SawError);
    }

    private async Task<(int ExitCode, bool SawError)> RunVendorAsync(
        IReadOnlyList<UpdateStage> allStages,
        IReadOnlyList<VendorStatus> vendors,
        UpdateStage[] runStages,
        string vendor,
        UpdateStage? requestedStage,
        string action,
        bool checkOnly,
        Func<bool> isStopRequested,
        Action<string, string?> appendLine,
        CancellationToken cancellationToken)
    {
        var operationState = GetOperationState(action, checkOnly);
        var operationName = GetOperationName(operationState);
        var run = new VendorRun(vendor, runStages);
        var vendorStatus = vendors.First(item => item.Name == vendor);
        vendorStatus.SetStatus(operationName, $"{operationName}: запуск", AccentTextBrush);
        var temporaryLog = Path.Combine(Path.GetTempPath(), $"AIUpdater-{Guid.NewGuid():N}.log");
        var request = new UpdaterProcessRequest(
            ScriptPath,
            vendor,
            temporaryLog,
            action,
            checkOnly,
            requestedStage?.Id);

        var exitCode = -1;
        try
        {
            exitCode = await _processRunner.RunAsync(
                request,
                async (chunk, isError) =>
                {
                    if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    {
                        await dispatcher.InvokeAsync(() => _outputParser.HandleChunk(chunk, isError, run, operationState, vendorStatus, appendLine));
                    }
                    else
                    {
                        _outputParser.HandleChunk(chunk, isError, run, operationState, vendorStatus, appendLine);
                    }
                },
                async isError =>
                {
                    if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                    {
                        await dispatcher.InvokeAsync(() => _outputParser.FlushBuffer(isError, run, operationState, vendorStatus, appendLine));
                    }
                    else
                    {
                        _outputParser.FlushBuffer(isError, run, operationState, vendorStatus, appendLine);
                    }
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            run.SawError = true;
            appendLine(exception.Message, vendor);
        }
        finally
        {
            var stopped = isStopRequested();
            if (exitCode == 0 && !stopped)
            {
                foreach (var stage in runStages.Where(stage => stage.Vendor == vendor && (requestedStage is null || stage == requestedStage) && !run.ResultStages.Contains(stage.Id)))
                {
                    stage.SetFailed();
                    run.SawError = true;
                    appendLine($"Скрипт не передал результат: {stage.Name}", vendor);
                }
            }

            foreach (var stage in allStages.Where(stage => stage.Vendor == vendor && (requestedStage is null || stage == requestedStage) && stage.IsActiveOperation))
            {
                if (exitCode == 0 && !run.SawError && !stopped)
                {
                    stage.SetCompleted();
                }
                else
                {
                    stage.SetFailed(stopped);
                }
            }

            var successful = exitCode == 0 && !run.SawError && !stopped;
            vendorStatus.SetStatus(
                stopped ? "Остановлено" : successful ? "Готово" : "Ошибка",
                stopped
                    ? "Остановка запрошена пользователем"
                    : successful
                        ? $"{operationName}: завершено"
                        : "Завершено с ошибками" + (exitCode == 0 ? "" : $" (код {exitCode})"),
                stopped ? Brushes.LightSlateGray : successful ? AccentTextBrush : Brushes.LightCoral);

            if (checkOnly && successful)
            {
                var selected = allStages.Where(stage => stage.Vendor == vendor && (requestedStage is null || stage == requestedStage)).ToArray();
                var available = selected.Any(stage => stage.HasUpdate);
                var unknown = selected.Any(stage => stage.PackageStatus == PackageStatus.Unknown);
                var errors = selected.Any(stage => stage.PackageStatus == PackageStatus.CheckFailed);
                vendorStatus.SetStatus(
                    errors ? "Ошибка проверки" : available ? "Есть обновления" : unknown ? "Проверено частично" : "Проверено",
                    UpdaterOutputParser.BuildCheckSummary(selected),
                    errors ? Brushes.LightCoral : available ? Brushes.Gold : AccentTextBrush);
            }

            appendLine(vendorStatus.Detail, vendor);
            try
            {
                File.Delete(temporaryLog);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return (exitCode == 0 && run.SawError ? 1 : exitCode, run.SawError);
    }
}

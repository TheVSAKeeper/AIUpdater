using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IUpdaterExecutionService
{
    string ScriptPath { get; set; }

    string LogPath { get; set; }

    string ResultsPath { get; set; }

    string LogDirectory { get; }

    string GetVendorLogPath(string vendor);

    bool ScriptExists();

    bool LogFileExists(string path);

    string? ReadPreviousLog(string path, string prefix);

    void ConfigurePaths(string? scriptPath = null, string? logPath = null, string? resultsPath = null);

    bool LoadResults(IEnumerable<UpdateStage> stages, IEnumerable<VendorStatus> vendors, Action<string> onError);

    void SaveResults(IEnumerable<UpdateStage> stages, Action<string> onError);

    void RefreshVendorSummary(VendorStatus vendor);

    string PrepareRun(
        IEnumerable<UpdateStage> allStages,
        UpdateStage[] runStages,
        UpdateStage? requestedStage,
        string action,
        bool checkOnly);

    string FinishRun(
        IReadOnlyList<UpdateStage> allStages,
        IReadOnlyList<VendorStatus> vendors,
        UpdateStage[] runStages,
        UpdateStage? requestedStage,
        string action,
        bool checkOnly,
        int exitCode,
        bool sawError,
        bool stopRequested,
        Action<string> onError);

    void AppendLog(string line, string? vendorName, IEnumerable<VendorStatus> vendors, bool isRunning, Action<string> appendGeneralLog);

    void HandleOutputChunk(
        string chunk,
        bool isError,
        VendorRun run,
        OperationState operationState,
        IEnumerable<VendorStatus> vendors,
        Action<string, string?> logLine);

    void HandleOutputLine(
        string line,
        bool isError,
        VendorRun run,
        OperationState operationState,
        IEnumerable<VendorStatus> vendors,
        Action<string, string?> logLine);

    Task<(int ExitCode, bool SawError)> ExecuteRunAsync(
        IReadOnlyList<UpdateStage> allStages,
        IReadOnlyList<VendorStatus> vendors,
        UpdateStage[] runStages,
        UpdateStage? requestedStage,
        string action,
        bool checkOnly,
        bool isBulkSelection,
        Func<bool> isStopRequested,
        Action<string, string?> appendLine,
        CancellationToken cancellationToken);
}

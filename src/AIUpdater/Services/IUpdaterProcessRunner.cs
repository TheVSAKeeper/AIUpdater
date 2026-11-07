namespace AIUpdater.Services;

public sealed record UpdaterProcessRequest(
    string ScriptPath,
    string Vendor,
    string LogPath,
    string Action,
    bool CheckOnly,
    string? OnlyStageId);

public interface IUpdaterProcessRunner
{
    Task<int> RunAsync(
        UpdaterProcessRequest request,
        Func<string, bool, Task> onOutputChunk,
        Func<bool, Task> onStreamCompleted,
        CancellationToken cancellationToken);
}

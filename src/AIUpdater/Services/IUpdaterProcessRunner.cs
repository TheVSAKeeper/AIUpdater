namespace AIUpdater.Services;

public sealed record UpdaterProcessRequest(
    string ScriptPath,
    string Vendor,
    string LogPath,
    string Action,
    bool CheckOnly,
    string? OnlyStageId,
    string? DotNetPackageKind = null,
    string? DotNetChannel = null,
    string? DotNetVersion = null,
    bool IncludePrerelease = false);

public interface IUpdaterProcessRunner
{
    Task<int> RunAsync(
        UpdaterProcessRequest request,
        Func<string, bool, Task> onOutputChunk,
        Func<bool, Task> onStreamCompleted,
        CancellationToken cancellationToken);
}

using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IChangelogContentFetcher
{
    Task<string> FetchAsync(ChangelogSource source, CancellationToken cancellationToken);

    Task<(string Content, string? ETag, bool NotModified)> FetchWithETagAsync(
        ChangelogSource source,
        string? cachedETag,
        CancellationToken cancellationToken);

    Task<string> DownloadUrlAsync(string url, CancellationToken cancellationToken);

    Task<string> RunCommandAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    string? FindAgyExecutable();
}

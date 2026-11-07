using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IChangelogService
{
    IChangelogRepository Repository { get; }

    IChangelogTranslator Translator { get; }

    ChangelogDocument? TryLoadCachedDocument(string stageId);

    void SaveCachedDocument(ChangelogDocument document);

    List<ChangelogRelease> ParseReleases(string content, string format);

    List<ChangelogRelease> MergeReleases(IReadOnlyList<ChangelogRelease> fresh, IReadOnlyList<ChangelogRelease> cached);

    Task<(ChangelogDocument Document, bool FromCacheOnly)> LoadDocumentAsync(
        UpdateStage stage,
        bool forceRefresh,
        CancellationToken cancellationToken);

    Task<bool> TranslateReleaseAsync(
        ChangelogDocument document,
        ChangelogRelease release,
        CancellationToken cancellationToken,
        bool forceRetranslate = false);
}

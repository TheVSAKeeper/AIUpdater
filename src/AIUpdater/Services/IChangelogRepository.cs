using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IChangelogRepository
{
    string CacheDirectory { get; set; }

    ChangelogDocument? TryLoad(string stageId);

    void Save(ChangelogDocument document);

    List<ChangelogRelease> MergeReleases(IReadOnlyList<ChangelogRelease> fresh, IReadOnlyList<ChangelogRelease> cached);
}

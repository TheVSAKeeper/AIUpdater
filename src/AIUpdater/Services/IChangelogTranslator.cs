using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IChangelogTranslator
{
    TranslationAgentSettings Settings { get; }

    string DisplayName { get; }

    Task<bool> TranslateReleaseAsync(
        ChangelogRelease release,
        CancellationToken cancellationToken,
        bool forceRetranslate = false);
}

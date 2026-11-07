using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IChangelogTranslator
{
    Func<string, CancellationToken, Task<string?>>? PromptRunner { get; set; }

    Task<bool> TranslateReleaseAsync(
        ChangelogRelease release,
        CancellationToken cancellationToken,
        bool forceRetranslate = false);
}

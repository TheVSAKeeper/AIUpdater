using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IChangelogFormatParser
{
    bool CanParse(string format);

    IReadOnlyList<ChangelogRelease> Parse(string raw);
}

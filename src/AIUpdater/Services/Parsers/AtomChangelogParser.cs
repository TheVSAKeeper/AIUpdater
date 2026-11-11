using AIUpdater.Models;
using System.Xml.Linq;

namespace AIUpdater.Services.Parsers;

public sealed class AtomChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "atom", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw) =>
        ParseAtom(raw);

    public static List<ChangelogRelease> ParseAtom(string xml)
    {
        var releases = new List<ChangelogRelease>();
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.w3.org/2005/Atom";
        var entries = doc.Descendants(ns + "entry").Take(40).ToList();
        var hasRustPrefix = entries.Any(entry =>
        {
            var id = entry.Element(ns + "id")?.Value ?? "";
            var tag = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : "";
            return tag.StartsWith("rust-v", StringComparison.OrdinalIgnoreCase);
        });

        var stableCount = 0;
        var prereleaseCount = 0;
        foreach (var entry in entries)
        {
            var title = entry.Element(ns + "title")?.Value?.Trim() ?? "";
            var updated = ChangelogParseHelper.FormatIsoDate(entry.Element(ns + "updated")?.Value);
            var id = entry.Element(ns + "id")?.Value ?? "";
            var tagFromId = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : title;
            if (hasRustPrefix && !tagFromId.StartsWith("rust-v", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var version = ChangelogFormatter.NormalizeVersion(tagFromId) ?? ChangelogFormatter.NormalizeVersion(title) ?? title;
            var isPrerelease = ChangelogFormatter.IsPrereleaseVersion(tagFromId) || ChangelogFormatter.IsPrereleaseVersion(version);
            if (isPrerelease)
            {
                if (prereleaseCount >= 12)
                {
                    continue;
                }

                prereleaseCount++;
            }
            else
            {
                if (stableCount >= 12)
                {
                    continue;
                }

                stableCount++;
            }

            var htmlBody = entry.Element(ns + "content")?.Value ?? "";
            var plainBody = ChangelogParseHelper.HtmlToMarkdownLines(htmlBody);
            var groups = ChangelogParseHelper.ParseBodyIntoGroups(plainBody, title);

            releases.Add(new()
            {
                Version = version,
                Title = string.IsNullOrWhiteSpace(title) ? $"v{version}" : title,
                PublishedAt = updated,
                IsPrerelease = isPrerelease,
                Groups = groups,
            });
        }

        return releases;
    }
}

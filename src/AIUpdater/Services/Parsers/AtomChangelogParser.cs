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
        foreach (var entry in doc.Descendants(ns + "entry").Take(12))
        {
            var title = entry.Element(ns + "title")?.Value?.Trim() ?? "";
            var updated = ChangelogParseHelper.FormatIsoDate(entry.Element(ns + "updated")?.Value);
            var id = entry.Element(ns + "id")?.Value ?? "";
            var tagFromId = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : title;
            var htmlBody = entry.Element(ns + "content")?.Value ?? "";
            var plainBody = ChangelogParseHelper.HtmlToMarkdownLines(htmlBody);
            var version = ChangelogFormatter.NormalizeVersion(tagFromId) ?? ChangelogFormatter.NormalizeVersion(title) ?? title;
            var groups = ChangelogParseHelper.ParseBodyIntoGroups(plainBody, title);

            releases.Add(new()
            {
                Version = version,
                Title = string.IsNullOrWhiteSpace(title) ? $"v{version}" : title,
                PublishedAt = updated,
                Groups = groups,
            });
        }

        return releases;
    }
}

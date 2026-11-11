using AIUpdater.Models;
using System.Text.RegularExpressions;

namespace AIUpdater.Services.Parsers;

public sealed class MarkdownChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw) =>
        ParseMarkdown(raw);

    public static List<ChangelogRelease> ParseMarkdown(string content)
    {
        var cleaned = Regex.Replace(content, @"\A---\r?\n[\s\S]*?\r?\n---\r?\n", "");
        cleaned = Regex.Replace(cleaned, @"\{\{[<%][\s\S]*?[>%]\}\}", "");
        var headingPattern = new Regex(
            @"(?m)^#{1,3}\s+(?<line>(?:(?<date1>\d{4}-\d{2}-\d{2})[,\s–-]*(?:Version\s+)?)?\[?v?(?<ver>\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z._-]+)?)\]?(?<rest>[^\r\n]*))$",
            RegexOptions.IgnoreCase);

        var matches = headingPattern.Matches(cleaned);
        if (matches.Count == 0)
        {
            var fallbackGroups = ChangelogParseHelper.ParseBodyIntoGroups(cleaned);
            return fallbackGroups.Count == 0
                ? []
                :
                [
                    new()
                    {
                        Version = "latest",
                        Title = "Последние изменения",
                        Groups = fallbackGroups,
                    },
                ];
        }

        var releases = new List<ChangelogRelease>();
        for (var index = 0; index < matches.Count && releases.Count < 12; index++)
        {
            var match = matches[index];
            var version = match.Groups["ver"].Value.Trim();
            var rest = match.Groups["rest"].Value.Trim();
            var dateMatch = Regex.Match(match.Groups["line"].Value, @"\b(\d{4}-\d{2}-\d{2})\b");
            var publishedAt = dateMatch.Success ? dateMatch.Groups[1].Value : null;
            var start = match.Index + match.Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : Math.Min(cleaned.Length, start + 18000);
            var section = cleaned[start..end].Trim();
            var groups = ChangelogParseHelper.ParseBodyIntoGroups(section);
            if (groups.Count == 0)
            {
                continue;
            }

            var cleanRest = Regex.Replace(rest, @"^[\s()\[\]–\u2014,-]+|\b\d{4}-\d{2}-\d{2}\b|\bLatest\b", "", RegexOptions.IgnoreCase).Trim(' ', '-', '–', ',', '(', ')');
            var title = string.IsNullOrWhiteSpace(cleanRest) ? $"v{version}" : $"v{version} – {cleanRest}";
            releases.Add(new()
            {
                Version = version,
                Title = title,
                PublishedAt = publishedAt,
                IsPrerelease = ChangelogFormatter.IsPrereleaseVersion(version),
                Groups = groups,
            });
        }

        return releases;
    }
}

using AIUpdater.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace AIUpdater.Services.Parsers;

public sealed class HtmlChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "html", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "vscode", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "claude-desktop", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw) =>
        ParseHtml(raw);

    public static List<ChangelogRelease> ParseHtml(string content)
    {
        var main = Regex.Match(content, @"<main\b[^>]*>([\s\S]*?)</main>", RegexOptions.IgnoreCase);
        var articles = Regex.Matches(content, @"<article\b[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase);
        string bodyHtml;
        if (main.Success)
        {
            bodyHtml = main.Groups[1].Value;
        }
        else if (articles.Count > 0)
        {
            bodyHtml = string.Join("\n\n", articles.Select(a => a.Groups[1].Value));
        }
        else
        {
            var bodyMatch = Regex.Match(content, @"<body\b[^>]*>([\s\S]*?)</body>", RegexOptions.IgnoreCase);
            if (!bodyMatch.Success)
            {
                throw new InvalidDataException("Страница требует просмотра в браузере. Откройте официальный источник.");
            }

            bodyHtml = bodyMatch.Groups[1].Value;
        }

        var markdownLike = ChangelogParseHelper.HtmlToMarkdownLines(bodyHtml);
        if (markdownLike.Length < 80)
        {
            throw new InvalidDataException("На странице нет доступного текста изменений. Откройте официальный источник.");
        }

        var parsed = MarkdownChangelogParser.ParseMarkdown(markdownLike);
        if (parsed.Count > 0 && !(parsed.Count == 1 && parsed[0].Version == "latest"))
        {
            return parsed;
        }

        var sectionMatches = Regex.Matches(markdownLike, @"(?m)^##\s+(?<head>[^\r\n]+)$");
        if (sectionMatches.Count > 0)
        {
            var releases = new List<ChangelogRelease>();
            for (var index = 0; index < sectionMatches.Count && releases.Count < 12; index++)
            {
                var head = Regex.Replace(sectionMatches[index].Groups["head"].Value, @"\bLatest\b", "", RegexOptions.IgnoreCase).Trim(' ', '-', '–', ':');
                if (string.IsNullOrWhiteSpace(head)
                    || ChangelogParseHelper.IsCategoryHeading(head)
                    || Regex.IsMatch(head, @"^(?:ChatGPT\s*&\s*Codex\s*changelog|Codex\s*changelog|Release\s*notes|Changelog|What's\s*new)$", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                var start = sectionMatches[index].Index + sectionMatches[index].Length;
                var end = index + 1 < sectionMatches.Count ? sectionMatches[index + 1].Index : markdownLike.Length;
                var block = markdownLike[start..end].Trim();
                var groups = ChangelogParseHelper.ParseBodyIntoGroups(block);
                if (groups.Count == 0)
                {
                    continue;
                }

                if (Regex.IsMatch(head, @"^(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}$", RegexOptions.IgnoreCase)
                    && groups.Sum(g => g.Items.Count) <= 1)
                {
                    continue;
                }

                var versionMatch = Regex.Match(head, @"v?(\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z.-]+)?)");
                var dateMatch = Regex.Match(head + " " + block, @"\b(\d{4}-\d{2}-\d{2}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},\s+\d{4})\b", RegexOptions.IgnoreCase);
                releases.Add(new()
                {
                    Version = versionMatch.Success ? versionMatch.Groups[1].Value : head,
                    Title = head,
                    PublishedAt = dateMatch.Success ? dateMatch.Groups[1].Value : null,
                    Groups = groups,
                });
            }

            if (releases.Count > 0)
            {
                return releases;
            }
        }

        return parsed;
    }
}

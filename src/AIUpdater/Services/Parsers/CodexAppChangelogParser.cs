using AIUpdater.Models;
using System.Text.RegularExpressions;

namespace AIUpdater.Services.Parsers;

public sealed class CodexAppChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "codex-app", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw)
    {
        var itemMatches = Regex.Matches(
            raw,
            @"<li\b[^>]*data-codex-topics=""(?<topics>[^""]*)""[^>]*>(?<body>[\s\S]*?</article>)\s*</li>",
            RegexOptions.IgnoreCase);

        if (itemMatches.Count == 0)
        {
            return HtmlChangelogParser.ParseHtml(raw);
        }

        var releases = new List<ChangelogRelease>();
        foreach (Match match in itemMatches)
        {
            var topics = match.Groups["topics"].Value;
            if (!Regex.IsMatch(topics, @"\b(?:codex-app|general)\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var bodyHtml = match.Groups["body"].Value;
            var date = Regex.Match(bodyHtml, @"<time\b[^>]*>([^<]+)</time>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var rawTitle = Regex.Match(bodyHtml, @"<h3\b[^>]*>([\s\S]*?)</h3>", RegexOptions.IgnoreCase).Groups[1].Value;
            var title = ChangelogParseHelper.StripTags(Regex.Replace(rawTitle, @"<button\b[^>]*>[\s\S]*?</button>", "", RegexOptions.IgnoreCase));
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var articleMatch = Regex.Match(bodyHtml, @"<article\b[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase);
            var articleHtml = articleMatch.Success ? articleMatch.Groups[1].Value : bodyHtml;
            var articleLines = ChangelogParseHelper.HtmlToMarkdownLines(articleHtml);
            var groups = ChangelogParseHelper.ParseBodyIntoGroups(articleLines);
            if (groups.Count == 0)
            {
                continue;
            }

            var versionMatch = Regex.Match(
                title,
                @"(?:Codex\s+app|ChatGPT(?:\s+app)?)\s+v?(?<ver>\d{2}\.\d+(?:\.\d+)*|\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\b",
                RegexOptions.IgnoreCase);
            var versionKey = versionMatch.Success
                ? versionMatch.Groups["ver"].Value
                : !string.IsNullOrWhiteSpace(date) ? $"{date}-{title}" : title;

            var displayTitle = title.StartsWith("Codex app", StringComparison.OrdinalIgnoreCase)
                               || title.Contains("Codex", StringComparison.OrdinalIgnoreCase)
                               || title.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)
                ? title
                : $"ChatGPT / Codex App – {title}";

            releases.Add(new()
            {
                Version = versionKey,
                Title = displayTitle,
                PublishedAt = string.IsNullOrWhiteSpace(date) ? null : date,
                Groups = groups,
            });

            if (releases.Count >= 15)
            {
                break;
            }
        }

        return releases.Count > 0 ? releases : HtmlChangelogParser.ParseHtml(raw);
    }
}

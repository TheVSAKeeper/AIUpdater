using AIUpdater.Models;
using System.Text.RegularExpressions;

namespace AIUpdater.Services.Parsers;

public sealed class AntigravityDesktopChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "antigravity-desktop", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw)
    {
        var articleMatches = Regex.Matches(
            raw,
            @"<article\b[^>]*\bid=""rel-hub-(?<ver>\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z.-]+)?)""[^>]*>(?<body>[\s\S]*?)</article>",
            RegexOptions.IgnoreCase);

        if (articleMatches.Count == 0)
        {
            return HtmlChangelogParser.ParseHtml(raw);
        }

        var releases = new List<ChangelogRelease>();
        foreach (Match match in articleMatches)
        {
            var version = match.Groups["ver"].Value.Trim();
            var body = match.Groups["body"].Value;
            var rawDate = Regex.Match(body, @"<time\b[^>]*class=""[^""]*rn-date-text[^""]*""[^>]*>([^<]+)</time>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var publishedAt = ChangelogParseHelper.FormatIsoDate(rawDate);
            var headlineHtml = Regex.Match(body, @"<h3\b[^>]*class=""[^""]*rn-headline[^""]*""[^>]*>([\s\S]*?)</h3>", RegexOptions.IgnoreCase).Groups[1].Value;
            var headline = ChangelogParseHelper.StripTags(headlineHtml).Replace('\u2014', '–');
            var summaryHtml = Regex.Match(body, @"<div\b[^>]*class=""[^""]*rn-summary[^""]*""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase).Groups[1].Value;
            var summary = ChangelogParseHelper.StripTags(summaryHtml).Replace('\u2014', '–');

            var detailMatches = Regex.Matches(body, @"<details\b[^>]*>[\s\S]*?</details>", RegexOptions.IgnoreCase);
            var detailsHtml = detailMatches.Count > 0
                ? string.Join("\n", detailMatches.Select(m => m.Value))
                : summaryHtml;

            var markdownLines = ChangelogParseHelper.HtmlToMarkdownLines(detailsHtml);
            var displayTitle = string.IsNullOrWhiteSpace(headline)
                ? $"Antigravity Desktop v{version}"
                : $"v{version} – {headline}";

            var groups = ChangelogParseHelper.ParseBodyIntoGroups(markdownLines, displayTitle);
            releases.Add(new()
            {
                Version = version,
                Title = displayTitle,
                PublishedAt = publishedAt,
                IsPrerelease = ChangelogFormatter.IsPrereleaseVersion(version),
                Summary = detailMatches.Count > 0 && !string.IsNullOrWhiteSpace(summary) ? summary : null,
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

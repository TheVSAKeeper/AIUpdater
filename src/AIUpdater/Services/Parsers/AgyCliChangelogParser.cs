using AIUpdater.Models;
using System.Text.RegularExpressions;

namespace AIUpdater.Services.Parsers;

public sealed class AgyCliChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "agy", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw) =>
        ParseAgy(raw);

    public static List<ChangelogRelease> ParseAgy(string content)
    {
        var releases = new List<ChangelogRelease>();
        var matches = Regex.Matches(content, @"(?m)^(?<ver>\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z.-]+)?)\s*:\s*$");
        if (matches.Count == 0)
        {
            return MarkdownChangelogParser.ParseMarkdown(content);
        }

        for (var index = 0; index < matches.Count && releases.Count < 15; index++)
        {
            var version = matches[index].Groups["ver"].Value.Trim();
            var start = matches[index].Index + matches[index].Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : content.Length;
            var body = content[start..end].Trim();
            var groups = ChangelogParseHelper.ParseBodyIntoGroups(body);
            if (groups.Count > 0)
            {
                releases.Add(new()
                {
                    Version = version,
                    Title = $"Antigravity CLI v{version}",
                    Groups = groups,
                });
            }
        }

        return releases;
    }
}

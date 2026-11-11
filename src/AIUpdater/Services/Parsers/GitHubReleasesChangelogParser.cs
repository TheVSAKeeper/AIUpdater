using AIUpdater.Models;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services.Parsers;

public sealed class GitHubReleasesChangelogParser : IChangelogFormatParser
{
    private const int MaxPerChannel = 12;

    public bool CanParse(string format) =>
        string.Equals(format, "github-releases", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "github", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw)
    {
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            return AtomChangelogParser.ParseAtom(raw);
        }

        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var elements = new List<JsonElement>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray().Take(80))
            {
                elements.Add(item);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            elements.Add(root);
        }

        var hasRustPrefix = elements.Any(element =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("tag_name", out var tagProp)
            && (tagProp.GetString()?.StartsWith("rust-v", StringComparison.OrdinalIgnoreCase) ?? false));

        var entries = new List<ParsedEntry>();
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (element.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var tag = element.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            if (hasRustPrefix && (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("rust-v", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var htmlUrl = element.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
            var date = element.TryGetProperty("published_at", out var dateProp) ? ChangelogParseHelper.FormatIsoDate(dateProp.GetString()) : null;
            var body = element.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            var jsonPrerelease = element.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;
            var version = ChangelogFormatter.NormalizeVersion(tag) ?? ChangelogFormatter.NormalizeVersion(name) ?? tag ?? name ?? "Release";
            var isPrerelease = jsonPrerelease || ChangelogFormatter.IsPrereleaseVersion(tag) || ChangelogFormatter.IsPrereleaseVersion(version);
            var rawTitle = !string.IsNullOrWhiteSpace(name) ? name! : !string.IsNullOrWhiteSpace(tag) ? tag! : version;
            var title = CleanReleaseTitle(rawTitle, version);

            entries.Add(new ParsedEntry(entries.Count, tag, htmlUrl, version, title, date, body, isPrerelease));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("Источник не вернул описание выпуска.");
        }

        var stableEntries = SortPrereleaseSeriesRuns(entries.Where(entry => !entry.IsPrerelease).ToList());
        var prereleaseEntries = SortPrereleaseSeriesRuns(entries.Where(entry => entry.IsPrerelease).ToList());

        var stableReleases = BuildChannelReleases(stableEntries, entries).Take(MaxPerChannel);
        var prereleaseReleases = BuildChannelReleases(prereleaseEntries, entries).Take(MaxPerChannel);

        return stableReleases
            .Concat(prereleaseReleases)
            .OrderBy(pair => pair.OrderKey)
            .Select(pair => pair.Release)
            .ToList();
    }

    private static List<(int OrderKey, ChangelogRelease Release)> BuildChannelReleases(
        IReadOnlyList<ParsedEntry> channelEntries,
        IReadOnlyList<ParsedEntry> allEntries)
    {
        var result = new List<(int OrderKey, ChangelogRelease Release)>(channelEntries.Count);
        for (var index = 0; index < channelEntries.Count; index++)
        {
            var current = channelEntries[index];
            var groups = ChangelogParseHelper.ParseBodyIntoGroups(current.Body, current.Title);
            var compareUrl = ExtractCompareApiUrl(current.Body);
            if (compareUrl is null && ChangelogParseHelper.IsPlaceholderOnlyGroups(groups))
            {
                var previousTag = FindPreviousTagForEntry(current, index, channelEntries, allEntries);
                compareUrl = BuildCompareApiUrlFromReleaseUrl(current.HtmlUrl, previousTag, current.Tag);
            }

            result.Add((
                current.FeedOrder,
                new ChangelogRelease
                {
                    Version = current.Version,
                    Title = current.Title,
                    PublishedAt = current.Date,
                    IsPrerelease = current.IsPrerelease,
                    CompareUrl = compareUrl,
                    Groups = groups,
                }));
        }

        return result;
    }

    private static string? FindPreviousTagForEntry(
        ParsedEntry current,
        int channelIndex,
        IReadOnlyList<ParsedEntry> channelEntries,
        IReadOnlyList<ParsedEntry> allEntries)
    {
        var currentMinor = ExtractMajorMinorPrefix(current.Version);
        if (currentMinor is not null)
        {
            for (var i = channelIndex + 1; i < channelEntries.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(channelEntries[i].Tag)
                    && string.Equals(ExtractMajorMinorPrefix(channelEntries[i].Version), currentMinor, StringComparison.OrdinalIgnoreCase))
                {
                    return channelEntries[i].Tag;
                }
            }

            for (var i = current.FeedOrder + 1; i < allEntries.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(allEntries[i].Tag)
                    && !string.Equals(allEntries[i].Tag, current.Tag, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ExtractMajorMinorPrefix(allEntries[i].Version), currentMinor, StringComparison.OrdinalIgnoreCase))
                {
                    return allEntries[i].Tag;
                }
            }
        }

        if (channelIndex + 1 < channelEntries.Count && !string.IsNullOrWhiteSpace(channelEntries[channelIndex + 1].Tag))
        {
            return channelEntries[channelIndex + 1].Tag;
        }

        for (var index = current.FeedOrder + 1; index < allEntries.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(allEntries[index].Tag)
                && !string.Equals(allEntries[index].Tag, current.Tag, StringComparison.OrdinalIgnoreCase))
            {
                return allEntries[index].Tag;
            }
        }

        return null;
    }

    private static string? ExtractMajorMinorPrefix(string version)
    {
        var match = Regex.Match(version, @"^(?<mm>\d+\.\d+)\b");
        return match.Success ? match.Groups["mm"].Value : null;
    }

    private static List<ParsedEntry> SortPrereleaseSeriesRuns(List<ParsedEntry> list)
    {
        var index = 0;
        while (index < list.Count)
        {
            if (!TryParsePrereleaseSeries(list[index].Version, out var series, out _))
            {
                index++;
                continue;
            }

            var end = index + 1;
            while (end < list.Count && TryParsePrereleaseSeries(list[end].Version, out var nextSeries, out _) && nextSeries == series)
            {
                end++;
            }

            if (end - index > 1)
            {
                var originalSlots = list.GetRange(index, end - index).Select(item => item.FeedOrder).OrderBy(n => n).ToList();
                var sortedSlice = list.GetRange(index, end - index)
                    .OrderByDescending(item => TryParsePrereleaseSeries(item.Version, out _, out var number) ? number : -1)
                    .Select((item, offset) => item with { FeedOrder = originalSlots[offset] })
                    .ToList();

                for (var offset = 0; offset < sortedSlice.Count; offset++)
                {
                    list[index + offset] = sortedSlice[offset];
                }
            }

            index = end;
        }

        return list;
    }

    private static bool TryParsePrereleaseSeries(string version, out string series, out int number)
    {
        var match = Regex.Match(version, @"^(?<series>\d+\.\d+(?:\.\d+)*-[A-Za-z]+)\.(?<num>\d+)$");
        if (match.Success && int.TryParse(match.Groups["num"].Value, out number))
        {
            series = match.Groups["series"].Value.ToLowerInvariant();
            return true;
        }

        series = "";
        number = -1;
        return false;
    }

    private static string CleanReleaseTitle(string rawTitle, string version)
    {
        var trimmed = rawTitle.Trim();
        if (Regex.IsMatch(trimmed, @"^Release\s+v?\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z._-]+)?$", RegexOptions.IgnoreCase))
        {
            return $"v{version}";
        }

        return trimmed;
    }

    private static string? ExtractCompareApiUrl(string body)
    {
        var match = Regex.Match(
            body,
            @"https://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/compare/(?<spec>[A-Za-z0-9_.\-~^/]+)",
            RegexOptions.IgnoreCase);

        return match.Success
            ? $"https://api.github.com/repos/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}/compare/{match.Groups["spec"].Value}"
            : null;
    }

    private static string? BuildCompareApiUrlFromReleaseUrl(string? htmlUrl, string? previousTag, string? currentTag)
    {
        if (string.IsNullOrWhiteSpace(htmlUrl) || string.IsNullOrWhiteSpace(previousTag) || string.IsNullOrWhiteSpace(currentTag))
        {
            return null;
        }

        var match = Regex.Match(
            htmlUrl,
            @"^https://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/releases/",
            RegexOptions.IgnoreCase);

        return match.Success
            ? $"https://api.github.com/repos/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}/compare/{Uri.EscapeDataString(previousTag)}...{Uri.EscapeDataString(currentTag)}"
            : null;
    }

    private sealed record ParsedEntry(
        int FeedOrder,
        string? Tag,
        string? HtmlUrl,
        string Version,
        string Title,
        string? Date,
        string Body,
        bool IsPrerelease);
}

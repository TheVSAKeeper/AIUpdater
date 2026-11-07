using AIUpdater.Models;
using System.IO;
using System.Text.Json;

namespace AIUpdater.Services.Parsers;

public sealed class GitHubReleasesChangelogParser : IChangelogFormatParser
{
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
            foreach (var item in root.EnumerateArray().Take(12))
            {
                elements.Add(item);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            elements.Add(root);
        }

        var releases = new List<ChangelogRelease>();
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tag = element.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var date = element.TryGetProperty("published_at", out var dateProp) ? ChangelogParseHelper.FormatIsoDate(dateProp.GetString()) : null;
            var body = element.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            var version = ChangelogFormatter.NormalizeVersion(tag) ?? ChangelogFormatter.NormalizeVersion(name) ?? tag ?? name ?? "Release";
            var title = !string.IsNullOrWhiteSpace(name) ? name! : !string.IsNullOrWhiteSpace(tag) ? tag! : version;
            var groups = ChangelogParseHelper.ParseBodyIntoGroups(body, title);

            releases.Add(new()
            {
                Version = version,
                Title = title,
                PublishedAt = date,
                Groups = groups,
            });
        }

        if (releases.Count == 0)
        {
            throw new InvalidDataException("Источник не вернул описание выпуска.");
        }

        return releases;
    }
}

using AIUpdater.Models;
using System.IO;
using System.Text.Json;

namespace AIUpdater.Services.Parsers;

public sealed class DotNetReleasesChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "dotnet-releases", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        if (!document.RootElement.TryGetProperty("releases", out var releasesArray)
            || releasesArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Метаданные выпусков .NET не найдены.");
        }

        var releases = new List<ChangelogRelease>();
        foreach (var item in releasesArray.EnumerateArray().Take(10))
        {
            var releaseVersion = item.TryGetProperty("release-version", out var rv) ? rv.GetString() ?? "" : "";
            var releaseDate = item.TryGetProperty("release-date", out var rd) ? rd.GetString() : null;
            var isSecurity = item.TryGetProperty("security", out var sec) && sec.ValueKind == JsonValueKind.True;
            var primarySdk = item.TryGetProperty("sdk", out var sdkObj) && sdkObj.TryGetProperty("version", out var sv)
                ? sv.GetString()
                : null;

            var sdkVersions = new List<string>();
            if (item.TryGetProperty("sdks", out var sdksArr) && sdksArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var sdkEntry in sdksArr.EnumerateArray())
                {
                    if (sdkEntry.TryGetProperty("version", out var v) && v.GetString() is { Length: > 0 } ver && !sdkVersions.Contains(ver))
                    {
                        sdkVersions.Add(ver);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(primarySdk))
            {
                sdkVersions.Add(primarySdk!);
            }

            var groups = new List<ChangelogGroup>();
            var componentItems = new List<string>();
            if (sdkVersions.Count > 0)
            {
                componentItems.Add($"Выпуски .NET SDK: {string.Join(", ", sdkVersions.Select(v => $"`{v}`"))}");
            }

            if (item.TryGetProperty("runtime", out var rtObj) && rtObj.TryGetProperty("version", out var rtv) && rtv.GetString() is { } rtVer)
            {
                componentItems.Add($".NET Runtime: `{rtVer}`");
            }

            if (item.TryGetProperty("aspnetcore-runtime", out var aspObj) && aspObj.TryGetProperty("version", out var aspv) && aspv.GetString() is { } aspVer)
            {
                componentItems.Add($"ASP.NET Core Runtime: `{aspVer}`");
            }

            if (item.TryGetProperty("windowsdesktop", out var wdObj) && wdObj.TryGetProperty("version", out var wdv) && wdv.GetString() is { } wdVer)
            {
                componentItems.Add($"Windows Desktop Runtime: `{wdVer}`");
            }

            if (componentItems.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Changed,
                    Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Changed),
                    Items = componentItems,
                });
            }

            var securityItems = new List<string>();
            if (item.TryGetProperty("cve-list", out var cveArr) && cveArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var cve in cveArr.EnumerateArray())
                {
                    var id = cve.TryGetProperty("cve-id", out var cid) ? cid.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        securityItems.Add($"Исправлена уязвимость `{id}`");
                    }
                }
            }

            if (isSecurity && securityItems.Count == 0)
            {
                securityItems.Add("Выпуск содержит обновления безопасности.");
            }

            if (securityItems.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Security,
                    Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Security),
                    Items = securityItems,
                });
            }

            var versionKey = primarySdk ?? releaseVersion;
            releases.Add(new()
            {
                Version = versionKey,
                Title = sdkVersions.Count > 0
                    ? $".NET {releaseVersion} (SDK {string.Join("; ", sdkVersions)})"
                    : $".NET {releaseVersion}",
                PublishedAt = releaseDate,
                Groups = groups,
            });
        }

        return releases;
    }
}

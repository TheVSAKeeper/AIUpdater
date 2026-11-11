using AIUpdater.Models;
using AIUpdater.Services.Parsers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public sealed class FileChangelogRepository(string? cacheDirectory = null) : IChangelogRepository
{
    private static readonly object SaveGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string CacheDirectory { get; set; } = cacheDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIUpdater",
        "Changelogs");

    public ChangelogDocument? TryLoad(string stageId)
    {
        var path = GetCacheFilePath(stageId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<ChangelogDocument>(File.ReadAllText(path, Encoding.UTF8));
            if (doc is null || doc.SchemaVersion is < 7 or > ChangelogDocument.CurrentSchemaVersion || doc.Releases.Count == 0)
            {
                return null;
            }

            ChangelogFormatter.EnsureSummaries(doc.Releases);
            return doc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(ChangelogDocument document)
    {
        lock (SaveGate) SaveCore(document);
    }

    private void SaveCore(ChangelogDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.StageId) || document.Releases.Count == 0)
        {
            return;
        }

        try
        {
            document.SchemaVersion = ChangelogDocument.CurrentSchemaVersion;
            if (TryLoad(document.StageId) is { } current) document.Releases = MergeReleases(document.Releases, current.Releases);
            ChangelogFormatter.EnsureSummaries(document.Releases);
            Directory.CreateDirectory(CacheDirectory);
            var path = GetCacheFilePath(document.StageId);
            var json = JsonSerializer.Serialize(document, JsonOptions);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json, Encoding.UTF8);
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public List<ChangelogRelease> MergeReleases(IReadOnlyList<ChangelogRelease> fresh, IReadOnlyList<ChangelogRelease> cached)
    {
        if (cached.Count == 0)
        {
            var copy = fresh.ToList();
            ChangelogFormatter.EnsureSummaries(copy);
            return copy;
        }

        var cachedByKey = new Dictionary<string, ChangelogRelease>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cached)
        {
            var key = ChangelogFormatter.ReleaseKey(item);
            if (!string.IsNullOrEmpty(key) && !cachedByKey.ContainsKey(key))
            {
                cachedByKey[key] = item;
            }
        }

        var merged = new List<ChangelogRelease>(fresh.Count + cached.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in fresh)
        {
            var key = ChangelogFormatter.ReleaseKey(item);
            if (!string.IsNullOrEmpty(key))
            {
                seen.Add(key);
                if (cachedByKey.TryGetValue(key, out var previous))
                {
                    if (ChangelogParseHelper.IsPlaceholderOnlyGroups(item.Groups)
                        && !ChangelogParseHelper.IsPlaceholderOnlyGroups(previous.Groups))
                    {
                        item.Groups = previous.Groups;
                        item.CompareUrl = null;
                    }

                    if (previous.HasTranslation && !item.HasTranslation)
                    {
                        item.TranslatedTitle = previous.TranslatedTitle;
                        item.TranslatedSummary = previous.TranslatedSummary;
                        item.TranslatedGroups = previous.TranslatedGroups;
                        item.Translation = previous.Translation;
                    }
                }
            }

            merged.Add(item);
        }

        foreach (var old in cached)
        {
            var key = ChangelogFormatter.ReleaseKey(old);
            if (!string.IsNullOrEmpty(key) && seen.Add(key))
            {
                merged.Add(old);
            }
        }

        var result = LimitMergedReleases(merged);
        ChangelogFormatter.EnsureSummaries(result);
        return result;
    }

    private static List<ChangelogRelease> LimitMergedReleases(List<ChangelogRelease> merged)
    {
        if (!ChangelogFormatter.HasMultipleChannels(merged))
        {
            return merged.Take(20).ToList();
        }

        var result = new List<ChangelogRelease>(merged.Count);
        var stableCount = 0;
        var prereleaseCount = 0;
        foreach (var item in merged)
        {
            if (item.IsPrerelease)
            {
                if (prereleaseCount >= 15)
                {
                    continue;
                }

                prereleaseCount++;
            }
            else
            {
                if (stableCount >= 15)
                {
                    continue;
                }

                stableCount++;
            }

            result.Add(item);
        }

        return result;
    }

    private string GetCacheFilePath(string stageId)
    {
        var safe = Regex.Replace(stageId.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return Path.Combine(CacheDirectory, $"{(string.IsNullOrEmpty(safe) ? "stage" : safe)}.json");
    }
}

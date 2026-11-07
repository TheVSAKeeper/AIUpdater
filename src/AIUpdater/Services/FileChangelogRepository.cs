using AIUpdater.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public sealed class FileChangelogRepository(string? cacheDirectory = null) : IChangelogRepository
{
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
            if (doc is not { SchemaVersion: ChangelogDocument.CurrentSchemaVersion, Releases.Count: > 0 })
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
        if (string.IsNullOrWhiteSpace(document.StageId) || document.Releases.Count == 0)
        {
            return;
        }

        try
        {
            document.SchemaVersion = ChangelogDocument.CurrentSchemaVersion;
            ChangelogFormatter.EnsureSummaries(document.Releases);
            Directory.CreateDirectory(CacheDirectory);
            var path = GetCacheFilePath(document.StageId);
            var json = JsonSerializer.Serialize(document, JsonOptions);
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, json, Encoding.UTF8);
                File.Move(temp, path, true);
            }
            catch (UnauthorizedAccessException)
            {
                File.WriteAllText(path, json, Encoding.UTF8);
            }
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
                if (cachedByKey.TryGetValue(key, out var previous) && previous.HasTranslation && !item.HasTranslation)
                {
                    item.TranslatedTitle = previous.TranslatedTitle;
                    item.TranslatedSummary = previous.TranslatedSummary;
                    item.TranslatedGroups = previous.TranslatedGroups;
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

        var result = merged.Take(20).ToList();
        ChangelogFormatter.EnsureSummaries(result);
        return result;
    }

    private string GetCacheFilePath(string stageId)
    {
        var safe = Regex.Replace(stageId.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return Path.Combine(CacheDirectory, $"{(string.IsNullOrEmpty(safe) ? "stage" : safe)}.json");
    }
}

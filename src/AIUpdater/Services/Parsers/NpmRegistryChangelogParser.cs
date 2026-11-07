using AIUpdater.Models;
using System.IO;
using System.Text.Json;

namespace AIUpdater.Services.Parsers;

public sealed class NpmRegistryChangelogParser : IChangelogFormatParser
{
    public bool CanParse(string format) =>
        string.Equals(format, "npm-registry", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ChangelogRelease> Parse(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (!root.TryGetProperty("versions", out var versionsObj) || versionsObj.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Реестр npm не вернул список версий пакета.");
        }

        var publishTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("time", out var timeObj) && timeObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in timeObj.EnumerateObject())
            {
                if (prop.Name is "created" or "modified")
                {
                    continue;
                }

                if (DateTimeOffset.TryParse(prop.Value.GetString(), out var parsed))
                {
                    publishTimes[prop.Name] = parsed;
                }
            }
        }

        var distTags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("dist-tags", out var tagsObj) && tagsObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var tag in tagsObj.EnumerateObject())
            {
                var ver = tag.Value.GetString();
                if (!string.IsNullOrWhiteSpace(ver))
                {
                    if (!distTags.TryGetValue(ver, out var list))
                    {
                        distTags[ver] = list = [];
                    }

                    list.Add(tag.Name);
                }
            }
        }

        var allVersions = versionsObj.EnumerateObject()
            .Select(prop => (Version: prop.Name, Element: prop.Value, Time: publishTimes.GetValueOrDefault(prop.Name, DateTimeOffset.MinValue)))
            .OrderByDescending(entry => entry.Time)
            .ToList();

        var releases = new List<ChangelogRelease>();
        for (var index = 0; index < Math.Min(10, allVersions.Count); index++)
        {
            var current = allVersions[index];
            var previous = index + 1 < allVersions.Count ? allVersions[index + 1] : default;
            var added = new List<string>();
            var changed = new List<string>();

            if (distTags.TryGetValue(current.Version, out var tags))
            {
                changed.Add($"Канал публикации npm: {string.Join(", ", tags.Select(t => $"`{t}`"))}");
            }

            var currentNodeEngine = ReadEngineNode(current.Element);
            var previousNodeEngine = previous.Element.ValueKind == JsonValueKind.Object ? ReadEngineNode(previous.Element) : null;
            if (!string.IsNullOrWhiteSpace(currentNodeEngine))
            {
                if (!string.IsNullOrWhiteSpace(previousNodeEngine) && previousNodeEngine != currentNodeEngine)
                {
                    changed.Add($"Требование к среде Node.js изменено: `{previousNodeEngine}` → `{currentNodeEngine}`");
                }
                else
                {
                    changed.Add($"Требуемая версия среды выполнения Node.js: `{currentNodeEngine}`");
                }
            }

            var currentDeps = ReadPackageDependencies(current.Element);
            var previousDeps = previous.Element.ValueKind == JsonValueKind.Object
                ? ReadPackageDependencies(previous.Element)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (depName, depVer) in currentDeps)
            {
                if (previousDeps.Count > 0 && !previousDeps.ContainsKey(depName))
                {
                    added.Add($"Добавлен модуль `{depName}` версии `{depVer}`");
                }
                else if (previousDeps.TryGetValue(depName, out var oldVer) && oldVer != depVer)
                {
                    changed.Add($"Обновлён компонент `{depName}`: `{oldVer}` → `{depVer}`");
                }
            }

            if (added.Count == 0 && changed.Count <= 2 && currentDeps.Count > 0)
            {
                var summaryDeps = currentDeps
                    .Where(kv => kv.Key.StartsWith("@deepseek-ai/", StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .Select(kv => $"`{kv.Key}@{kv.Value}`")
                    .ToList();

                if (summaryDeps.Count > 0)
                {
                    changed.Add($"Платформенные пакеты выпуска: {string.Join(", ", summaryDeps)}");
                }
            }

            var groups = new List<ChangelogGroup>();
            if (added.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Added,
                    Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Added),
                    Items = added,
                });
            }

            if (changed.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Changed,
                    Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Changed),
                    Items = changed,
                });
            }

            releases.Add(new()
            {
                Version = current.Version,
                Title = $"DeepSeek Harness v{current.Version}",
                PublishedAt = current.Time > DateTimeOffset.MinValue ? current.Time.ToString("yyyy-MM-dd") : null,
                Groups = groups,
            });
        }

        return releases;
    }

    private static string? ReadEngineNode(JsonElement versionElement)
    {
        if (versionElement.TryGetProperty("engines", out var engines)
            && engines.ValueKind == JsonValueKind.Object
            && engines.TryGetProperty("node", out var nodeProp))
        {
            return nodeProp.GetString();
        }

        return null;
    }

    private static Dictionary<string, string> ReadPackageDependencies(JsonElement versionElement)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propName in new[] { "dependencies", "optionalDependencies" })
        {
            if (versionElement.TryGetProperty(propName, out var deps) && deps.ValueKind == JsonValueKind.Object)
            {
                foreach (var dep in deps.EnumerateObject())
                {
                    if (dep.Value.GetString() is { Length: > 0 } ver)
                    {
                        result[dep.Name] = ver;
                    }
                }
            }
        }

        return result;
    }
}

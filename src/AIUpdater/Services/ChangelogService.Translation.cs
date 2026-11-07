using AIUpdater.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public static partial class ChangelogService
{
    public static string CategoryTitle(ChangeCategory category)
    {
        return category switch
        {
            ChangeCategory.Added => "Новое",
            ChangeCategory.Fixed => "Исправлено",
            ChangeCategory.Security => "Безопасность",
            ChangeCategory.Other => "Прочее",
            _ => "Изменено и улучшено",
        };
    }

    public static void EnsureSummaries(IReadOnlyList<ChangelogRelease> releases)
    {
        foreach (var release in releases)
        {
            if (release.TranslatedGroups is { Count: > 0 } translatedGroups)
            {
                foreach (var group in translatedGroups)
                {
                    if (string.IsNullOrWhiteSpace(group.Summary))
                    {
                        group.Summary = BuildGroupSummary(group);
                    }
                }

                if (string.IsNullOrWhiteSpace(release.TranslatedSummary))
                {
                    release.TranslatedSummary = BuildReleaseSummary(translatedGroups);
                }
            }
        }
    }

    public static string BuildGroupSummary(ChangelogGroup group)
    {
        if (group.Items.Count == 0)
        {
            return "";
        }

        var snippets = group.Items
            .Take(2)
            .Select(ShortenForSummary)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (snippets.Count == 0)
        {
            return $"Пунктов в разделе: {group.Items.Count}";
        }

        var joined = string.Join("; ", snippets);
        if (group.Items.Count > snippets.Count)
        {
            joined += $" (и ещё {group.Items.Count - snippets.Count})";
        }

        return joined;
    }

    public static string BuildReleaseSummary(IReadOnlyList<ChangelogGroup> groups)
    {
        if (groups.Count == 0)
        {
            return "";
        }

        var total = groups.Sum(g => g.Items.Count);
        var breakdown = string.Join(", ", groups.Select(g => $"{g.Title.ToLowerInvariant()}: {g.Items.Count}"));
        var highlights = groups
            .SelectMany(g => g.Items.Take(1))
            .Take(2)
            .Select(ShortenForSummary)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return highlights.Count > 0
            ? $"Всего изменений: {total} ({breakdown}). Главное: {string.Join("; ", highlights)}."
            : $"Всего изменений в выпуске: {total} ({breakdown}).";
    }

    private static string ShortenForSummary(string item)
    {
        var clean = Regex.Replace(item, @"\(\s*#\d+(?:\s*,\s*#\d+)*\s*\)", "").Trim();
        var sentenceEnd = clean.IndexOf(". ", StringComparison.Ordinal);
        if (sentenceEnd is > 12 and < 110)
        {
            clean = clean[..sentenceEnd];
        }

        clean = clean.TrimEnd('.', ';', ',');
        return clean.Length > 95 ? clean[..92].TrimEnd() + "…" : clean;
    }

    private static (List<ChangelogGroup> Groups, string? Summary) ParseTranslatedRelease(string translatedText, List<ChangelogGroup> originalGroups)
    {
        var buckets = new Dictionary<ChangeCategory, List<string>>
        {
            [ChangeCategory.Added] = [],
            [ChangeCategory.Changed] = [],
            [ChangeCategory.Fixed] = [],
            [ChangeCategory.Security] = [],
            [ChangeCategory.Other] = [],
        };
        var groupSummaries = new Dictionary<ChangeCategory, string>();
        var releaseSummaryLines = new List<string>();
        var inReleaseSummary = false;

        var currentCategory = originalGroups.FirstOrDefault()?.Category ?? ChangeCategory.Changed;
        foreach (var rawLine in translatedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().Replace('\u2014', '–');
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            if (Regex.IsMatch(line, @"^\[(?:Summary|Резюме|Сводка)\]$", RegexOptions.IgnoreCase))
            {
                inReleaseSummary = true;
                continue;
            }

            var sectionMatch = Regex.Match(line, @"^\[(?<sec>Added|Changed|Fixed|Security|Other|Новое|Изменено|Исправлено|Безопасность|Прочее)\]$", RegexOptions.IgnoreCase);
            if (sectionMatch.Success)
            {
                inReleaseSummary = false;
                var token = sectionMatch.Groups["sec"].Value;
                currentCategory = token.ToLowerInvariant() switch
                {
                    "added" or "новое" => ChangeCategory.Added,
                    "fixed" or "исправлено" => ChangeCategory.Fixed,
                    "security" or "безопасность" => ChangeCategory.Security,
                    "other" or "прочее" => ChangeCategory.Other,
                    _ => ChangeCategory.Changed,
                };
                continue;
            }

            var summaryLineMatch = Regex.Match(line, @"^(?:Summary|Резюме|Сводка|Кратко)\s*:\s*(?<sum>.+)$", RegexOptions.IgnoreCase);
            if (summaryLineMatch.Success)
            {
                var summaryVal = summaryLineMatch.Groups["sum"].Value.Trim();
                if (inReleaseSummary)
                {
                    releaseSummaryLines.Add(summaryVal);
                }
                else if (!string.IsNullOrWhiteSpace(summaryVal))
                {
                    groupSummaries[currentCategory] = summaryVal;
                }

                continue;
            }

            var item = Regex.Replace(line, @"^(?:[-*•·]|\d+\.)\s+", "").Trim();
            if (item.Length < 3)
            {
                continue;
            }

            if (inReleaseSummary)
            {
                releaseSummaryLines.Add(item);
                continue;
            }

            buckets[currentCategory].Add(item);
        }

        var result = new List<ChangelogGroup>();
        foreach (var category in new[] { ChangeCategory.Added, ChangeCategory.Changed, ChangeCategory.Fixed, ChangeCategory.Security, ChangeCategory.Other })
        {
            if (buckets[category].Count > 0)
            {
                var group = new ChangelogGroup
                {
                    Category = category,
                    Title = CategoryTitle(category),
                    Items = buckets[category],
                };
                group.Summary = groupSummaries.TryGetValue(category, out var customSum) && !string.IsNullOrWhiteSpace(customSum)
                    ? customSum
                    : BuildGroupSummary(group);
                result.Add(group);
            }
        }

        var releaseSummary = releaseSummaryLines.Count > 0
            ? string.Join(" ", releaseSummaryLines)
            : result.Count > 0 ? BuildReleaseSummary(result) : null;

        return (result, releaseSummary);
    }

    private static async Task<List<ChangelogRelease>> LoadClaudeDesktopReleasesAsync(
        UpdateStage stage,
        ChangelogSource source,
        CancellationToken cancellationToken)
    {
        if (source.DownloadUrl is not null)
        {
            try
            {
                var html = await DownloadStringAsync(source.DownloadUrl, cancellationToken);
                var parsed = ParseHtmlReleases(html);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
            {
            }
        }

        var releases = new List<ChangelogRelease>();
        var installedVersion = stage.InstalledVersion ?? stage.AvailableVersion;
        if (!string.IsNullOrWhiteSpace(installedVersion))
        {
            var items = new List<string>
            {
                $"Установленная сборка пакета `Anthropic.Claude`: `v{installedVersion}`",
            };

            if (!string.IsNullOrWhiteSpace(stage.AvailableVersion) && stage.AvailableVersion != stage.InstalledVersion)
            {
                items.Add($"Доступно обновление в каталоге winget: `v{stage.AvailableVersion}`");
            }

            items.Add("Полные примечания к выпускам Claude Desktop публикуются на портале поддержки Anthropic (кнопка «Открыть источник»).");
            releases.Add(new()
            {
                Version = installedVersion!,
                Title = $"Claude Desktop v{installedVersion}",
                PublishedAt = stage.CheckedAt?.ToLocalTime().ToString("yyyy-MM-dd"),
                Groups =
                [
                    new()
                    {
                        Category = ChangeCategory.Changed,
                        Title = CategoryTitle(ChangeCategory.Changed),
                        Items = items,
                    },
                ],
            });
        }

        try
        {
            var claudeCodeMd = await DownloadStringAsync("https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md", cancellationToken);
            var codeReleases = ParseMarkdownReleases(claudeCodeMd).Take(5).ToList();
            foreach (var codeRelease in codeReleases)
            {
                codeRelease.Title = $"Claude Code / Cowork {codeRelease.Title}";
                releases.Add(codeRelease);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
        }

        return releases;
    }

    private static async Task<List<ChangelogRelease>> LoadAntigravityDesktopReleasesAsync(
        UpdateStage stage,
        ChangelogSource source,
        CancellationToken cancellationToken)
    {
        if (source.DownloadUrl is not null)
        {
            try
            {
                var html = await DownloadStringAsync(source.DownloadUrl, cancellationToken);
                var parsed = ParseHtmlReleases(html);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
            {
            }
        }

        try
        {
            var agyText = await LoadAgyAsync(cancellationToken);
            var agyReleases = ParseAgyReleases(agyText);
            foreach (var item in agyReleases)
            {
                item.Title = $"Google Antigravity v{item.Version}";
            }

            if (agyReleases.Count > 0)
            {
                return agyReleases;
            }
        }
        catch (InvalidDataException)
        {
        }

        return [];
    }

    private static async Task<List<ChangelogRelease>> LoadPluginReleasesAsync(
        string cliName,
        UpdateStage stage,
        CancellationToken cancellationToken)
    {
        var releases = new List<ChangelogRelease>();
        try
        {
            var output = await RunCommandCaptureAsync(cliName, ["plugin", "list", "--json"], 10, cancellationToken);
            if (!string.IsNullOrWhiteSpace(output))
            {
                using var doc = JsonDocument.Parse(output);
                var root = doc.RootElement;
                var items = new List<string>();
                if (cliName == "codex"
                    && root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("installed", out var installedArr)
                    && installedArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var plugin in installedArr.EnumerateArray())
                    {
                        var id = plugin.TryGetProperty("pluginId", out var pId) ? pId.GetString() : "plugin";
                        var ver = plugin.TryGetProperty("version", out var pVer) ? pVer.GetString() : "bundled";
                        var enabled = !plugin.TryGetProperty("enabled", out var pEn) || pEn.ValueKind != JsonValueKind.False;
                        items.Add($"Плагин `{id}`: версия `{ver}` ({(enabled ? "включён" : "отключён")})");
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var plugin in root.EnumerateArray())
                    {
                        var id = plugin.TryGetProperty("id", out var pId) ? pId.GetString() : "plugin";
                        var ver = plugin.TryGetProperty("version", out var pVer) ? pVer.GetString() : "unknown";
                        var scope = plugin.TryGetProperty("scope", out var pScope) ? pScope.GetString() : "user";
                        var updated = plugin.TryGetProperty("lastUpdated", out var pUpd) ? FormatIsoDate(pUpd.GetString()) : null;
                        items.Add($"Плагин `{id}` (`{scope}`): версия `{ver}`" + (updated is not null ? $", обновлён {updated}" : ""));
                    }
                }

                if (items.Count > 0)
                {
                    releases.Add(new()
                    {
                        Version = $"plugins-{items.Count}",
                        Title = $"Установленные плагины ({items.Count})",
                        PublishedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
                        Groups =
                        [
                            new()
                            {
                                Category = ChangeCategory.Changed,
                                Title = "Состав установленных плагинов",
                                Items = items,
                            },
                        ],
                    });
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or Win32Exception or OperationCanceledException)
        {
        }

        if (releases.Count == 0)
        {
            var info = !string.IsNullOrWhiteSpace(stage.Info) ? stage.Info! : "Сведения о плагинах обновляются при проверке версий.";
            releases.Add(new()
            {
                Version = "plugins",
                Title = stage.Name,
                Groups =
                [
                    new()
                    {
                        Category = ChangeCategory.Changed,
                        Title = "Состояние плагинов",
                        Items =
                        [
                            info,
                            "Каждый плагин имеет собственную историю версий в своём репозитории или маркетплейсе.",
                        ],
                    },
                ],
            });
        }

        return releases;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
            { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = MaxResponseBufferBytes };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AIUpdater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, text/html, application/xhtml+xml, application/xml;q=0.9, */*;q=0.8");
        return client;
    }

    private static async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string? FindAgyExecutable()
    {
        var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory => Path.Combine(directory.Trim('"'), "agy.exe")));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string> LoadAgyAsync(CancellationToken cancellationToken)
    {
        var executable = FindAgyExecutable() ?? throw new InvalidDataException("agy не установлен или не найден.");
        var text = await RunCommandCaptureAsync(executable, ["changelog"], 20, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("agy не вернул историю изменений.");
        }

        return Parse(text, "markdown");
    }

    private static async Task<string?> InvokeAgyFlashTranslationAsync(string prompt, CancellationToken cancellationToken)
    {
        var executable = FindAgyExecutable();
        if (executable is null)
        {
            return null;
        }

        try
        {
            return await RunCommandCaptureAsync(
                executable,
                ["--model", "gemini-3.8-flash-low", "--effort", "low", "--disable-slash-commands", "-p", prompt],
                35,
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or Win32Exception or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }

    private static async Task<string> RunCommandCaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error);
            if (process.ExitCode != 0)
            {
                throw new InvalidDataException($"{Path.GetFileNameWithoutExtension(executable)}: {await error}");
            }

            return (await output).Trim();
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }

            throw;
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException($"Не удалось запустить {executable}.", exception);
        }
    }

    private static string GetCacheFilePath(string stageId)
    {
        var safe = Regex.Replace(stageId.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return Path.Combine(CacheDirectory, $"{(string.IsNullOrEmpty(safe) ? "stage" : safe)}.json");
    }

    private static string ReleaseKey(ChangelogRelease release)
    {
        return NormalizeVersion(release.Version) ?? release.Version.Trim();
    }

    private static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = Regex.Match(raw.Trim(), @"\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z._-]+)?");
        return match.Success ? match.Value : raw.Trim().TrimStart('v', 'V');
    }

    private static int? ExtractMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var match = Regex.Match(version, @"\b(\d{2})\.\d+");
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) ? major : null;
    }

    private static string? FormatIsoDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, out var dto) ? dto.ToString("yyyy-MM-dd") : raw;
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

    private static string CapitalizeFirst(string value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static bool IsPrimarilyRussian(ChangelogRelease release)
    {
        var cyrillic = 0;
        var latin = 0;
        foreach (var group in release.Groups)
        {
            foreach (var item in group.Items)
            {
                foreach (var ch in item)
                {
                    if (ch is >= 'А' and <= 'я' or 'Ё' or 'ё')
                    {
                        cyrillic++;
                    }
                    else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
                    {
                        latin++;
                    }
                }
            }
        }

        return cyrillic > latin;
    }
}

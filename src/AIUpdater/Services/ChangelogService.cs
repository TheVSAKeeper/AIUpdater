using AIUpdater.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public static partial class ChangelogService
{
    public const long MaxResponseBufferBytes = 32L * 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Loaded, string Text)> Cache = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIUpdater",
        "Changelogs");

    public static Func<string, CancellationToken, Task<string?>>? TranslatorOverride { get; set; }

    public static ChangelogSource ForStage(string id, string? installedVersion = null)
    {
        var nodeMajor = ExtractMajorVersion(installedVersion) ?? 22;
        return id switch
        {
            "Codex desktop" => new(
                "https://developers.openai.com/codex/changelog?type=codex-app",
                "https://developers.openai.com/codex/changelog/",
                "codex-app",
                "Официальная история изменений настольного приложения ChatGPT / Codex App."),
            "Active Codex CLI" or "Codex npm package" => new(
                "https://github.com/openai/codex/releases",
                "https://api.github.com/repos/openai/codex/releases?per_page=10",
                "github",
                "Официальные выпуски Codex CLI и npm-пакета @openai/codex."),
            "Codex plugins" => new(
                "https://developers.openai.com/codex/changelog/",
                "local://codex-plugins",
                "codex-plugins",
                "Установленные плагины Codex, их версии и локальные заметки о выпусках."),
            "Claude Desktop" => new(
                "https://support.claude.com/en/articles/12138966-release-notes",
                "https://support.claude.com/en/articles/12138966-release-notes",
                "claude-desktop",
                "Официальная история изменений Claude и установленного пакета Claude Desktop."),
            "Claude Code" or "Claude npm package" => new(
                "https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md",
                "https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md",
                "markdown",
                "Официальный журнал изменений Claude Code и npm-пакета @anthropic-ai/claude-code."),
            "Claude plugins" => new(
                "https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md",
                "local://claude-plugins",
                "claude-plugins",
                "Установленные плагины Claude Code, их версии и локальные заметки об изменениях."),
            "Antigravity Desktop" => new(
                "https://antigravity.google/docs/changelog",
                "https://antigravity.google/docs/changelog",
                "antigravity-desktop",
                "Официальная история изменений Google Antigravity."),
            "Antigravity CLI" => new(
                "https://antigravity.google/docs/changelog",
                "local://agy-changelog",
                "agy",
                "Встроенная история agy changelog и опубликованные изменения Google Antigravity."),
            "DSH" => new(
                "https://www.npmjs.com/package/@deepseek-ai/dsh",
                "https://registry.npmjs.org/@deepseek-ai/dsh",
                "npm-registry",
                "История выпусков @deepseek-ai/dsh из реестра npm (включая RC-сборки, требования к Node.js и изменения подпакетов)."),
            "Windows Terminal" => new(
                "https://github.com/microsoft/terminal/releases",
                "https://api.github.com/repos/microsoft/terminal/releases?per_page=10",
                "github",
                "Официальные выпуски Windows Terminal на GitHub."),
            "PowerShell 7" => new(
                "https://github.com/PowerShell/PowerShell/releases",
                "https://api.github.com/repos/PowerShell/PowerShell/releases?per_page=10",
                "github",
                "Официальные выпуски PowerShell 7 на GitHub."),
            "Docker Desktop" => new(
                "https://docs.docker.com/desktop/release-notes/",
                "https://raw.githubusercontent.com/docker/docs/main/content/manuals/desktop/release-notes.md",
                "markdown",
                "Официальная история выпусков Docker Desktop."),
            ".NET SDK 10" => new(
                "https://github.com/dotnet/core/tree/main/release-notes/10.0",
                "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json",
                "dotnet-releases",
                "Официальные метаданные выпусков .NET 10 SDK, Runtime и исправлений безопасности (CVE)."),
            "Git" => new(
                "https://github.com/git-for-windows/git/releases",
                "https://api.github.com/repos/git-for-windows/git/releases?per_page=10",
                "github",
                "Официальные выпуски Git for Windows."),
            "Node.js LTS" => new(
                $"https://github.com/nodejs/node/blob/main/doc/changelogs/CHANGELOG_V{nodeMajor}.md",
                $"https://raw.githubusercontent.com/nodejs/node/main/doc/changelogs/CHANGELOG_V{nodeMajor}.md",
                "markdown",
                $"Официальный журнал изменений ветки Node.js v{nodeMajor} LTS."),
            _ => new(null, null, "none", "Источник истории изменений для этого компонента не указан."),
        };
    }

    public static ChangelogDocument? TryLoadCachedDocument(string stageId)
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

            EnsureSummaries(doc.Releases);
            return doc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void SaveCachedDocument(ChangelogDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.StageId) || document.Releases.Count == 0)
        {
            return;
        }

        try
        {
            document.SchemaVersion = ChangelogDocument.CurrentSchemaVersion;
            EnsureSummaries(document.Releases);
            Directory.CreateDirectory(CacheDirectory);
            var path = GetCacheFilePath(document.StageId);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions), Encoding.UTF8);
            File.Move(temp, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static async Task<(ChangelogDocument Document, bool FromCacheOnly)> LoadDocumentAsync(
        UpdateStage stage,
        ChangelogSource source,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cached = TryLoadCachedDocument(stage.Id);
        if (!forceRefresh && cached is not null && IsCacheUpToDateForStage(cached, stage))
        {
            return (cached, true);
        }

        try
        {
            var etagToUse = forceRefresh ? null : cached?.ETag;
            var (releases, etag, notModified) = await FetchReleasesAsync(stage, source, etagToUse, cancellationToken);
            if (notModified && cached is not null)
            {
                cached.UpdatedAt = DateTimeOffset.UtcNow;
                SaveCachedDocument(cached);
                return (cached, true);
            }

            if (releases.Count == 0)
            {
                if (cached is not null)
                {
                    return (cached, true);
                }

                throw new InvalidDataException("Источник не содержит доступного текста изменений.");
            }

            EnsureSummaries(releases);
            var merged = cached is null ? releases : MergeReleases(releases, cached.Releases);
            EnsureSummaries(merged);
            var document = new ChangelogDocument
            {
                SchemaVersion = ChangelogDocument.CurrentSchemaVersion,
                StageId = stage.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
                ETag = etag ?? cached?.ETag,
                Releases = merged,
            };

            SaveCachedDocument(document);
            return (document, false);
        }
        catch (Exception exception) when (cached is not null && exception is HttpRequestException or InvalidDataException or JsonException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return (cached, true);
        }
    }

    public static Task<bool> TranslateLatestReleaseAsync(ChangelogDocument document, CancellationToken cancellationToken)
    {
        if (document.Releases.Count == 0 || IsPrimarilyRussian(document.Releases[0]))
        {
            return Task.FromResult(false);
        }

        return TranslateReleaseAsync(document, document.Releases[0], cancellationToken);
    }

    public static async Task<bool> TranslateReleaseAsync(
        ChangelogDocument document,
        ChangelogRelease release,
        CancellationToken cancellationToken,
        bool forceRetranslate = false)
    {
        if (release.Groups.Count == 0)
        {
            return false;
        }

        if (!forceRetranslate && release.HasTranslation && !string.IsNullOrWhiteSpace(release.TranslatedSummary))
        {
            return true;
        }

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Переведи на русский язык список изменений релиза ПО и составь краткие резюме.");
        promptBuilder.AppendLine("Правила:");
        promptBuilder.AppendLine("1. Сначала выведи секцию [Summary] и 1–2 лаконичных предложения на русском языке с общим резюме релиза.");
        promptBuilder.AppendLine("2. Далее сохраняй заголовки секций строго в квадратных скобках: [Added], [Changed], [Fixed], [Security], [Other].");
        promptBuilder.AppendLine("3. Сразу под заголовком каждой секции выведи строку \"Summary: <краткая суть этого блока в одну строку>\".");
        promptBuilder.AppendLine("4. Каждый пункт изменений начинай с \"- \".");
        promptBuilder.AppendLine("5. Названия пакетов, команд, флагов CLI, API и фрагменты кода в `обратных кавычках` не переводи.");
        promptBuilder.AppendLine("6. Не добавляй вступлений, пояснений или заключений.");
        promptBuilder.AppendLine();

        foreach (var group in release.Groups)
        {
            var tag = group.Category switch
            {
                ChangeCategory.Added => "Added",
                ChangeCategory.Fixed => "Fixed",
                ChangeCategory.Security => "Security",
                ChangeCategory.Other => "Other",
                _ => "Changed",
            };

            promptBuilder.AppendLine($"[{tag}]");
            foreach (var item in group.Items.Take(20))
            {
                promptBuilder.AppendLine($"- {item}");
            }

            promptBuilder.AppendLine();
        }

        var rawTranslation = TranslatorOverride is not null
            ? await TranslatorOverride(promptBuilder.ToString(), cancellationToken)
            : await InvokeAgyFlashTranslationAsync(promptBuilder.ToString(), cancellationToken);

        if (string.IsNullOrWhiteSpace(rawTranslation))
        {
            return false;
        }

        var (translatedGroups, translatedSummary) = ParseTranslatedRelease(rawTranslation, release.Groups);
        if (translatedGroups.Count == 0)
        {
            return false;
        }

        release.TranslatedTitle = string.IsNullOrWhiteSpace(release.Title) ? release.Version : release.Title;
        release.TranslatedSummary = !string.IsNullOrWhiteSpace(translatedSummary)
            ? translatedSummary
            : BuildReleaseSummary(translatedGroups);
        release.TranslatedGroups = translatedGroups;
        SaveCachedDocument(document);
        return true;
    }

    public static async Task<string> LoadAsync(ChangelogSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Format == "agy")
        {
            var agyText = await LoadAgyAsync(cancellationToken);
            var agyReleases = ParseReleases(agyText, "agy");
            return agyReleases.Count > 0 ? FormatAsPlainText(agyReleases, false) : agyText;
        }

        if (source.DownloadUrl is null)
        {
            return source.Note;
        }

        if (Cache.TryGetValue(source.DownloadUrl, out var cached) && DateTimeOffset.UtcNow - cached.Loaded < TimeSpan.FromMinutes(15))
        {
            return cached.Text;
        }

        var dummyStage = new UpdateStage { Id = "Temp", Name = "Temp", Vendor = "Temp" };
        var (releases, _, _) = await FetchReleasesAsync(dummyStage, source, null, cancellationToken);
        var text = releases.Count > 0 ? FormatAsPlainText(releases, false) : source.Note;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("Источник не содержит доступного текста изменений.");
        }

        Cache[source.DownloadUrl] = (DateTimeOffset.UtcNow, text);
        return text;
    }

    public static string Parse(string content, string format)
    {
        if (format == "github")
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("name", out var name)
                || !root.TryGetProperty("published_at", out var date)
                || !root.TryGetProperty("body", out var body))
            {
                throw new InvalidDataException("Источник не вернул описание выпуска.");
            }

            content = $"{name.GetString()}\nОпубликовано: {date.GetString()}\n\n{body.GetString()}";
        }

        if (format == "html")
        {
            var main = Regex.Match(content, @"<main\b[^>]*>([\s\S]*?)</main>", RegexOptions.IgnoreCase);
            var articles = Regex.Matches(content, @"<article\b[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase);
            if (main.Success)
            {
                content = main.Groups[1].Value;
            }
            else if (articles.Count > 0)
            {
                content = string.Join("\n\n", articles.Select(article => article.Groups[1].Value));
            }
            else
            {
                throw new InvalidDataException("Страница требует просмотра в браузере. Откройте официальный источник.");
            }

            content = Regex.Replace(content, @"<(script|style|nav|footer)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"<(br|/p|/div|/li|/h[1-6])\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"<li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
            content = WebUtility.HtmlDecode(Regex.Replace(content, "<[^>]+>", " "));
            content = Regex.Replace(content, @"[ \t]+", " ");
            content = Regex.Replace(content, @"\n\s*\n\s*\n", "\n\n").Trim();
            if (content.Length < 100)
            {
                throw new InvalidDataException("На странице нет доступного текста изменений. Откройте официальный источник.");
            }
        }

        if (content.Length > 40000)
        {
            var end = content.LastIndexOf('\n', 40000);
            content = content[..(end > 0 ? end : 40000)] + "\n\nПолная история доступна по кнопке «Открыть источник».";
        }

        return content.Trim();
    }

    public static List<ChangelogRelease> ParseReleases(string content, string format)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var releases = format switch
        {
            "github" => ParseGitHubReleases(content),
            "agy" => ParseAgyReleases(content),
            "dotnet-releases" => ParseDotNetReleases(content),
            "npm-registry" => ParseNpmRegistryReleases(content),
            "codex-app" => ParseCodexAppHtmlReleases(content),
            "html" or "claude-desktop" or "antigravity-desktop" => ParseHtmlReleases(content),
            _ => ParseMarkdownReleases(content),
        };

        EnsureSummaries(releases);
        return releases;
    }

    public static string FormatAsPlainText(IReadOnlyList<ChangelogRelease> releases, bool preferTranslation)
    {
        var sb = new StringBuilder();
        for (var index = 0; index < releases.Count; index++)
        {
            var release = releases[index];
            if (index > 0)
            {
                sb.AppendLine().AppendLine(new string('–', 40)).AppendLine();
            }

            var title = preferTranslation && !string.IsNullOrWhiteSpace(release.TranslatedTitle)
                ? release.TranslatedTitle
                : !string.IsNullOrWhiteSpace(release.Title) ? release.Title : release.Version;

            sb.Append(title);
            if (!string.IsNullOrWhiteSpace(release.PublishedAt))
            {
                sb.Append($" ({release.PublishedAt})");
            }

            sb.AppendLine();
            var summary = preferTranslation && !string.IsNullOrWhiteSpace(release.TranslatedSummary)
                ? release.TranslatedSummary
                : release.Summary;
            if (!string.IsNullOrWhiteSpace(summary))
            {
                sb.AppendLine($"Кратко: {summary}");
            }

            var groups = preferTranslation && release.TranslatedGroups is { Count: > 0 }
                ? release.TranslatedGroups
                : release.Groups;

            foreach (var group in groups)
            {
                sb.AppendLine().AppendLine($"[{group.Title}]");
                if (!string.IsNullOrWhiteSpace(group.Summary))
                {
                    sb.AppendLine($"Резюме блока: {group.Summary}");
                }

                foreach (var item in group.Items)
                {
                    sb.AppendLine($"• {item}");
                }
            }
        }

        return sb.ToString().Trim();
    }

    public static List<ChangelogRelease> MergeReleases(List<ChangelogRelease> fresh, List<ChangelogRelease> cached)
    {
        if (cached.Count == 0)
        {
            EnsureSummaries(fresh);
            return fresh;
        }

        var cachedByKey = new Dictionary<string, ChangelogRelease>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cached)
        {
            var key = ReleaseKey(item);
            if (!string.IsNullOrEmpty(key) && !cachedByKey.ContainsKey(key))
            {
                cachedByKey[key] = item;
            }
        }

        var merged = new List<ChangelogRelease>(fresh.Count + cached.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in fresh)
        {
            var key = ReleaseKey(item);
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
            var key = ReleaseKey(old);
            if (!string.IsNullOrEmpty(key) && seen.Add(key))
            {
                merged.Add(old);
            }
        }

        var result = merged.Take(20).ToList();
        EnsureSummaries(result);
        return result;
    }

    private static bool IsCacheUpToDateForStage(ChangelogDocument cached, UpdateStage stage)
    {
        if (cached.Releases.Count == 0)
        {
            return false;
        }

        var targetVersion = NormalizeVersion(stage.AvailableVersion ?? stage.InstalledVersion);
        if (!string.IsNullOrEmpty(targetVersion))
        {
            var hasTarget = cached.Releases.Any(release =>
                string.Equals(NormalizeVersion(release.Version), targetVersion, StringComparison.OrdinalIgnoreCase));

            if (hasTarget && DateTimeOffset.UtcNow - cached.UpdatedAt < TimeSpan.FromHours(6))
            {
                return true;
            }
        }

        return DateTimeOffset.UtcNow - cached.UpdatedAt < TimeSpan.FromMinutes(15);
    }

    private static async Task<(List<ChangelogRelease> Releases, string? ETag, bool NotModified)> FetchReleasesAsync(
        UpdateStage stage,
        ChangelogSource source,
        string? cachedETag,
        CancellationToken cancellationToken)
    {
        if (source.Format == "agy")
        {
            var agyText = await LoadAgyAsync(cancellationToken);
            var releases = ParseAgyReleases(agyText);
            if (!string.IsNullOrWhiteSpace(stage.AvailableVersion)
                && releases.Count > 0
                && !string.Equals(NormalizeVersion(releases[0].Version), NormalizeVersion(stage.AvailableVersion), StringComparison.OrdinalIgnoreCase)
                && source.PageUrl is not null)
            {
                try
                {
                    var webHtml = await DownloadStringAsync(source.PageUrl, cancellationToken);
                    var webReleases = ParseHtmlReleases(webHtml);
                    if (webReleases.Count > 0)
                    {
                        releases = MergeReleases(webReleases, releases);
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
                {
                }
            }

            return (releases, null, false);
        }

        if (source.Format == "codex-plugins")
        {
            return (await LoadPluginReleasesAsync("codex", stage, cancellationToken), null, false);
        }

        if (source.Format == "claude-plugins")
        {
            return (await LoadPluginReleasesAsync("claude", stage, cancellationToken), null, false);
        }

        if (source.Format == "claude-desktop")
        {
            return (await LoadClaudeDesktopReleasesAsync(stage, source, cancellationToken), null, false);
        }

        if (source.Format == "antigravity-desktop")
        {
            return (await LoadAntigravityDesktopReleasesAsync(stage, source, cancellationToken), null, false);
        }

        if (source.DownloadUrl is null)
        {
            return ([], null, false);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source.DownloadUrl);
        if (!string.IsNullOrWhiteSpace(cachedETag) && EntityTagHeaderValue.TryParse(cachedETag, out var etagValue))
        {
            request.Headers.IfNoneMatch.Add(etagValue);
        }

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException) when (source.Format == "github" && source.PageUrl is not null)
        {
            var atomContent = await DownloadStringAsync(source.PageUrl.TrimEnd('/') + ".atom", cancellationToken);
            return (ParseGitHubAtomReleases(atomContent), null, false);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return ([], cachedETag, true);
            }

            if (source.Format == "github"
                && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                && source.PageUrl is not null)
            {
                var atomContent = await DownloadStringAsync(source.PageUrl.TrimEnd('/') + ".atom", cancellationToken);
                return (ParseGitHubAtomReleases(atomContent), null, false);
            }

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var etag = response.Headers.ETag?.ToString();
            var releases = ParseReleases(content, source.Format);
            return (releases, etag, false);
        }
    }
}

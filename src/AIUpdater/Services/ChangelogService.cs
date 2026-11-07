using AIUpdater.Models;
using AIUpdater.Services.Parsers;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AIUpdater.Services;

public sealed class ChangelogService(
    IChangelogContentFetcher fetcher,
    IEnumerable<IChangelogFormatParser> parsers,
    IChangelogRepository repository,
    IChangelogTranslator translator) : IChangelogService
{
    private readonly IChangelogContentFetcher _fetcher = fetcher;
    private readonly IReadOnlyList<IChangelogFormatParser> _parsers = parsers.ToList();

    public ChangelogService()
        : this(
            new ChangelogContentFetcher(),
            CreateDefaultParsers(),
            new FileChangelogRepository(),
            new AgyChangelogTranslator())
    {
    }

    public IChangelogRepository Repository { get; } = repository;

    public IChangelogTranslator Translator { get; } = translator;

    public static IReadOnlyList<IChangelogFormatParser> CreateDefaultParsers() =>
    [
        new GitHubReleasesChangelogParser(),
        new AtomChangelogParser(),
        new AgyCliChangelogParser(),
        new DotNetReleasesChangelogParser(),
        new NpmRegistryChangelogParser(),
        new CodexAppChangelogParser(),
        new HtmlChangelogParser(),
        new MarkdownChangelogParser(),
    ];

    public ChangelogDocument? TryLoadCachedDocument(string stageId) =>
        Repository.TryLoad(stageId);

    public void SaveCachedDocument(ChangelogDocument document) =>
        Repository.Save(document);

    public List<ChangelogRelease> ParseReleases(string content, string format)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var parser = _parsers.FirstOrDefault(item => item.CanParse(format))
                     ?? _parsers.FirstOrDefault(item => item.CanParse("markdown"))
                     ?? new MarkdownChangelogParser();

        var releases = parser.Parse(content).ToList();
        ChangelogFormatter.EnsureSummaries(releases);
        return releases;
    }

    public List<ChangelogRelease> MergeReleases(IReadOnlyList<ChangelogRelease> fresh, IReadOnlyList<ChangelogRelease> cached) =>
        Repository.MergeReleases(fresh, cached);

    public async Task<(ChangelogDocument Document, bool FromCacheOnly)> LoadDocumentAsync(
        UpdateStage stage,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cached = Repository.TryLoad(stage.Id);
        if (!forceRefresh && cached is not null && IsCacheUpToDateForStage(cached, stage))
        {
            return (cached, true);
        }

        var source = stage.GetChangelogSource();
        try
        {
            var etagToUse = forceRefresh ? null : cached?.ETag;
            var (releases, etag, notModified) = await FetchReleasesAsync(stage, source, etagToUse, cancellationToken);
            if (notModified && cached is not null)
            {
                cached.UpdatedAt = DateTimeOffset.UtcNow;
                Repository.Save(cached);
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

            ChangelogFormatter.EnsureSummaries(releases);
            var merged = cached is null ? releases : Repository.MergeReleases(releases, cached.Releases);
            ChangelogFormatter.EnsureSummaries(merged);
            var document = new ChangelogDocument
            {
                SchemaVersion = ChangelogDocument.CurrentSchemaVersion,
                StageId = stage.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
                ETag = etag ?? cached?.ETag,
                Releases = merged,
            };

            Repository.Save(document);
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

    public async Task<bool> TranslateReleaseAsync(
        ChangelogDocument document,
        ChangelogRelease release,
        CancellationToken cancellationToken,
        bool forceRetranslate = false)
    {
        var translated = await Translator.TranslateReleaseAsync(release, cancellationToken, forceRetranslate);
        if (translated)
        {
            Repository.Save(document);
        }

        return translated;
    }

    private static bool IsCacheUpToDateForStage(ChangelogDocument cached, UpdateStage stage)
    {
        if (cached.Releases.Count == 0)
        {
            return false;
        }

        var targetVersion = ChangelogFormatter.NormalizeVersion(stage.AvailableVersion ?? stage.InstalledVersion);
        if (!string.IsNullOrEmpty(targetVersion))
        {
            var hasTarget = cached.Releases.Any(release =>
                string.Equals(ChangelogFormatter.NormalizeVersion(release.Version), targetVersion, StringComparison.OrdinalIgnoreCase));

            if (hasTarget && DateTimeOffset.UtcNow - cached.UpdatedAt < TimeSpan.FromHours(6))
            {
                return true;
            }
        }

        return DateTimeOffset.UtcNow - cached.UpdatedAt < TimeSpan.FromMinutes(15);
    }

    private async Task<(List<ChangelogRelease> Releases, string? ETag, bool NotModified)> FetchReleasesAsync(
        UpdateStage stage,
        ChangelogSource source,
        string? cachedETag,
        CancellationToken cancellationToken)
    {
        switch (source.Format)
        {
            case "agy":
            {
                var raw = await _fetcher.FetchAsync(source, cancellationToken);
                var releases = ParseReleases(raw, "agy");
                if (!string.IsNullOrWhiteSpace(stage.AvailableVersion)
                    && releases.Count > 0
                    && !string.Equals(ChangelogFormatter.NormalizeVersion(releases[0].Version), ChangelogFormatter.NormalizeVersion(stage.AvailableVersion), StringComparison.OrdinalIgnoreCase)
                    && source.PageUrl is not null)
                {
                    try
                    {
                        var webRaw = await _fetcher.DownloadUrlAsync(source.PageUrl, cancellationToken);
                        var webReleases = ParseReleases(webRaw, "html");
                        if (webReleases.Count > 0)
                        {
                            releases = Repository.MergeReleases(webReleases, releases);
                        }
                    }
                    catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
                    {
                    }
                }

                return (releases, null, false);
            }
            case "codex-plugins":
                return (await LoadPluginReleasesAsync("codex", stage, cancellationToken), null, false);
            case "claude-plugins":
                return (await LoadPluginReleasesAsync("claude", stage, cancellationToken), null, false);
            case "claude-desktop":
                return (await LoadClaudeDesktopReleasesAsync(stage, source, cancellationToken), null, false);
            case "antigravity-desktop":
                return (await LoadAntigravityDesktopReleasesAsync(source, cancellationToken), null, false);
        }

        var (content, etag, notModified) = await _fetcher.FetchWithETagAsync(source, cachedETag, cancellationToken);
        if (notModified)
        {
            return ([], etag, true);
        }

        return (ParseReleases(content, source.Format), etag, false);
    }

    private async Task<List<ChangelogRelease>> LoadClaudeDesktopReleasesAsync(
        UpdateStage stage,
        ChangelogSource source,
        CancellationToken cancellationToken)
    {
        if (source.DownloadUrl is not null)
        {
            try
            {
                var html = await _fetcher.DownloadUrlAsync(source.DownloadUrl, cancellationToken);
                var parsed = ParseReleases(html, "html");
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
                        Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Changed),
                        Items = items,
                    },
                ],
            });
        }

        var claudeCodeUrl = UpdateStage.GetChangelogSource("Claude Code").DownloadUrl;
        if (claudeCodeUrl is not null)
        {
            try
            {
                var claudeCodeMd = await _fetcher.DownloadUrlAsync(claudeCodeUrl, cancellationToken);
                var codeReleases = ParseReleases(claudeCodeMd, "markdown").Take(5).ToList();
                foreach (var codeRelease in codeReleases)
                {
                    codeRelease.Title = $"Claude Code / Cowork {codeRelease.Title}";
                    releases.Add(codeRelease);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
            {
            }
        }

        return releases;
    }

    private async Task<List<ChangelogRelease>> LoadAntigravityDesktopReleasesAsync(
        ChangelogSource source,
        CancellationToken cancellationToken)
    {
        if (source.DownloadUrl is not null)
        {
            try
            {
                var html = await _fetcher.DownloadUrlAsync(source.DownloadUrl, cancellationToken);
                var parsed = ParseReleases(html, "html");
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
            var agySource = UpdateStage.GetChangelogSource("Antigravity CLI");
            var agyText = await _fetcher.FetchAsync(agySource, cancellationToken);
            var agyReleases = ParseReleases(agyText, "agy");
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

    private async Task<List<ChangelogRelease>> LoadPluginReleasesAsync(
        string cliName,
        UpdateStage stage,
        CancellationToken cancellationToken)
    {
        var releases = new List<ChangelogRelease>();
        try
        {
            var output = await _fetcher.RunCommandAsync(cliName, ["plugin", "list", "--json"], 10, cancellationToken);
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
                        var updated = plugin.TryGetProperty("lastUpdated", out var pUpd) ? ChangelogParseHelper.FormatIsoDate(pUpd.GetString()) : null;
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
}

using AIUpdater.Bootstrap;
using AIUpdater.Models;
using AIUpdater.Services.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public sealed class ChangelogService(
    IChangelogContentFetcher fetcher,
    IEnumerable<IChangelogFormatParser> parsers,
    IChangelogRepository repository,
    IChangelogTranslator translator,
    ILogger<ChangelogService>? logger = null) : IChangelogService
{
    private readonly IChangelogContentFetcher _fetcher = fetcher;
    private readonly IReadOnlyList<IChangelogFormatParser> _parsers = parsers.ToList();
    private readonly ILogger _logger = logger ?? NullLogger<ChangelogService>.Instance;

    public ChangelogService()
        : this(
            new ChangelogContentFetcher(),
            CreateDefaultParsers(),
            new FileChangelogRepository(),
            new AgentChangelogTranslator())
    {
    }

    public event Action<ChangelogDocument>? DocumentUpdated;

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
        new AntigravityDesktopChangelogParser(),
        new HtmlChangelogParser(),
        new MarkdownChangelogParser(),
    ];

    public ChangelogDocument? TryLoadCachedDocument(string stageId) =>
        Repository.TryLoad(stageId);

    public void SaveCachedDocument(ChangelogDocument document)
    {
        Repository.Save(document);
        DocumentUpdated?.Invoke(document);
    }

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
        var source = stage.GetChangelogSource();
        if (!forceRefresh && cached is not null && IsCacheUpToDateForStage(cached, stage))
        {
            if (EnsureStageBuildCard(stage, source.Format, cached.Releases))
            {
                SaveCachedDocument(cached);
            }

            _logger.ChangelogServedFromFreshCache(stage.Id, cached.Releases.Count);
            return (cached, true);
        }

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var etagToUse = forceRefresh ? null : cached?.ETag;
            var (releases, etag, notModified) = await FetchReleasesAsync(stage, source, etagToUse, cancellationToken);
            if (notModified && cached is not null)
            {
                EnsureStageBuildCard(stage, source.Format, cached.Releases);
                await EnrichCompareReleasesAsync(cached.Releases, cancellationToken);
                cached.UpdatedAt = DateTimeOffset.UtcNow;
                SaveCachedDocument(cached);
                _logger.ChangelogDocumentUpdated(stage.Id, source.Format, cached.Releases.Count, true, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                return (cached, true);
            }

            if (releases.Count == 0)
            {
                if (cached is not null)
                {
                    EnsureStageBuildCard(stage, source.Format, cached.Releases);
                    return (cached, true);
                }

                throw new InvalidDataException("Источник не содержит доступного текста изменений.");
            }

            ChangelogFormatter.EnsureSummaries(releases);
            var merged = cached is null ? releases : Repository.MergeReleases(releases, cached.Releases);
            EnsureStageBuildCard(stage, source.Format, merged);
            await EnrichCompareReleasesAsync(merged, cancellationToken);
            ChangelogFormatter.EnsureSummaries(merged);
            var document = new ChangelogDocument
            {
                SchemaVersion = ChangelogDocument.CurrentSchemaVersion,
                StageId = stage.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
                ETag = etag ?? cached?.ETag,
                Releases = merged,
            };

            SaveCachedDocument(document);
            _logger.ChangelogDocumentUpdated(stage.Id, source.Format, merged.Count, false, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return (document, false);
        }
        catch (Exception exception) when (cached is not null && exception is HttpRequestException or InvalidDataException or JsonException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.ChangelogFallbackToCachedDocument(exception, stage.Id, cached.Releases.Count);
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
            SaveCachedDocument(document);
        }

        return translated;
    }

    public async Task PrefetchInstalledAsync(
        IReadOnlyList<UpdateStage> stages,
        CancellationToken cancellationToken)
    {
        var targets = stages.Where(stage => stage.IsInstalled && !stage.IsDisabled).ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        _logger.ChangelogPrefetchStarting(targets.Length, Translator.Settings.Current.Automatic);

        var loaded = new List<(UpdateStage Stage, ChangelogDocument Document)>(targets.Length);
        var loadedGate = new object();
        using var throttle = new SemaphoreSlim(3, 3);
        await Task.WhenAll(targets.Select(async stage =>
        {
            var acquired = false;
            try
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
                var (document, _) = await LoadDocumentAsync(stage, false, cancellationToken).ConfigureAwait(false);
                lock (loadedGate)
                {
                    loaded.Add((stage, document));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logger.ChangelogPrefetchStageFailed(exception, stage.Id);
            }
            finally
            {
                if (acquired)
                {
                    throttle.Release();
                }
            }
        })).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var orderedLoaded = targets
            .Select(stage => loaded.FirstOrDefault(item => ReferenceEquals(item.Stage, stage)))
            .Where(item => item.Document is not null)
            .ToArray();

        var translatedCount = 0;
        if (Translator.Settings.Current.Automatic)
        {
            var reusableByHash = new Dictionary<string, ChangelogRelease>(StringComparer.Ordinal);
            foreach (var (_, doc) in orderedLoaded)
            {
                foreach (var rel in doc.Releases)
                {
                    if (rel.HasTranslation && !rel.IsTranslationStale)
                    {
                        reusableByHash.TryAdd(TranslationProvenance.Hash(rel), rel);
                    }
                }
            }

            foreach (var (stage, document) in orderedLoaded)
            {
                if (cancellationToken.IsCancellationRequested || !Translator.Settings.Current.Automatic)
                {
                    break;
                }

                var channel = ChangelogFormatter.GetDefaultChannel(stage);
                var visible = ChangelogFormatter.FilterReleasesByChannel(document.Releases, channel);
                var latestCandidate = ChangelogFormatter.FindLatestAutoTranslateCandidate(visible);
                if (latestCandidate is null)
                {
                    continue;
                }

                var hash = TranslationProvenance.Hash(latestCandidate);
                if (reusableByHash.TryGetValue(hash, out var donor))
                {
                    CopyTranslation(donor, latestCandidate);
                    SaveCachedDocument(document);
                    translatedCount++;
                    continue;
                }

                try
                {
                    if (await TranslateReleaseAsync(document, latestCandidate, cancellationToken, false).ConfigureAwait(false))
                    {
                        reusableByHash[hash] = latestCandidate;
                        translatedCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.ChangelogPrefetchStageFailed(exception, stage.Id);
                }
            }
        }

        _logger.ChangelogPrefetchCompleted(orderedLoaded.Length, translatedCount, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private static void CopyTranslation(ChangelogRelease source, ChangelogRelease target)
    {
        target.TranslatedTitle = source.TranslatedTitle;
        target.TranslatedSummary = source.TranslatedSummary;
        target.TranslatedGroups = source.TranslatedGroups?
            .Select(group => new ChangelogGroup
            {
                Category = group.Category,
                Title = group.Title,
                Summary = group.Summary,
                Items = [.. group.Items],
            })
            .ToList();
        target.Translation = source.Translation;
    }

    private static bool IsCacheUpToDateForStage(ChangelogDocument cached, UpdateStage stage)
    {
        if (cached.Releases.Count == 0)
        {
            return false;
        }

        var targetVersion = stage.HasUpdate && !string.IsNullOrWhiteSpace(stage.AvailableVersion)
            ? stage.AvailableVersion
            : stage.InstalledVersion;

        if (!string.IsNullOrEmpty(targetVersion))
        {
            var hasTarget = cached.Releases.Any(release => ChangelogFormatter.VersionsMatch(release.Version, targetVersion));
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
                return (ParseReleases(raw, "agy"), null, false);
            }
            case "antigravity-cli":
                return await LoadAntigravityCliReleasesAsync(source, cachedETag, cancellationToken);
            case "codex-plugins":
                return (await LoadPluginReleasesAsync("codex", stage, cancellationToken), null, false);
            case "claude-plugins":
                return (await LoadPluginReleasesAsync("claude", stage, cancellationToken), null, false);
            case "claude-desktop":
                return (await LoadClaudeDesktopReleasesAsync(stage, source, cancellationToken), null, false);
            case "antigravity-desktop":
                return await LoadAntigravityDesktopReleasesAsync(source, cachedETag, cancellationToken);
        }

        var (content, etag, notModified) = await _fetcher.FetchWithETagAsync(source, cachedETag, cancellationToken);
        if (notModified)
        {
            return ([], etag, true);
        }

        return (ParseReleases(content, source.Format), etag, false);
    }

    private async Task<(List<ChangelogRelease> Releases, string? ETag, bool NotModified)> LoadAntigravityCliReleasesAsync(
        ChangelogSource source,
        string? cachedETag,
        CancellationToken cancellationToken)
    {
        try
        {
            var (content, etag, notModified) = await _fetcher.FetchWithETagAsync(source, cachedETag, cancellationToken);
            if (notModified)
            {
                return ([], etag, true);
            }

            var releases = ParseReleases(content, "github-releases");
            foreach (var release in releases)
            {
                if (Regex.IsMatch(release.Title.Trim(), @"^v?\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z._-]+)?$", RegexOptions.IgnoreCase))
                {
                    var cleanVersion = ChangelogFormatter.NormalizeVersion(release.Version) ?? release.Version;
                    release.Title = $"Antigravity CLI v{cleanVersion}";
                }
            }

            if (releases.Count > 0)
            {
                return (releases, etag, false);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                                          && exception is HttpRequestException or InvalidDataException or JsonException or TaskCanceledException)
        {
            _logger.AntigravityCliGitHubFallbackToLocal(exception);
        }

        var localSource = new ChangelogSource(source.PageUrl, "local://agy-changelog", "agy", source.Note);
        var localRaw = await _fetcher.FetchAsync(localSource, cancellationToken);
        return (ParseReleases(localRaw, "agy"), null, false);
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

    private async Task<(List<ChangelogRelease> Releases, string? ETag, bool NotModified)> LoadAntigravityDesktopReleasesAsync(
        ChangelogSource source,
        string? cachedETag,
        CancellationToken cancellationToken)
    {
        var (content, etag, notModified) = await _fetcher.FetchWithETagAsync(source, cachedETag, cancellationToken);
        if (notModified)
        {
            return ([], etag, true);
        }

        return (ParseReleases(content, "antigravity-desktop"), etag, false);
    }

    private static bool EnsureStageBuildCard(UpdateStage stage, string format, List<ChangelogRelease> releases)
    {
        if (!string.Equals(format, "codex-app", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var storeVersion = stage.HasUpdate
                           && !string.IsNullOrWhiteSpace(stage.AvailableVersion)
                           && !string.Equals(stage.AvailableVersion, stage.InstalledVersion, StringComparison.OrdinalIgnoreCase)
            ? stage.AvailableVersion
            : stage.InstalledVersion;

        var removed = releases.RemoveAll(release =>
            release.Title.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(storeVersion)
                || !string.Equals(ChangelogFormatter.NormalizeVersion(release.Version), ChangelogFormatter.NormalizeVersion(storeVersion), StringComparison.OrdinalIgnoreCase)
                || releases.Any(other => !ReferenceEquals(other, release) && ChangelogFormatter.VersionsMatch(other.Version, release.Version))));

        if (string.IsNullOrWhiteSpace(storeVersion))
        {
            return removed > 0;
        }

        if (releases.Any(release => !release.Title.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase)
                                    && ChangelogFormatter.VersionsMatch(release.Version, storeVersion)))
        {
            return removed > 0;
        }

        var existingStoreCard = releases.FirstOrDefault(release => release.Title.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase));
        ChangelogFormatter.TryExtractCalVerPrefix(storeVersion, out var shortCalVer, out var buildIsoDate);
        var publishedAt = buildIsoDate ?? stage.CheckedAt?.ToLocalTime().ToString("yyyy-MM-dd");

        var items = new List<string>();
        if (!string.IsNullOrWhiteSpace(stage.InstalledVersion))
        {
            var dateSuffix = buildIsoDate is not null && string.Equals(storeVersion, stage.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                ? $" (сборка от {buildIsoDate})"
                : "";
            items.Add($"Установлен пакет Microsoft Store `OpenAI.Codex`: `v{stage.InstalledVersion}`{dateSuffix}.");
        }

        if (stage.HasUpdate)
        {
            if (!string.IsNullOrWhiteSpace(stage.AvailableVersion)
                && !string.Equals(stage.AvailableVersion, stage.InstalledVersion, StringComparison.OrdinalIgnoreCase))
            {
                items.Add($"В Microsoft Store доступно обновление: сборка `v{stage.AvailableVersion}`.");
            }
            else
            {
                items.Add("В Microsoft Store доступно более новое обновление пакета (магазин Windows не раскрывает номер новой сборки и список изменений до установки).");
            }
        }

        items.Add("Подробные заметки к этой сборке настольного приложения ещё не опубликованы в веб-журнале OpenAI; ниже приведены последние опубликованные записи Codex и приложения.");

        var title = !string.IsNullOrEmpty(shortCalVer)
            ? $"ChatGPT / Codex App {shortCalVer} (Microsoft Store v{storeVersion})"
            : $"ChatGPT / Codex App v{storeVersion} (Microsoft Store)";

        var existingIndex = existingStoreCard is null ? -1 : releases.IndexOf(existingStoreCard);
        if (existingStoreCard is not null)
        {
            releases.RemoveAt(existingIndex);
        }

        var insertIndex = 0;
        if (!string.IsNullOrEmpty(publishedAt))
        {
            while (insertIndex < releases.Count
                   && !string.IsNullOrEmpty(releases[insertIndex].PublishedAt)
                   && string.CompareOrdinal(releases[insertIndex].PublishedAt, publishedAt) > 0)
            {
                insertIndex++;
            }
        }

        if (existingStoreCard is not null)
        {
            var sameItems = existingStoreCard.Groups.Count == 1 && existingStoreCard.Groups[0].Items.SequenceEqual(items, StringComparer.Ordinal);
            existingStoreCard.Title = title;
            existingStoreCard.PublishedAt = publishedAt;
            if (!sameItems)
            {
                existingStoreCard.Groups =
                [
                    new()
                    {
                        Category = ChangeCategory.Changed,
                        Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Changed),
                        Items = items,
                    },
                ];
            }

            releases.Insert(insertIndex, existingStoreCard);
            return removed > 0 || !sameItems || existingIndex != insertIndex;
        }

        var card = new ChangelogRelease
        {
            Version = storeVersion!,
            Title = title,
            PublishedAt = publishedAt,
            Groups =
            [
                new()
                {
                    Category = ChangeCategory.Changed,
                    Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Changed),
                    Items = items,
                },
            ],
        };

        releases.Insert(insertIndex, card);
        return true;
    }

    private async Task EnrichCompareReleasesAsync(List<ChangelogRelease> releases, CancellationToken cancellationToken)
    {
        var targets = releases.Where(r => !r.IsPrerelease && !string.IsNullOrWhiteSpace(r.CompareUrl) && ChangelogParseHelper.IsPlaceholderOnlyGroups(r.Groups)).Take(3)
            .Concat(releases.Where(r => r.IsPrerelease && !string.IsNullOrWhiteSpace(r.CompareUrl) && ChangelogParseHelper.IsPlaceholderOnlyGroups(r.Groups)).Take(8))
            .ToList();

        foreach (var release in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var compareJson = await _fetcher.DownloadUrlAsync(release.CompareUrl!, cancellationToken);
                var commitGroups = ParseCompareCommitsIntoGroups(compareJson);
                if (commitGroups.Count > 0)
                {
                    release.Groups = commitGroups;
                }
                else
                {
                    release.Groups =
                    [
                        new()
                        {
                            Category = ChangeCategory.Changed,
                            Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Changed),
                            Items = [$"Сборка {release.Version} без отдельных изменений кода относительно предыдущего тега."],
                        },
                    ];
                }

                release.CompareUrl = null;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or JsonException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _logger.CompareCommitsEnrichmentStopped(exception, release.Version);
                break;
            }
        }
    }

    private static List<ChangelogGroup> ParseCompareCommitsIntoGroups(string compareJson)
    {
        using var doc = JsonDocument.Parse(compareJson);
        if (!doc.RootElement.TryGetProperty("commits", out var commitsArr) || commitsArr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var subjects = new List<string>();
        foreach (var item in commitsArr.EnumerateArray().Reverse())
        {
            if (!item.TryGetProperty("commit", out var commitObj)
                || !commitObj.TryGetProperty("message", out var msgProp)
                || msgProp.GetString() is not { Length: > 0 } fullMessage)
            {
                continue;
            }

            var firstLine = fullMessage.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(firstLine)
                || firstLine.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(firstLine, @"^(?:chore(?:\(release\))?:?\s*)?(?:release|bump\s+version)\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(firstLine, @"^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z._-]+)?$"))
            {
                continue;
            }

            subjects.Add($"- {firstLine}");
            if (subjects.Count >= 25)
            {
                break;
            }
        }

        return subjects.Count == 0 ? [] : ChangelogParseHelper.ParseBodyIntoGroups(string.Join('\n', subjects));
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
            _logger.PluginListFetchFailed(exception, cliName);
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

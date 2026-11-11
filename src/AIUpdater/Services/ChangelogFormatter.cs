using AIUpdater.Models;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace AIUpdater.Services;

public sealed record ChangelogCardData(
    string ReleaseKey,
    string Title,
    string? PublishedAt,
    bool IsLatest,
    double BaseFontSize,
    bool IsCollapsed,
    string? Summary,
    bool IsTranslating,
    string TranslationProgressText,
    string? TranslationErrorText,
    bool ShowRuForCard,
    bool HasError,
    IReadOnlyList<ReleaseBadge> Badges,
    IReadOnlyList<(string GroupKey, ChangelogGroup Group, bool IsCollapsed)> Groups);

public sealed class ChangelogPresentationState : IDisposable
{
    private readonly DispatcherTimer _translationTimer;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _translationCancellation;
    private string? _translatingReleaseKey;
    private string? _translatingReleaseTitle;
    private int _translatingGroupCount;
    private int _translatingItemCount;
    private DateTimeOffset _translationStartedAt;

    public ChangelogPresentationState(Action onTimerTick)
    {
        _translationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _translationTimer.Tick += (_, _) => onTimerTick();
    }

    public ChangelogDocument? CurrentDocument { get; set; }

    public UpdateStage? CurrentStage { get; set; }

    public ReleaseChannel SelectedChannel { get; set; } = ReleaseChannel.Stable;

    public HashSet<string> CollapsedGroupKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CollapsedReleaseKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> OriginalOverrideReleaseKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> TranslationErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? TranslatingReleaseKey => _translatingReleaseKey;

    public int TranslatingGroupCount => _translatingGroupCount;

    public int TranslatingItemCount => _translatingItemCount;

    public DateTimeOffset TranslationStartedAt => _translationStartedAt;

    public CancellationToken ActiveLoadToken => _loadCancellation?.Token ?? CancellationToken.None;

    public static void SetCollapsedKey(HashSet<string> keys, string key, bool isCollapsed)
    {
        if (isCollapsed)
        {
            keys.Add(key);
        }
        else
        {
            keys.Remove(key);
        }
    }

    public CancellationToken BeginStageLoad(UpdateStage stage)
    {
        if (!ReferenceEquals(CurrentStage, stage))
        {
            CollapsedGroupKeys.Clear();
            CollapsedReleaseKeys.Clear();
            OriginalOverrideReleaseKeys.Clear();
            TranslationErrors.Clear();
            SelectedChannel = ChangelogFormatter.GetDefaultChannel(stage);
        }

        CurrentStage = stage;
        CancelAndDispose(ref _translationCancellation);
        StopTranslationBanner();
        CancelAndDispose(ref _loadCancellation);
        var cancellation = _loadCancellation = new();
        return cancellation.Token;
    }

    public void CancelTranslation() =>
        CancelSafe(_translationCancellation);

    public void StopTranslationBanner()
    {
        _translationTimer.Stop();
        _translatingReleaseKey = null;
    }

    public ChangelogCardData PrepareCardData(
        ChangelogRelease release,
        int index,
        UpdateStage? stage,
        double baseFontSize,
        bool showTranslation)
    {
        var releaseKey = ChangelogFormatter.ReleaseKey(release);
        var isLatest = index == 0;
        var isTranslating = string.Equals(_translatingReleaseKey, releaseKey, StringComparison.OrdinalIgnoreCase);
        var showRuForCard = showTranslation && release.HasTranslation && !OriginalOverrideReleaseKeys.Contains(releaseKey);
        var badges = ChangelogFormatter.BuildBadges(release, stage, isLatest, showRuForCard, Math.Max(10, baseFontSize - 3));
        var displayTitle = showRuForCard && !string.IsNullOrWhiteSpace(release.TranslatedTitle)
            ? release.TranslatedTitle!
            : !string.IsNullOrWhiteSpace(release.Title) ? release.Title : $"v{release.Version}";

        var sourceGroups = showRuForCard && release.TranslatedGroups is { Count: > 0 }
            ? release.TranslatedGroups
            : release.Groups;

        var groups = sourceGroups
            .Select(group =>
            {
                var groupKey = $"{releaseKey}::{group.Category}";
                return (groupKey, group, CollapsedGroupKeys.Contains(groupKey));
            })
            .ToList();

        var summaryText = showRuForCard && !string.IsNullOrWhiteSpace(release.TranslatedSummary)
            ? release.TranslatedSummary
            : release.Summary;

        var hasError = TranslationErrors.TryGetValue(releaseKey, out var errorText);
        var progressText = isTranslating
            ? ChangelogFormatter.BuildTranslationProgressText(
                release.Groups.Count,
                release.Groups.Sum(g => g.Items.Count),
                _translationStartedAt)
            : "";

        return new ChangelogCardData(
            releaseKey,
            displayTitle,
            release.PublishedAt,
            isLatest,
            baseFontSize,
            CollapsedReleaseKeys.Contains(releaseKey),
            summaryText,
            isTranslating,
            progressText,
            errorText,
            showRuForCard,
            hasError,
            badges,
            groups);
    }

    public async Task LoadStageAsync(
        IChangelogService service,
        UpdateStage stage,
        bool forceRefresh,
        Action<ChangelogDocument, UpdateStage?> onRender,
        Action onClearDisplay,
        Action<string> onStatus,
        Action<string> onFallbackText,
        Action onScrollHome,
        Action onTranslated)
    {
        var token = BeginStageLoad(stage);
        var cached = service.TryLoadCachedDocument(stage.Id);
        if (cached is not null)
        {
            CurrentDocument = cached;
            ChangelogFormatter.AddDefaultCollapsedReleases(cached.Releases, CollapsedReleaseKeys);
            onRender(cached, stage);
            onStatus("Показан сохранённый журнал · проверка новых выпусков…");
        }
        else
        {
            CurrentDocument = null;
            onClearDisplay();
            onStatus("Загрузка истории изменений…");
        }

        try
        {
            var (document, fromCacheOnly) = await service.LoadDocumentAsync(stage, forceRefresh, token);
            if (token.IsCancellationRequested || !ReferenceEquals(CurrentStage, stage))
            {
                return;
            }

            CurrentDocument = document;
            ChangelogFormatter.AddDefaultCollapsedReleases(document.Releases, CollapsedReleaseKeys);
            onRender(document, stage);
            onScrollHome();

            var visibleReleases = ChangelogFormatter.FilterReleasesByChannel(document.Releases, SelectedChannel);
            var latestToTranslate = visibleReleases
                .Take(2)
                .FirstOrDefault(release => (!release.HasTranslation || release.IsTranslationStale) && !ChangelogFormatter.IsPrimarilyRussian(release));
            var statusPrefix = fromCacheOnly
                ? $"Из кэша ({document.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}, выпусков: {visibleReleases.Count})"
                : $"Загружено выпусков: {visibleReleases.Count}";

            if (service.Translator.Settings.Current.Automatic && latestToTranslate is not null)
            {
                onStatus(statusPrefix + $" · Обработка свежего выпуска через {service.Translator.DisplayName}…");
                await TranslateReleaseAsync(service, document, latestToTranslate, stage, false, statusPrefix, onRender, onStatus, onTranslated, token);
            }
            else
            {
                onStatus(statusPrefix);
            }
        }
        catch (OperationCanceledException)
        {
            if (!token.IsCancellationRequested && ReferenceEquals(CurrentStage, stage) && CurrentDocument is null)
            {
                onStatus("Источник не ответил вовремя. Можно открыть его в браузере.");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or JsonException)
        {
            if (token.IsCancellationRequested || !ReferenceEquals(CurrentStage, stage))
            {
                return;
            }

            if (CurrentDocument is not null)
            {
                onStatus("Показан сохранённый журнал (источник сейчас недоступен).");
                return;
            }

            onStatus("Не удалось загрузить изменения. Можно открыть официальный источник.");
            onFallbackText(exception.Message);
        }
    }

    public async Task<bool> TranslateReleaseAsync(
        IChangelogService service,
        ChangelogDocument document,
        ChangelogRelease release,
        UpdateStage? stage,
        bool forceRetranslate,
        string statusPrefix,
        Action<ChangelogDocument, UpdateStage?> onRender,
        Action<string> onStatus,
        Action onTranslated,
        CancellationToken parentToken)
    {
        CancelAndDispose(ref _translationCancellation);
        CancellationTokenSource linked;
        try
        {
            linked = parentToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
                : new CancellationTokenSource();
        }
        catch (ObjectDisposedException)
        {
            linked = new CancellationTokenSource();
        }

        _translationCancellation = linked;
        var token = linked.Token;
        var key = ChangelogFormatter.ReleaseKey(release);
        CollapsedReleaseKeys.Remove(key);
        TranslationErrors.Remove(key);
        _translatingReleaseKey = key;
        _translatingReleaseTitle = !string.IsNullOrWhiteSpace(release.Title) ? release.Title : $"v{release.Version}";
        _translatingGroupCount = release.Groups.Count;
        _translatingItemCount = release.Groups.Sum(g => g.Items.Count);
        _translationStartedAt = DateTimeOffset.UtcNow;
        _translationTimer.Start();
        onRender(document, stage);

        try
        {
            var translated = await service.TranslateReleaseAsync(document, release, token, forceRetranslate);
            if (token.IsCancellationRequested || !ReferenceEquals(CurrentDocument, document) || !ReferenceEquals(CurrentStage, stage))
            {
                return false;
            }

            StopTranslationBanner();
            if (translated)
            {
                OriginalOverrideReleaseKeys.Remove(key);
                onTranslated();
                onRender(document, stage);
                onStatus($"{statusPrefix} · Выпуск «{_translatingReleaseTitle}» обработан ({service.Translator.DisplayName})");
                return true;
            }

            TranslationErrors[key] = "Не удалось обработать релиз выбранным агентом. Проверьте CLI и повторите попытку.";
            onRender(document, stage);
            onStatus($"{statusPrefix} · Не удалось обработать выпуск");
            return false;
        }
        catch (OperationCanceledException)
        {
            StopTranslationBanner();
            if (!parentToken.IsCancellationRequested
                && ReferenceEquals(CurrentDocument, document)
                && ReferenceEquals(CurrentStage, stage))
            {
                TranslationErrors[key] = "Обработка выпуска отменена.";
                onRender(document, stage);
                onStatus($"{statusPrefix} · Обработка отменена");
            }

            return false;
        }
        catch (Exception exception)
        {
            StopTranslationBanner();
            if (ReferenceEquals(CurrentDocument, document) && ReferenceEquals(CurrentStage, stage))
            {
                TranslationErrors[key] = $"Ошибка при обработке выпуска: {exception.Message}";
                onRender(document, stage);
                onStatus($"{statusPrefix} · Ошибка обработки выпуска");
            }

            return false;
        }
        finally
        {
            if (_translatingReleaseKey == key)
            {
                StopTranslationBanner();
            }

            if (ReferenceEquals(_translationCancellation, linked))
            {
                _translationCancellation = null;
            }

            try
            {
                linked.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose()
    {
        _translationTimer.Stop();
        CancelAndDispose(ref _translationCancellation);
        CancelAndDispose(ref _loadCancellation);
    }

    private static void CancelSafe(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void CancelAndDispose(ref CancellationTokenSource? slot)
    {
        var cts = Interlocked.Exchange(ref slot, null);
        if (cts is null)
        {
            return;
        }

        CancelSafe(cts);
        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

public static class ChangelogFormatter
{
    public static bool MatchesStageFilter(UpdateStage stage, bool installedOnly, string searchQuery)
    {
        if (installedOnly && !stage.IsInstalled)
        {
            return false;
        }

        var query = searchQuery.Trim();
        return string.IsNullOrEmpty(query)
               || stage.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || stage.Vendor.Contains(query, StringComparison.OrdinalIgnoreCase)
               || stage.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
               || stage.VersionDisplayText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatLanguageToggleText(bool showTranslation, ChangelogDocument? document, ReleaseChannel channel = ReleaseChannel.None)
    {
        var visible = document is null ? [] : FilterReleasesByChannel(document.Releases, channel);
        var translatedCount = visible.Count(r => r.HasTranslation);
        return showTranslation
            ? translatedCount > 0 ? $"Язык: Русский (ИИ · {translatedCount})" : "Язык: Русский (ИИ)"
            : "Язык: Оригинал";
    }

    public static ReleaseChannel GetDefaultChannel(UpdateStage? stage) =>
        IsPrereleaseVersion(stage?.InstalledVersion) ? ReleaseChannel.Prerelease : ReleaseChannel.Stable;

    public static bool HasMultipleChannels(IReadOnlyList<ChangelogRelease>? releases) =>
        releases is { Count: > 1 }
        && releases.Any(r => !r.IsPrerelease)
        && releases.Any(r => r.IsPrerelease);

    public static IReadOnlyList<ChangelogRelease> FilterReleasesByChannel(IReadOnlyList<ChangelogRelease> releases, ReleaseChannel channel)
    {
        if (channel == ReleaseChannel.None || !HasMultipleChannels(releases))
        {
            return releases;
        }

        return releases
            .Where(r => channel == ReleaseChannel.Prerelease ? r.IsPrerelease : !r.IsPrerelease)
            .ToList();
    }

    public static string FormatChannelToggleText(ReleaseChannel channel, ChangelogDocument? document)
    {
        if (document is null || !HasMultipleChannels(document.Releases))
        {
            return channel == ReleaseChannel.Prerelease ? "Канал: Альфа" : "Канал: Релизы";
        }

        var stableCount = document.Releases.Count(r => !r.IsPrerelease);
        var preCount = document.Releases.Count - stableCount;
        return channel == ReleaseChannel.Prerelease
            ? $"Канал: Альфа ({preCount})"
            : $"Канал: Релизы ({stableCount})";
    }

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

    public static void AddDefaultCollapsedReleases(IReadOnlyList<ChangelogRelease> releases, ISet<string> collapsedReleaseKeys)
    {
        if (HasMultipleChannels(releases))
        {
            AddCollapsedForChannel(releases.Where(r => !r.IsPrerelease).ToList(), collapsedReleaseKeys);
            AddCollapsedForChannel(releases.Where(r => r.IsPrerelease).ToList(), collapsedReleaseKeys);
            return;
        }

        AddCollapsedForChannel(releases, collapsedReleaseKeys);
    }

    public static List<ReleaseBadge> BuildBadges(
        ChangelogRelease release,
        UpdateStage? stage,
        bool isLatest,
        bool showRuForCard,
        double badgeFontSize)
    {
        var isInstalled = stage?.InstalledVersion is { Length: > 0 } inst
                          && VersionsMatch(release.Version, inst);
        var isAvailable = stage?.AvailableVersion is { Length: > 0 } avail
                          && stage.HasUpdate
                          && !string.Equals(avail, stage.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                          && VersionsMatch(release.Version, avail);
        var isPendingStoreUpdate = stage?.HasUpdate == true
                                   && isInstalled
                                   && (string.IsNullOrWhiteSpace(stage.AvailableVersion)
                                       || string.Equals(stage.AvailableVersion, stage.InstalledVersion, StringComparison.OrdinalIgnoreCase))
                                   && release.Title.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase);

        var badges = new List<ReleaseBadge>();
        if (isLatest)
        {
            badges.Add(new("Свежий", "Latest", badgeFontSize));
        }

        if (release.IsPrerelease)
        {
            badges.Add(new("Пререлиз", "Prerelease", badgeFontSize));
        }

        if (isInstalled)
        {
            badges.Add(new("Установлено", "Installed", badgeFontSize));
        }
        else if (isAvailable)
        {
            badges.Add(new("Доступно", "Available", badgeFontSize));
        }

        if (isPendingStoreUpdate)
        {
            badges.Add(new("Есть обновление", "Available", badgeFontSize));
        }

        if (release.HasTranslation && showRuForCard)
        {
            badges.Add(new(release.Translation is { } provenance ? $"{provenance.BackendId} / {provenance.RequestedModel}" : "Обработано ИИ", "Translated", badgeFontSize));
            if (release.IsTranslationStale) badges.Add(new("Исходник изменился", "Prerelease", badgeFontSize));
        }

        return badges;
    }

    public static string BuildTranslationProgressText(int groupCount, int itemCount, DateTimeOffset startedAt)
    {
        var elapsed = Math.Max(0, (int)(DateTimeOffset.UtcNow - startedAt).TotalSeconds);
        return $"Обработка релиза ({elapsed} сек) · разделов: {groupCount}, пунктов: {itemCount} · генерация резюме и перевода…";
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

    public static bool IsPrimarilyRussian(ChangelogRelease release)
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

    public static string ReleaseKey(ChangelogRelease release)
    {
        return NormalizeVersion(release.Version)
            ?? (!string.IsNullOrWhiteSpace(release.Version) ? release.Version.Trim() : release.Title.Trim());
    }

    public static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}-\d{2}-"))
        {
            return trimmed;
        }

        var match = Regex.Match(trimmed, @"\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z._-]+)?");
        return match.Success ? match.Value : trimmed.TrimStart('v', 'V');
    }

    public static bool IsPrereleaseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return Regex.IsMatch(
            raw.Trim(),
            @"^(?:rust-)?v?\d+\.\d+(?:\.\d+)*-(?:alpha|beta|rc\d*|pre|preview|canary|dev|nightly|insiders|next)(?:[._-]|$)",
            RegexOptions.IgnoreCase);
    }

    public static bool VersionsMatch(string? releaseVersion, string? stageVersion)
    {
        var normRelease = NormalizeVersion(releaseVersion);
        var normStage = NormalizeVersion(stageVersion);
        if (string.IsNullOrEmpty(normRelease) || string.IsNullOrEmpty(normStage))
        {
            return false;
        }

        if (string.Equals(normRelease, normStage, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryExtractCalVerPrefix(normRelease, out var calRelease, out _)
               && TryExtractCalVerPrefix(normStage, out var calStage, out _)
               && string.Equals(calRelease, calStage, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryExtractCalVerPrefix(string? raw, out string shortCalVer, out string? isoDate)
    {
        shortCalVer = "";
        isoDate = null;
        var normalized = NormalizeVersion(raw);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        var match = Regex.Match(normalized, @"^(?<yy>\d{2})\.(?<mmdd>\d{3,4})(?:\.\d+){0,2}$");
        if (!match.Success || !int.TryParse(match.Groups["yy"].Value, out var yy))
        {
            return false;
        }

        var mmdd = match.Groups["mmdd"].Value;
        var monthPart = mmdd.Length == 4 ? mmdd[..2] : mmdd[..1];
        var dayPart = mmdd.Length == 4 ? mmdd[2..] : mmdd[1..];
        if (!int.TryParse(monthPart, out var month) || !int.TryParse(dayPart, out var day))
        {
            return false;
        }

        var year = 2000 + yy;
        if (year is < 2020 or > 2099 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        shortCalVer = $"{yy}.{mmdd}";
        isoDate = $"{year:D4}-{month:D2}-{day:D2}";
        return true;
    }

    private static void AddCollapsedForChannel(IReadOnlyList<ChangelogRelease> channelReleases, ISet<string> collapsedReleaseKeys)
    {
        for (var index = 0; index < channelReleases.Count; index++)
        {
            var release = channelReleases[index];
            if (index >= 2 && release.Groups.Sum(g => g.Items.Count) > 4)
            {
                collapsedReleaseKeys.Add(ReleaseKey(release));
            }
        }
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
}

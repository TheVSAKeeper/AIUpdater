using AIUpdater.Bootstrap;
using AIUpdater.Models;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public sealed class DotNetPackageService : IDotNetPackageService
{
    public const string ReleasesIndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly DotNetPackageKind[] PackageOrder =
    [
        DotNetPackageKind.Sdk,
        DotNetPackageKind.DesktopRuntime,
        DotNetPackageKind.AspNetCoreRuntime,
        DotNetPackageKind.Runtime,
    ];

    private static readonly Regex SdkListLinePattern = new(
        @"^(?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)\s+\[",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RuntimeListLinePattern = new(
        @"^(?<name>Microsoft\.(?:NETCore|WindowsDesktop|AspNetCore)\.App)\s+(?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)\s+\[",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IChangelogContentFetcher _fetcher;
    private readonly AppPaths _paths;
    private readonly Func<IReadOnlyList<DotNetStandaloneBundle>> _bundleReader;
    private readonly Func<string?> _dotNetPathResolver;

    public DotNetPackageService(
        IChangelogContentFetcher fetcher,
        AppPaths paths,
        Func<IReadOnlyList<DotNetStandaloneBundle>>? bundleReader = null,
        Func<string?>? dotNetPathResolver = null)
    {
        _fetcher = fetcher;
        _paths = paths;
        _bundleReader = bundleReader ?? ReadWindowsStandaloneBundles;
        _dotNetPathResolver = dotNetPathResolver ?? ResolveDefaultDotNetExecutable;
    }

    public async Task<DotNetCatalogSnapshot> LoadCatalogAsync(
        bool forceRefresh,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        var (installedSdks, installedRuntimes) = await ReadLocalCliInventoryAsync(cancellationToken);
        var bundles = SafeReadBundles();

        string? indexJson = null;
        var offlineFallback = false;
        string? warning = null;
        var indexCacheFile = Path.Combine(_paths.DotNetMetadataDirectory, "releases-index.json");

        if (!forceRefresh && TryReadFreshCache(indexCacheFile, out var freshIndex))
        {
            indexJson = freshIndex;
        }
        else
        {
            try
            {
                indexJson = await _fetcher.DownloadUrlAsync(ReleasesIndexUrl, cancellationToken);
                TryWriteCache(indexCacheFile, indexJson);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (TryReadAnyCache(indexCacheFile, out var staleIndex))
                {
                    indexJson = staleIndex;
                    offlineFallback = true;
                    warning = "Показан сохранённый каталог выпусков Microsoft (сеть недоступна).";
                }
                else
                {
                    offlineFallback = true;
                    warning = $"Не удалось загрузить каталог Microsoft ({ex.Message}). Показаны только локально установленные компоненты.";
                }
            }
        }

        var indexEntries = indexJson is not null ? ParseReleasesIndex(indexJson) : [];
        var allChannelNames = indexEntries.Select(entry => entry.Channel)
            .Concat(installedSdks.Select(DotNetVersionComparer.TryGetChannel).OfType<string>())
            .Concat(installedRuntimes.Select(rt => DotNetVersionComparer.TryGetChannel(rt.Version)).OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .Where(IsSupportedDisplayChannel)
            .OrderByDescending(ParseChannelSortKey)
            .ToArray();

        var indexByChannel = indexEntries.ToDictionary(entry => entry.Channel, StringComparer.Ordinal);
        var channels = new List<DotNetChannelInfo>(allChannelNames.Length);

        foreach (var channelName in allChannelNames)
        {
            indexByChannel.TryGetValue(channelName, out var meta);
            var channelSdks = installedSdks
                .Where(ver => string.Equals(DotNetVersionComparer.TryGetChannel(ver), channelName, StringComparison.Ordinal))
                .OrderByDescending(ver => ver, DotNetVersionComparer.Instance)
                .ToArray();

            var needChannelDetail = meta is not null && (
                meta.SupportPhase != DotNetSupportPhase.Eol
                || channelSdks.Length > 0
                || installedRuntimes.Any(rt => string.Equals(DotNetVersionComparer.TryGetChannel(rt.Version), channelName, StringComparison.Ordinal)));

            ChannelReleaseDetail? detail = null;
            if (needChannelDetail && meta?.ReleasesJsonUrl is { } detailUrl && IsTrustedMetadataUrl(detailUrl))
            {
                detail = await TryLoadChannelDetailAsync(channelName, detailUrl, forceRefresh, cancellationToken);
            }

            channels.Add(BuildChannelInfo(channelName, meta, detail, channelSdks, installedRuntimes, bundles, includePrerelease));
        }

        var ordered = channels
            .OrderBy(ch => GetPhaseSortPriority(ch.SupportPhase))
            .ThenByDescending(ch => ParseChannelSortKey(ch.Channel))
            .ToArray();
        return new DotNetCatalogSnapshot(ordered, DateTimeOffset.Now, offlineFallback, warning);
    }

    private static int GetPhaseSortPriority(DotNetSupportPhase phase) => phase switch
    {
        DotNetSupportPhase.Active or DotNetSupportPhase.GoLive or DotNetSupportPhase.Maintenance => 0,
        DotNetSupportPhase.Preview => 1,
        DotNetSupportPhase.Eol => 2,
        _ => 1,
    };

    public static IReadOnlyList<DotNetStandaloneBundle> ReadWindowsStandaloneBundles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var archLabel = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var sdkRegex = new Regex($@"^Microsoft \.NET SDK (?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?) \({Regex.Escape(archLabel)}\)$", RegexOptions.CultureInvariant);
        var desktopRegex = new Regex($@"^Microsoft Windows Desktop Runtime - (?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?) \({Regex.Escape(archLabel)}\)$", RegexOptions.CultureInvariant);
        var aspRegex = new Regex($@"^Microsoft ASP\.NET Core (?:(?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?) - Shared Framework|Runtime - (?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)) \({Regex.Escape(archLabel)}\)$", RegexOptions.CultureInvariant);
        var runtimeRegex = new Regex($@"^Microsoft \.NET Runtime - (?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?) \({Regex.Escape(archLabel)}\)$", RegexOptions.CultureInvariant);

        var results = new List<DotNetStandaloneBundle>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var item = uninstall.OpenSubKey(subKeyName);
                    if (item is null)
                    {
                        continue;
                    }

                    if (item.GetValue("SystemComponent") is int sysComp && sysComp == 1)
                    {
                        continue;
                    }

                    var publisher = item.GetValue("Publisher") as string;
                    var displayName = item.GetValue("DisplayName") as string;
                    var quietUninstall = item.GetValue("QuietUninstallString") as string;
                    if (!string.Equals(publisher, "Microsoft Corporation", StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(displayName)
                        || string.IsNullOrWhiteSpace(quietUninstall)
                        || !quietUninstall.Contains("/uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var match = sdkRegex.Match(displayName);
                    if (match.Success)
                    {
                        AddDistinctBundle(results, new(DotNetPackageKind.Sdk, match.Groups["ver"].Value, displayName));
                        continue;
                    }

                    match = desktopRegex.Match(displayName);
                    if (match.Success)
                    {
                        AddDistinctBundle(results, new(DotNetPackageKind.DesktopRuntime, match.Groups["ver"].Value, displayName));
                        continue;
                    }

                    match = aspRegex.Match(displayName);
                    if (match.Success)
                    {
                        AddDistinctBundle(results, new(DotNetPackageKind.AspNetCoreRuntime, match.Groups["ver"].Value, displayName));
                        continue;
                    }

                    match = runtimeRegex.Match(displayName);
                    if (match.Success)
                    {
                        AddDistinctBundle(results, new(DotNetPackageKind.Runtime, match.Groups["ver"].Value, displayName));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }

        return results;
    }

    private static void AddDistinctBundle(List<DotNetStandaloneBundle> target, DotNetStandaloneBundle candidate)
    {
        if (!target.Any(existing => existing.Kind == candidate.Kind && string.Equals(existing.Version, candidate.Version, StringComparison.OrdinalIgnoreCase)))
        {
            target.Add(candidate);
        }
    }

    private static string? ResolveDefaultDotNetExecutable()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), "dotnet.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var systemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        return File.Exists(systemPath) ? systemPath : null;
    }

    private IReadOnlyList<DotNetStandaloneBundle> SafeReadBundles()
    {
        try
        {
            return _bundleReader();
        }
        catch
        {
            return [];
        }
    }

    private async Task<(IReadOnlyList<string> Sdks, IReadOnlyList<(DotNetPackageKind Kind, string Version)> Runtimes)> ReadLocalCliInventoryAsync(CancellationToken cancellationToken)
    {
        var dotNetExe = _dotNetPathResolver();
        if (string.IsNullOrWhiteSpace(dotNetExe))
        {
            return ([], []);
        }

        var sdks = new List<string>();
        var runtimes = new List<(DotNetPackageKind Kind, string Version)>();

        try
        {
            var sdkOutput = await _fetcher.RunCommandAsync(dotNetExe, ["--list-sdks"], 15, cancellationToken);
            foreach (var line in sdkOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var match = SdkListLinePattern.Match(line.Trim());
                if (match.Success)
                {
                    var ver = match.Groups["ver"].Value;
                    if (!sdks.Contains(ver, StringComparer.OrdinalIgnoreCase))
                    {
                        sdks.Add(ver);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        try
        {
            var runtimeOutput = await _fetcher.RunCommandAsync(dotNetExe, ["--list-runtimes"], 15, cancellationToken);
            foreach (var line in runtimeOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var match = RuntimeListLinePattern.Match(line.Trim());
                if (!match.Success)
                {
                    continue;
                }

                var kind = match.Groups["name"].Value switch
                {
                    "Microsoft.WindowsDesktop.App" => DotNetPackageKind.DesktopRuntime,
                    "Microsoft.AspNetCore.App" => DotNetPackageKind.AspNetCoreRuntime,
                    "Microsoft.NETCore.App" => DotNetPackageKind.Runtime,
                    _ => DotNetPackageKind.None,
                };
                var ver = match.Groups["ver"].Value;
                if (kind != DotNetPackageKind.None && !runtimes.Any(rt => rt.Kind == kind && string.Equals(rt.Version, ver, StringComparison.OrdinalIgnoreCase)))
                {
                    runtimes.Add((kind, ver));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        return (sdks, runtimes);
    }

    private async Task<ChannelReleaseDetail?> TryLoadChannelDetailAsync(
        string channel,
        string url,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cacheFile = Path.Combine(_paths.DotNetMetadataDirectory, $"releases-{channel}.json");
        string? raw = null;
        if (!forceRefresh && TryReadFreshCache(cacheFile, out var cached))
        {
            raw = cached;
        }
        else
        {
            try
            {
                raw = await _fetcher.DownloadUrlAsync(url, cancellationToken);
                TryWriteCache(cacheFile, raw);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (TryReadAnyCache(cacheFile, out var stale))
                {
                    raw = stale;
                }
            }
        }

        return raw is not null ? ParseChannelDetail(raw, channel) : null;
    }

    private static DotNetChannelInfo BuildChannelInfo(
        string channel,
        IndexChannelEntry? meta,
        ChannelReleaseDetail? detail,
        IReadOnlyList<string> channelSdks,
        IReadOnlyList<(DotNetPackageKind Kind, string Version)> allRuntimes,
        IReadOnlyList<DotNetStandaloneBundle> bundles,
        bool includePrerelease)
    {
        var supportPhase = detail?.SupportPhase is not null and not DotNetSupportPhase.None
            ? detail.SupportPhase
            : meta?.SupportPhase ?? DotNetSupportPhase.None;
        var releaseType = detail?.ReleaseType is not null and not DotNetReleaseType.None
            ? detail.ReleaseType
            : meta?.ReleaseType ?? DotNetReleaseType.None;
        var allowPre = includePrerelease || supportPhase is DotNetSupportPhase.Preview or DotNetSupportPhase.GoLive;

        var hasStandaloneSdkInChannel = channelSdks.Any(ver =>
            bundles.Any(b => b.Kind == DotNetPackageKind.Sdk && string.Equals(b.Version, ver, StringComparison.OrdinalIgnoreCase)));

        var packages = new List<DotNetPackageEntry>(PackageOrder.Length);
        foreach (var kind in PackageOrder)
        {
            if (kind == DotNetPackageKind.Sdk)
            {
                var instances = channelSdks
                    .Select(ver => CreateInstalledInstance(DotNetPackageKind.Sdk, ver, bundles, false, channelSdks.Count > 0))
                    .ToArray();

                var publishedSdks = SelectCandidateVersions(detail?.SdkVersions, meta?.LatestSdk, channel, allowPre);
                var latestChannelSdk = publishedSdks.FirstOrDefault();
                var sdkBands = BuildSdkBandPlans(channelSdks, publishedSdks);
                var hasBandUpdate = sdkBands.Any(band => band.HasUpdate);
                var availableSdk = instances.Length == 0
                    ? latestChannelSdk
                    : sdkBands.Count > 0
                        ? string.Join("; ", sdkBands.Select(band => band.Available))
                        : latestChannelSdk;

                packages.Add(new DotNetPackageEntry(
                    channel,
                    DotNetPackageKind.Sdk,
                    instances,
                    availableSdk,
                    latestChannelSdk,
                    hasBandUpdate,
                    sdkBands));
                continue;
            }

            var kindVersions = allRuntimes
                .Where(rt => rt.Kind == kind && string.Equals(DotNetVersionComparer.TryGetChannel(rt.Version), channel, StringComparison.Ordinal))
                .Select(rt => rt.Version)
                .OrderByDescending(ver => ver, DotNetVersionComparer.Instance)
                .ToArray();

            var runtimeInstances = kindVersions
                .Select(ver => CreateInstalledInstance(kind, ver, bundles, hasStandaloneSdkInChannel, channelSdks.Count > 0))
                .ToArray();

            var detailVersions = kind switch
            {
                DotNetPackageKind.DesktopRuntime => detail?.DesktopRuntimeVersions,
                DotNetPackageKind.AspNetCoreRuntime => detail?.AspNetCoreRuntimeVersions,
                DotNetPackageKind.Runtime => detail?.RuntimeVersions,
                _ => null,
            };

            var availableRuntime = SelectCandidateVersions(detailVersions, meta?.LatestRuntime, channel, allowPre).FirstOrDefault();
            var hasRuntimeUpdate = runtimeInstances.Length > 0
                && !string.IsNullOrWhiteSpace(availableRuntime)
                && DotNetVersionComparer.Instance.Compare(availableRuntime, runtimeInstances[0].Version) > 0;

            packages.Add(new DotNetPackageEntry(
                channel,
                kind,
                runtimeInstances,
                availableRuntime,
                availableRuntime,
                hasRuntimeUpdate,
                []));
        }

        return new DotNetChannelInfo(
            channel,
            releaseType,
            supportPhase,
            detail?.LatestRelease ?? meta?.LatestRelease,
            detail?.LatestSdk ?? meta?.LatestSdk,
            detail?.LatestRuntime ?? meta?.LatestRuntime,
            detail?.EolDate ?? meta?.EolDate,
            packages);
    }

    private static DotNetInstalledInstance CreateInstalledInstance(
        DotNetPackageKind kind,
        string version,
        IReadOnlyList<DotNetStandaloneBundle> bundles,
        bool hasStandaloneSdkInChannel,
        bool hasAnySdkInChannel)
    {
        var bundle = bundles.FirstOrDefault(item =>
            item.Kind == kind && string.Equals(item.Version, version, StringComparison.OrdinalIgnoreCase));
        if (bundle is not null)
        {
            return new DotNetInstalledInstance(version, DotNetInstallOwner.MicrosoftInstaller, bundle.DisplayName);
        }

        if (kind != DotNetPackageKind.Sdk && hasAnySdkInChannel)
        {
            return new DotNetInstalledInstance(
                version,
                hasStandaloneSdkInChannel ? DotNetInstallOwner.SharedWithSdk : DotNetInstallOwner.ExternalOrVisualStudio);
        }

        return new DotNetInstalledInstance(version, DotNetInstallOwner.ExternalOrVisualStudio);
    }

    private static IReadOnlyList<string> SelectCandidateVersions(
        IReadOnlyList<string>? published,
        string? fallbackLatest,
        string channel,
        bool allowPrerelease)
    {
        var source = published is { Count: > 0 }
            ? published
            : !string.IsNullOrWhiteSpace(fallbackLatest)
                ? [fallbackLatest!]
                : [];

        var filtered = source
            .Where(ver => ver.StartsWith(channel + ".", StringComparison.Ordinal) && (allowPrerelease || !DotNetVersionComparer.IsPrerelease(ver)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ver => ver, DotNetVersionComparer.Instance)
            .ToArray();

        return filtered;
    }

    private static IReadOnlyList<DotNetSdkBandPlan> BuildSdkBandPlans(
        IReadOnlyList<string> installedSdks,
        IReadOnlyList<string> publishedSdks)
    {
        if (installedSdks.Count == 0 || publishedSdks.Count == 0)
        {
            return [];
        }

        var plans = new List<DotNetSdkBandPlan>();
        foreach (var group in installedSdks
                     .Where(ver => DotNetVersionComparer.TryGetFeatureBand(ver) is not null)
                     .GroupBy(ver => DotNetVersionComparer.TryGetFeatureBand(ver)!.Value)
                     .OrderByDescending(g => g.Key))
        {
            var current = group.OrderByDescending(ver => ver, DotNetVersionComparer.Instance).First();
            var currentIsPre = DotNetVersionComparer.IsPrerelease(current);
            var available = publishedSdks
                .Where(ver => DotNetVersionComparer.TryGetFeatureBand(ver) == group.Key && (currentIsPre || !DotNetVersionComparer.IsPrerelease(ver)))
                .OrderByDescending(ver => ver, DotNetVersionComparer.Instance)
                .FirstOrDefault();

            if (available is null)
            {
                continue;
            }

            var hasUpdate = DotNetVersionComparer.Instance.Compare(available, current) > 0;
            plans.Add(new DotNetSdkBandPlan(current, available, hasUpdate));
        }

        return plans;
    }

    private static IReadOnlyList<IndexChannelEntry> ParseReleasesIndex(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("releases-index", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<IndexChannelEntry>();
            foreach (var element in array.EnumerateArray())
            {
                var channel = GetString(element, "channel-version");
                if (string.IsNullOrWhiteSpace(channel))
                {
                    continue;
                }

                list.Add(new IndexChannelEntry(
                    channel!,
                    ParseReleaseType(GetString(element, "release-type")),
                    ParseSupportPhase(GetString(element, "support-phase")),
                    GetString(element, "latest-release"),
                    GetString(element, "latest-sdk"),
                    GetString(element, "latest-runtime"),
                    GetString(element, "eol-date"),
                    GetString(element, "releases.json")));
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ChannelReleaseDetail? ParseChannelDetail(string raw, string expectedChannel)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var channel = GetString(root, "channel-version");
            if (!string.Equals(channel, expectedChannel, StringComparison.Ordinal))
            {
                return null;
            }

            var sdks = new List<string>();
            var desktopRuntimes = new List<string>();
            var aspRuntimes = new List<string>();
            var runtimes = new List<string>();

            if (root.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array)
            {
                foreach (var release in releases.EnumerateArray())
                {
                    if (release.TryGetProperty("sdks", out var sdksArray) && sdksArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sdkItem in sdksArray.EnumerateArray())
                        {
                            AddIfValid(sdks, GetString(sdkItem, "version"));
                        }
                    }

                    if (release.TryGetProperty("sdk", out var sdkObj) && sdkObj.ValueKind == JsonValueKind.Object)
                    {
                        AddIfValid(sdks, GetString(sdkObj, "version"));
                    }

                    if (release.TryGetProperty("windowsdesktop", out var wdObj) && wdObj.ValueKind == JsonValueKind.Object)
                    {
                        AddIfValid(desktopRuntimes, GetString(wdObj, "version"));
                    }

                    if (release.TryGetProperty("aspnetcore-runtime", out var aspObj) && aspObj.ValueKind == JsonValueKind.Object)
                    {
                        AddIfValid(aspRuntimes, GetString(aspObj, "version"));
                    }

                    if (release.TryGetProperty("runtime", out var rtObj) && rtObj.ValueKind == JsonValueKind.Object)
                    {
                        AddIfValid(runtimes, GetString(rtObj, "version"));
                    }
                }
            }

            return new ChannelReleaseDetail(
                ParseReleaseType(GetString(root, "release-type")),
                ParseSupportPhase(GetString(root, "support-phase")),
                GetString(root, "latest-release"),
                GetString(root, "latest-sdk"),
                GetString(root, "latest-runtime"),
                GetString(root, "eol-date"),
                sdks,
                desktopRuntimes,
                aspRuntimes,
                runtimes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddIfValid(List<string> target, string? version)
    {
        if (!string.IsNullOrWhiteSpace(version) && !target.Contains(version, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(version);
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DotNetReleaseType ParseReleaseType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "lts" => DotNetReleaseType.Lts,
        "sts" or "current" => DotNetReleaseType.Sts,
        _ => DotNetReleaseType.None,
    };

    private static DotNetSupportPhase ParseSupportPhase(string? raw) => raw?.ToLowerInvariant() switch
    {
        "preview" => DotNetSupportPhase.Preview,
        "go-live" or "rc" => DotNetSupportPhase.GoLive,
        "active" => DotNetSupportPhase.Active,
        "maintenance" => DotNetSupportPhase.Maintenance,
        "eol" => DotNetSupportPhase.Eol,
        _ => DotNetSupportPhase.None,
    };

    private static bool IsSupportedDisplayChannel(string channel)
    {
        var parts = channel.Split('.');
        return parts.Length == 2 && int.TryParse(parts[0], out var major) && major >= 5;
    }

    private static (int Major, int Minor) ParseChannelSortKey(string channel)
    {
        var parts = channel.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        return (major, minor);
    }

    private static bool IsTrustedMetadataUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (string.Equals(uri.Host, "builds.dotnet.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "download.visualstudio.microsoft.com", StringComparison.OrdinalIgnoreCase));

    private static bool TryReadFreshCache(string path, out string content)
    {
        content = "";
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || DateTimeOffset.UtcNow - info.LastWriteTimeUtc > CacheTtl)
            {
                return false;
            }

            content = File.ReadAllText(path, Encoding.UTF8);
            return !string.IsNullOrWhiteSpace(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryReadAnyCache(string path, out string content)
    {
        content = "";
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            content = File.ReadAllText(path, Encoding.UTF8);
            return !string.IsNullOrWhiteSpace(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryWriteCache(string path, string content)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record IndexChannelEntry(
        string Channel,
        DotNetReleaseType ReleaseType,
        DotNetSupportPhase SupportPhase,
        string? LatestRelease,
        string? LatestSdk,
        string? LatestRuntime,
        string? EolDate,
        string? ReleasesJsonUrl);

    private sealed record ChannelReleaseDetail(
        DotNetReleaseType ReleaseType,
        DotNetSupportPhase SupportPhase,
        string? LatestRelease,
        string? LatestSdk,
        string? LatestRuntime,
        string? EolDate,
        IReadOnlyList<string> SdkVersions,
        IReadOnlyList<string> DesktopRuntimeVersions,
        IReadOnlyList<string> AspNetCoreRuntimeVersions,
        IReadOnlyList<string> RuntimeVersions);
}

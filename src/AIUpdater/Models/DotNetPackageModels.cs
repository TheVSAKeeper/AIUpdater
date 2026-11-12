using System.Text.RegularExpressions;

namespace AIUpdater.Models;

public enum DotNetPackageKind
{
    None = 0,
    Sdk = 1,
    DesktopRuntime = 2,
    AspNetCoreRuntime = 3,
    Runtime = 4,
}

public enum DotNetSupportPhase
{
    None = 0,
    Preview = 1,
    GoLive = 2,
    Active = 3,
    Maintenance = 4,
    Eol = 5,
}

public enum DotNetReleaseType
{
    None = 0,
    Sts = 1,
    Lts = 2,
}

public enum DotNetInstallOwner
{
    None = 0,
    MicrosoftInstaller = 1,
    SharedWithSdk = 2,
    ExternalOrVisualStudio = 3,
}

public enum DotNetActionKind
{
    None = 0,
    Install = 1,
    Update = 2,
    Uninstall = 3,
}

public sealed record DotNetStandaloneBundle(
    DotNetPackageKind Kind,
    string Version,
    string DisplayName);

public sealed record DotNetInstalledInstance(
    string Version,
    DotNetInstallOwner Owner,
    string? BundleDisplayName = null)
{
    public bool CanUninstall => Owner == DotNetInstallOwner.MicrosoftInstaller;

    public string OwnerLabel => Owner switch
    {
        DotNetInstallOwner.MicrosoftInstaller => "Установщик Microsoft",
        DotNetInstallOwner.SharedWithSdk => "В составе SDK",
        DotNetInstallOwner.ExternalOrVisualStudio => "Visual Studio / внешний",
        _ => "Неизвестный источник",
    };

    public string OwnerDescription => Owner switch
    {
        DotNetInstallOwner.MicrosoftInstaller => "Установлен отдельным официальным пакетом Microsoft; доступно удаление через интерфейс.",
        DotNetInstallOwner.SharedWithSdk => "Установлен в составе пакета .NET SDK этой ветки.",
        DotNetInstallOwner.ExternalOrVisualStudio => "Установлен через Visual Studio Installer или другой внешний установщик; удаление через AI Updater заблокировано.",
        _ => "Источник установки не определён.",
    };
}

public sealed record DotNetSdkBandPlan(
    string Installed,
    string Available,
    bool HasUpdate);

public sealed record DotNetPackageEntry(
    string Channel,
    DotNetPackageKind Kind,
    IReadOnlyList<DotNetInstalledInstance> InstalledInstances,
    string? AvailableVersion,
    string? LatestChannelVersion,
    bool HasUpdate,
    IReadOnlyList<DotNetSdkBandPlan> SdkBands)
{
    public bool IsInstalled => InstalledInstances.Count > 0;

    public string? HighestInstalledVersion => InstalledInstances.Count > 0 ? InstalledInstances[0].Version : null;

    public bool CanInstallLatestBand =>
        Kind == DotNetPackageKind.Sdk
        && IsInstalled
        && !string.IsNullOrWhiteSpace(LatestChannelVersion)
        && HighestInstalledVersion is not null
        && DotNetVersionComparer.Instance.Compare(LatestChannelVersion, HighestInstalledVersion) > 0
        && DotNetVersionComparer.TryGetFeatureBand(LatestChannelVersion) != DotNetVersionComparer.TryGetFeatureBand(HighestInstalledVersion);

    public static string GetTitle(DotNetPackageKind kind) => kind switch
    {
        DotNetPackageKind.Sdk => ".NET SDK",
        DotNetPackageKind.DesktopRuntime => "Windows Desktop Runtime",
        DotNetPackageKind.AspNetCoreRuntime => "ASP.NET Core Runtime",
        DotNetPackageKind.Runtime => ".NET Runtime",
        _ => ".NET",
    };

    public static string GetSubtitle(DotNetPackageKind kind) => kind switch
    {
        DotNetPackageKind.Sdk => "Компилятор, CLI и все среды выполнения",
        DotNetPackageKind.DesktopRuntime => "Запуск приложений WPF и Windows Forms",
        DotNetPackageKind.AspNetCoreRuntime => "Запуск веб-приложений и серверных служб",
        DotNetPackageKind.Runtime => "Базовая консольная среда выполнения",
        _ => "",
    };

    public static string GetScriptKindName(DotNetPackageKind kind) => kind switch
    {
        DotNetPackageKind.Sdk => "Sdk",
        DotNetPackageKind.DesktopRuntime => "DesktopRuntime",
        DotNetPackageKind.AspNetCoreRuntime => "AspNetCoreRuntime",
        DotNetPackageKind.Runtime => "Runtime",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public sealed record DotNetChannelInfo(
    string Channel,
    DotNetReleaseType ReleaseType,
    DotNetSupportPhase SupportPhase,
    string? LatestRelease,
    string? LatestSdk,
    string? LatestRuntime,
    string? EolDate,
    IReadOnlyList<DotNetPackageEntry> Packages)
{
    public bool IsEol => SupportPhase == DotNetSupportPhase.Eol;

    public bool IsPreview => SupportPhase is DotNetSupportPhase.Preview or DotNetSupportPhase.GoLive;

    public bool HasInstalledPackages => Packages.Any(package => package.IsInstalled);

    public bool HasUpdates => Packages.Any(package => package.HasUpdate);

    public string DisplayTitle => $".NET {Channel.Split('.')[0]}";

    public string BadgeText => SupportPhase switch
    {
        DotNetSupportPhase.Preview => "Preview",
        DotNetSupportPhase.GoLive => "RC · Go-Live",
        DotNetSupportPhase.Eol => "EOL",
        _ => ReleaseType switch
        {
            DotNetReleaseType.Lts => "LTS",
            DotNetReleaseType.Sts => "STS",
            _ => "Активна",
        },
    };

    public string SupportSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(LatestRelease))
            {
                parts.Add($"Выпуск {LatestRelease}");
            }

            if (SupportPhase == DotNetSupportPhase.Eol)
            {
                parts.Add(string.IsNullOrWhiteSpace(EolDate) ? "Поддержка завершена" : $"Поддержка завершена ({EolDate})");
            }
            else if (SupportPhase == DotNetSupportPhase.Maintenance)
            {
                parts.Add("Обслуживание безопасности");
            }
            else if (!string.IsNullOrWhiteSpace(EolDate))
            {
                parts.Add($"Поддержка до {EolDate}");
            }

            return string.Join(" · ", parts);
        }
    }
}

public sealed record DotNetCatalogSnapshot(
    IReadOnlyList<DotNetChannelInfo> Channels,
    DateTimeOffset LoadedAt,
    bool IsOfflineFallback = false,
    string? WarningMessage = null);

public sealed record DotNetOperationRequest(
    DotNetActionKind Action,
    DotNetPackageKind Kind,
    string Channel,
    string? Version = null,
    bool IncludePrerelease = false)
{
    public string ActionScriptName => Action switch
    {
        DotNetActionKind.Install => "Install",
        DotNetActionKind.Update => "Update",
        DotNetActionKind.Uninstall => "Uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(Action)),
    };

    public string DisplayLabel
    {
        get
        {
            var verb = Action switch
            {
                DotNetActionKind.Install => "Установка",
                DotNetActionKind.Update => "Обновление",
                DotNetActionKind.Uninstall => "Удаление",
                _ => "Операция",
            };
            var suffix = string.IsNullOrWhiteSpace(Version) ? $"(.NET {Channel})" : $"{Version}";
            return $"{verb}: {DotNetPackageEntry.GetTitle(Kind)} {suffix}";
        }
    }
}

public sealed class DotNetVersionComparer : IComparer<string?>
{
    private static readonly Regex SemVerPattern = new(
        @"^(?<core>\d+(?:\.\d+){1,3})(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DotNetVersionComparer Instance { get; } = new();

    public static bool IsPrerelease(string? version) =>
        !string.IsNullOrWhiteSpace(version) && version.Contains('-', StringComparison.Ordinal);

    public static string? TryGetChannel(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');
        return parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _)
            ? $"{parts[0]}.{parts[1]}"
            : null;
    }

    public static int? TryGetFeatureBand(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');
        return parts.Length >= 3 && int.TryParse(parts[2], out var patch)
            ? patch / 100
            : null;
    }

    public int Compare(string? x, string? y)
    {
        if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(x))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(y))
        {
            return 1;
        }

        var left = SemVerPattern.Match(x.Trim());
        var right = SemVerPattern.Match(y.Trim());
        if (!left.Success || !right.Success)
        {
            return string.CompareOrdinal(x, y);
        }

        var leftCore = left.Groups["core"].Value.Split('.');
        var rightCore = right.Groups["core"].Value.Split('.');
        for (var i = 0; i < 4; i++)
        {
            var a = i < leftCore.Length && long.TryParse(leftCore[i], out var lv) ? lv : 0L;
            var b = i < rightCore.Length && long.TryParse(rightCore[i], out var rv) ? rv : 0L;
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        var leftPre = left.Groups["pre"].Value;
        var rightPre = right.Groups["pre"].Value;
        if (leftPre.Length == 0 && rightPre.Length == 0)
        {
            return 0;
        }

        if (leftPre.Length == 0)
        {
            return 1;
        }

        if (rightPre.Length == 0)
        {
            return -1;
        }

        var aParts = leftPre.Split('.');
        var bParts = rightPre.Split('.');
        var min = Math.Min(aParts.Length, bParts.Length);
        for (var i = 0; i < min; i++)
        {
            var aNum = long.TryParse(aParts[i], out var an);
            var bNum = long.TryParse(bParts[i], out var bn);
            var cmp = aNum && bNum
                ? an.CompareTo(bn)
                : aNum != bNum
                    ? (aNum ? -1 : 1)
                    : string.CompareOrdinal(aParts[i], bParts[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return aParts.Length.CompareTo(bParts.Length);
    }
}

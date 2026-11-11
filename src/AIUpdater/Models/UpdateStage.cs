using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace AIUpdater.Models;

public sealed class UpdateStage : ObservableObject
{
    private static readonly ChangelogSource CodexCliSource = new(
        "https://github.com/openai/codex/releases",
        "https://api.github.com/repos/openai/codex/releases?per_page=60",
        "github-releases",
        "Официальные выпуски Codex CLI и npm-пакета @openai/codex.");

    private static readonly ChangelogSource ClaudeCodeSource = new(
        "https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md",
        "https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md",
        "markdown",
        "Официальный журнал изменений Claude Code и npm-пакета @anthropic-ai/claude-code.");

    private static readonly StageCatalogEntry[] Catalog =
    [
        new("Codex desktop", "OpenAI", "ChatGPT / Codex", true, static _ => new(
            "https://developers.openai.com/codex/changelog?type=codex-app",
            "https://developers.openai.com/codex/changelog/",
            "codex-app",
            "Официальная история изменений настольного приложения ChatGPT / Codex App.")),
        new("Active Codex CLI", "OpenAI", "Codex CLI", true, static _ => CodexCliSource),
        new("Codex npm package", "OpenAI", "Codex npm", true, static _ => CodexCliSource),
        new("Codex plugins", "OpenAI", "Codex плагины", false, static _ => new(
            "https://developers.openai.com/codex/changelog/",
            "local://codex-plugins",
            "codex-plugins",
            "Установленные плагины Codex, их версии и локальные заметки о выпусках.")),
        new("Claude Desktop", "Anthropic", "Claude Desktop", true, static _ => new(
            "https://support.claude.com/en/articles/12138966-release-notes",
            "https://support.claude.com/en/articles/12138966-release-notes",
            "claude-desktop",
            "Официальная история изменений Claude и установленного пакета Claude Desktop.")),
        new("Claude Code", "Anthropic", "Claude Code", true, static _ => ClaudeCodeSource),
        new("Claude npm package", "Anthropic", "Claude npm", true, static _ => ClaudeCodeSource),
        new("Claude plugins", "Anthropic", "Claude плагины", false, static _ => new(
            ClaudeCodeSource.PageUrl,
            "local://claude-plugins",
            "claude-plugins",
            "Установленные плагины Claude Code, их версии и локальные заметки об изменениях.")),
        new("Antigravity Desktop", "Google", "Antigravity Desktop", true, static _ => new(
            "https://antigravity.google/docs/changelog",
            "https://antigravity.google/docs/changelog",
            "antigravity-desktop",
            "Официальная история изменений настольного приложения Google Antigravity.")),
        new("Antigravity CLI", "Google", "Antigravity CLI (agy)", true, static _ => new(
            "https://github.com/google-antigravity/antigravity-cli/releases",
            "https://api.github.com/repos/google-antigravity/antigravity-cli/releases?per_page=15",
            "antigravity-cli",
            "Официальные выпуски Antigravity CLI (agy) на GitHub с локальным резервом agy changelog.")),
        new("DSH", "DeepSeek", "DeepSeek Harness (DSH)", true, static _ => new(
            "https://www.npmjs.com/package/@deepseek-ai/dsh",
            "https://registry.npmjs.org/@deepseek-ai/dsh",
            "npm-registry",
            "История выпусков @deepseek-ai/dsh из реестра npm (включая RC-сборки, требования к Node.js и изменения подпакетов).")),
        new("Windows Terminal", "Разработка", "Windows Terminal", true, static _ => new(
            "https://github.com/microsoft/terminal/releases",
            "https://api.github.com/repos/microsoft/terminal/releases?per_page=10",
            "github-releases",
            "Официальные выпуски Windows Terminal на GitHub.")),
        new("PowerShell 7", "Разработка", "PowerShell 7", true, static _ => new(
            "https://github.com/PowerShell/PowerShell/releases",
            "https://api.github.com/repos/PowerShell/PowerShell/releases?per_page=10",
            "github-releases",
            "Официальные выпуски PowerShell 7 на GitHub.")),
        new("Docker Desktop", "Разработка", "Docker Desktop", true, static _ => new(
            "https://docs.docker.com/desktop/release-notes/",
            "https://raw.githubusercontent.com/docker/docs/main/content/manuals/desktop/release-notes.md",
            "markdown",
            "Официальная история выпусков Docker Desktop.")),
        new(".NET SDK 10", "Разработка", ".NET SDK 10", true, static _ => new(
            "https://github.com/dotnet/core/tree/main/release-notes/10.0",
            "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json",
            "dotnet-releases",
            "Официальные метаданные выпусков .NET 10 SDK, Runtime и исправлений безопасности (CVE).")),
        new("Git", "Разработка", "Git", true, static _ => new(
            "https://github.com/git-for-windows/git/releases",
            "https://api.github.com/repos/git-for-windows/git/releases?per_page=10",
            "github-releases",
            "Официальные выпуски Git for Windows.")),
        new("Node.js LTS", "Разработка", "Node.js", true, static installedVersion =>
        {
            var nodeMajor = ExtractMajorVersion(installedVersion) ?? 22;
            return new(
                $"https://github.com/nodejs/node/blob/main/doc/changelogs/CHANGELOG_V{nodeMajor}.md",
                $"https://raw.githubusercontent.com/nodejs/node/main/doc/changelogs/CHANGELOG_V{nodeMajor}.md",
                "markdown",
                $"Официальный журнал изменений ветки Node.js v{nodeMajor} LTS.");
        }),
    ];

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Vendor { get; init; }
    public bool CanManageInstallation { get; init; } = true;
    public PackageStatus PackageStatus { get; private set; }
    public OperationState OperationState { get; private set; }
    public string? InstalledVersion { get; private set; }
    public string? AvailableVersion { get; private set; }
    public DateTimeOffset? CheckedAt { get; private set; }
    public string? Info { get; private set; }
    public bool HasUpdate => PackageStatus == PackageStatus.UpdateAvailable;
    public bool IsInstalled => PackageStatus != PackageStatus.NotInstalled
                               && (!string.IsNullOrWhiteSpace(InstalledVersion)
                                   || PackageStatus is PackageStatus.UpToDate or PackageStatus.UpdateAvailable or PackageStatus.DirectUpdate);
    public bool CanSelectForUpdate => HasUpdate || PackageStatus == PackageStatus.DirectUpdate;
    public bool CanUpdate => PackageStatus is PackageStatus.UpToDate or PackageStatus.UpdateAvailable or PackageStatus.Unknown or PackageStatus.DirectUpdate or PackageStatus.CheckFailed;
    public bool IsActiveOperation => OperationState is OperationState.Queued or OperationState.Checking or OperationState.Installing or OperationState.Updating or OperationState.Uninstalling;

    public string? CheckResult => PackageStatus switch
    {
        PackageStatus.NotInstalled => "NOT_INSTALLED",
        PackageStatus.UpToDate => "CURRENT",
        PackageStatus.UpdateAvailable => "AVAILABLE",
        PackageStatus.Unknown => "UNKNOWN",
        PackageStatus.DirectUpdate => "DIRECT",
        PackageStatus.CheckFailed => "ERROR",
        _ => null,
    };

    public bool IsSelectedForUpdate
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string VersionDisplayText => InstalledVersion is null
        ? AvailableVersion is null ? PackageStatus == PackageStatus.DirectUpdate ? Info ?? "" : "" : $"Доступна v{AvailableVersion}"
        : HasUpdate && AvailableVersion is not null && !string.Equals(AvailableVersion, InstalledVersion, StringComparison.OrdinalIgnoreCase)
            ? $"v{InstalledVersion} → v{AvailableVersion}"
            : $"v{InstalledVersion}";

    public string VersionPrefixText => HasUpdate && InstalledVersion is not null && AvailableVersion is not null && !string.Equals(AvailableVersion, InstalledVersion, StringComparison.OrdinalIgnoreCase)
        ? $"v{InstalledVersion} → "
        : VersionDisplayText;

    public string VersionTargetText => HasUpdate && InstalledVersion is not null && AvailableVersion is not null && !string.Equals(AvailableVersion, InstalledVersion, StringComparison.OrdinalIgnoreCase)
        ? $"v{AvailableVersion}"
        : "";

    public Brush VersionBrush => PackageStatus == PackageStatus.UpToDate ? Brushes.MediumAquamarine : Brushes.LightSlateGray;
    public Brush VersionTargetBrush => HasUpdate ? Brushes.Gold : VersionBrush;
    public string CardActionName => CanSelectForUpdate ? $"Выбрать обновление: {Name}" : $"Проверить: {Name}";

    public string ResultDescription => CheckedAt is null
        ? "Проверка ещё не выполнялась"
        : $"Последняя проверка: {CheckedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}\n{VersionDisplayText}"
          + (string.IsNullOrWhiteSpace(Info) || Info == VersionDisplayText ? "" : $"\n{Info}");

    public string Status => OperationState switch
    {
        OperationState.Queued => "В очереди",
        OperationState.Checking => "Проверка…",
        OperationState.Installing => "Установка…",
        OperationState.Updating => "Обновление…",
        OperationState.Uninstalling => "Удаление…",
        OperationState.Failed => "Ошибка выполнения",
        OperationState.Stopped => "Остановлено",
        _ => PackageStatus switch
        {
            PackageStatus.NotInstalled => "Не установлено",
            PackageStatus.UpToDate => "Актуально",
            PackageStatus.UpdateAvailable => "Есть обновление",
            PackageStatus.Unknown => "Не удалось сравнить",
            PackageStatus.DirectUpdate => "Обновляется напрямую",
            PackageStatus.CheckFailed => "Ошибка проверки",
            _ => "Не проверено",
        },
    };

    public Brush StatusBrush => OperationState switch
    {
        OperationState.Failed => Brushes.LightCoral,
        OperationState.Checking or OperationState.Installing or OperationState.Updating or OperationState.Uninstalling => Brushes.Aquamarine,
        OperationState.Queued or OperationState.Stopped => Brushes.LightSlateGray,
        _ => PackageStatus switch
        {
            PackageStatus.UpdateAvailable => Brushes.Gold,
            PackageStatus.UpToDate => Brushes.MediumAquamarine,
            PackageStatus.CheckFailed => Brushes.LightCoral,
            _ => Brushes.LightSlateGray,
        },
    };

    public static UpdateStage[] CreateCatalog()
    {
        return Catalog
            .Select(entry => new UpdateStage
            {
                Id = entry.Id,
                Vendor = entry.Vendor,
                Name = entry.Name,
                CanManageInstallation = entry.CanManageInstallation,
            })
            .ToArray();
    }

    public ChangelogSource GetChangelogSource() =>
        GetChangelogSource(Id, InstalledVersion ?? AvailableVersion);

    public static ChangelogSource GetChangelogSource(string id, string? installedVersion = null)
    {
        var entry = Array.Find(Catalog, item => item.Id == id);
        return entry is not null
            ? entry.SourceFactory(installedVersion)
            : new(null, null, "none", "Источник истории изменений для этого компонента не указан.");
    }

    public static bool TryParseCheckResult(string? result, out PackageStatus status)
    {
        status = result switch
        {
            "AVAILABLE" => PackageStatus.UpdateAvailable,
            "CURRENT" => PackageStatus.UpToDate,
            "NOT_INSTALLED" => PackageStatus.NotInstalled,
            "DIRECT" => PackageStatus.DirectUpdate,
            "ERROR" => PackageStatus.CheckFailed,
            "UNKNOWN" => PackageStatus.Unknown,
            _ => PackageStatus.None,
        };

        return status != PackageStatus.None;
    }

    public void Queue()
    {
        OperationState = OperationState.Queued;
        Refresh();
    }

    public void SetRunning(OperationState state)
    {
        OperationState = state;
        Refresh();
    }

    public void SetCompleted()
    {
        OperationState = OperationState.None;
        Refresh();
    }

    public void SetFailed(bool stopped = false)
    {
        OperationState = stopped ? OperationState.Stopped : OperationState.Failed;
        Refresh();
    }

    public void SetCheckResult(
        string result,
        string? installedVersion = null,
        string? availableVersion = null,
        DateTimeOffset? checkedAt = null,
        string? info = null)
    {
        PackageStatus = TryParseCheckResult(result, out var parsed) ? parsed : PackageStatus.Unknown;
        InstalledVersion = installedVersion;
        AvailableVersion = availableVersion;
        CheckedAt = checkedAt ?? DateTimeOffset.Now;
        Info = info;
        IsSelectedForUpdate = HasUpdate;
        Refresh();
    }

    public void InvalidateResult()
    {
        PackageStatus = PackageStatus.None;
        InstalledVersion = null;
        AvailableVersion = null;
        CheckedAt = null;
        Info = null;
        IsSelectedForUpdate = false;
        Refresh();
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

    private void Refresh()
    {
        OnPropertyChanged(string.Empty);
    }

    private sealed record StageCatalogEntry(
        string Id,
        string Vendor,
        string Name,
        bool CanManageInstallation,
        Func<string?, ChangelogSource> SourceFactory);
}

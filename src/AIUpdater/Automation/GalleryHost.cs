using AIUpdater.Bootstrap;
using AIUpdater.Models;
using AIUpdater.Services;
using AIUpdater.ViewModels;
using AIUpdater.Views;
using KeepShell.Automation;
using KeepShell.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace AIUpdater.Automation;

internal sealed class GalleryHost(MainWindow window, IReadOnlyList<string> pages) : IGalleryHost
{
    private static readonly string[] PageKeys = [SectionKey.Updates, SectionKey.DotNet, SectionKey.Changelog, SectionKey.Logs, SectionKey.Settings, "appearance", "confirmation"];
    private readonly ShellViewModel _shell = window.ViewModel;
    public string AppName => "AI Updater";
    public string AppVersion => _shell.AppVersion;
    public Window Window => window;
    public IReadOnlyList<GalleryCase> Cases { get; } = pages.Select(key => new GalleryCase(key == "confirmation" ? "dialog" : GalleryCase.PageKind, key)).ToArray();

    internal static async Task<int> RunAsync(IEnumerable<string> args)
    {
        var arguments = GalleryArguments.Parse(args, new GalleryDefaults
        {
            Directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "gallery"),
            Themes = [AppThemes.Key],
        }, (requested, unknown) =>
        {
            foreach (var key in requested.Where(key => !PageKeys.Contains(key))) unknown.Add(key);
            var known = requested.Where(PageKeys.Contains).Distinct().ToArray();
            return known.Length == 0 ? PageKeys : known;
        });
        var paths = new AppPaths(Path.Combine(Path.GetFullPath(arguments.Directory), ".state", Guid.NewGuid().ToString("N")), AppContext.BaseDirectory);
        ISettingsStore settings = new SettingsStore(paths.SettingsFile);
        try
        {
            settings.SetValue(SettingsKeys.TranslationSelection, JsonSerializer.Serialize(new TranslationAgentSelection { BackendId = "antigravity", Automatic = false }));
            settings.SetDouble(SettingsKeys.FontScale, arguments.FontScale);
            AppThemes.Initialize(settings);
            if (arguments.Themes.Any(key => ThemeManager.Find(key) is null)) throw new ArgumentException("Указана незарегистрированная тема галереи.");
            using var logging = KeepShellLogging.Bootstrap(new() { LogsDirectory = paths.LogsDirectory, WriteToDebug = false });
            var backend = new GalleryBackend();
            using var services = App.ConfigureServices(settings, logging, paths, collection =>
            {
                collection.AddSingleton<IChangelogContentFetcher>(backend);
                collection.AddSingleton<IUpdaterProcessRunner>(backend);
                collection.AddSingleton<IDotNetPackageService>(backend);
                collection.AddSingleton<IChangelogTranslator, GalleryTranslator>();
            });
            var host = new GalleryHost(services.GetRequiredService<MainWindow>(), arguments.Pages);
            var exitCode = await GalleryRunner.RunAsync(host, arguments);
            settings.Close();
            return exitCode;
        }
        finally { settings.Close(); }
    }

    public Task ArrangeAsync()
    {
        foreach (var stage in _shell.Updates.Stages) stage.SetCheckResult("CURRENT", "1.0.0");
        _shell.Updates.Stages.Single(stage => stage.Id == "Claude Code").SetCheckResult("AVAILABLE", "2.0.0", "2.1.0");
        _shell.Updates.LogPanel.GeneralLogText = "[12:00:00] Проверка инструментов завершена.\n[12:00:01] Доступно обновление Claude Code: 2.0.0 → 2.1.0.\n[12:00:02] Выберите компоненты на странице «Обновления».";
        foreach (var vendor in _shell.Updates.Vendors) _shell.Updates.RefreshVendorSummary(vendor);
        var selected = _shell.Updates.Stages.Single(stage => stage.Id == "Claude Code");
        _shell.Changelog.SelectStage(selected);
        _shell.Changelog.RenderDocument(new ChangelogDocument
        {
            StageId = selected.Id,
            UpdatedAt = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero),
            Releases = [new ChangelogRelease
            {
                Version = "2.1.0", Title = "Claude Code 2.1.0", PublishedAt = "2026-11-11",
                Summary = "Обновления командной строки и исправления стабильности.",
                Groups = [new ChangelogGroup { Category = ChangeCategory.Changed, Title = "Изменения", Items = ["Улучшен запуск задач в CLI.", "Сохранён контекст при переключении рабочих разделов."] }],
            }],
        }, selected);
        _shell.DotNet.ArrangeCatalog(GalleryBackend.CreateCatalogSnapshot());
        return Task.CompletedTask;
    }

    public async Task<GalleryShot> CaptureAsync(GalleryCase item, GalleryContext context)
    {
        MessageDialogViewModel? dialog = null;
        if (item.Name == "confirmation")
        {
            _shell.Navigate(SectionKey.Updates);
            dialog = new("Удаление компонента", "Удалить Claude Code с этого компьютера?", true, "Удалить");
            _ = _shell.Modal.ShowAsync(dialog);
        }
        else if (item.Name == "appearance")
        {
            _shell.Navigate(SectionKey.Settings);
            _shell.SettingsPage.Sections.Restore("appearance");
        }
        else if (item.Name == SectionKey.Settings) _shell.ShowSettings();
        else _shell.Navigate(item.Name);
        try
        {
            await context.SettleAsync();
            return context.Save(item.Name);
        }
        finally { dialog?.CancelCommand.Execute(null); }
    }

    private sealed class GalleryBackend : IChangelogContentFetcher, IUpdaterProcessRunner, IDotNetPackageService
    {
        private const string Notes = "## 2.1.0\n\n### Changed\n- Improved CLI startup";
        public Task<string> FetchAsync(ChangelogSource source, CancellationToken cancellationToken) => Task.FromResult(Notes);
        public Task<(string Content, string? ETag, bool NotModified)> FetchWithETagAsync(ChangelogSource source, string? cachedETag, CancellationToken cancellationToken) => Task.FromResult((Notes, (string?)null, false));
        public Task<string> DownloadUrlAsync(string url, CancellationToken cancellationToken) => Task.FromResult(Notes);
        public Task<string> RunCommandAsync(string executable, IReadOnlyList<string> arguments, int timeoutSeconds, CancellationToken cancellationToken) => Task.FromResult("");
        public string? FindAgyExecutable() => null;
        public Task<int> RunAsync(UpdaterProcessRequest request, Func<string, bool, Task> onOutputChunk, Func<bool, Task> onStreamCompleted, CancellationToken cancellationToken) => Task.FromException<int>(new InvalidOperationException("Запуск обновлений недоступен в галерее."));
        public Task<DotNetCatalogSnapshot> LoadCatalogAsync(bool forceRefresh, bool includePrerelease, CancellationToken cancellationToken) => Task.FromResult(CreateCatalogSnapshot());

        public static DotNetCatalogSnapshot CreateCatalogSnapshot() => new(
        [
            new DotNetChannelInfo(
                "11.0",
                DotNetReleaseType.Sts,
                DotNetSupportPhase.Active,
                "11.0.0",
                "11.0.100",
                "11.0.0",
                "2028-05-14",
                [
                    new DotNetPackageEntry("11.0", DotNetPackageKind.Sdk, [], "11.0.100", "11.0.100", false, []),
                    new DotNetPackageEntry("11.0", DotNetPackageKind.DesktopRuntime, [], "11.0.0", "11.0.0", false, []),
                    new DotNetPackageEntry("11.0", DotNetPackageKind.AspNetCoreRuntime, [], "11.0.0", "11.0.0", false, []),
                    new DotNetPackageEntry("11.0", DotNetPackageKind.Runtime, [], "11.0.0", "11.0.0", false, []),
                ]),
            new DotNetChannelInfo(
                "10.0",
                DotNetReleaseType.Lts,
                DotNetSupportPhase.Active,
                "10.0.5",
                "10.0.202",
                "10.0.5",
                "2028-11-14",
                [
                    new DotNetPackageEntry(
                        "10.0",
                        DotNetPackageKind.Sdk,
                        [new DotNetInstalledInstance("10.0.201", DotNetInstallOwner.MicrosoftInstaller, "Microsoft .NET SDK 10.0.201 (x64)")],
                        "10.0.202",
                        "10.0.202",
                        true,
                        [new DotNetSdkBandPlan("10.0.201", "10.0.202", true)]),
                    new DotNetPackageEntry(
                        "10.0",
                        DotNetPackageKind.DesktopRuntime,
                        [new DotNetInstalledInstance("10.0.5", DotNetInstallOwner.SharedWithSdk)],
                        "10.0.5",
                        "10.0.5",
                        false,
                        []),
                    new DotNetPackageEntry(
                        "10.0",
                        DotNetPackageKind.AspNetCoreRuntime,
                        [new DotNetInstalledInstance("10.0.5", DotNetInstallOwner.SharedWithSdk)],
                        "10.0.5",
                        "10.0.5",
                        false,
                        []),
                    new DotNetPackageEntry(
                        "10.0",
                        DotNetPackageKind.Runtime,
                        [new DotNetInstalledInstance("10.0.5", DotNetInstallOwner.SharedWithSdk)],
                        "10.0.5",
                        "10.0.5",
                        false,
                        []),
                ]),
            new DotNetChannelInfo(
                "8.0",
                DotNetReleaseType.Lts,
                DotNetSupportPhase.Eol,
                "8.0.25",
                "8.0.419",
                "8.0.25",
                "2026-11-10",
                [
                    new DotNetPackageEntry(
                        "8.0",
                        DotNetPackageKind.Sdk,
                        [new DotNetInstalledInstance("8.0.301", DotNetInstallOwner.ExternalOrVisualStudio)],
                        "8.0.419",
                        "8.0.419",
                        true,
                        [new DotNetSdkBandPlan("8.0.301", "8.0.320", true)]),
                ]),
        ],
        new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    }

    private sealed class GalleryTranslator(TranslationAgentSettings settings) : IChangelogTranslator
    {
        public TranslationAgentSettings Settings => settings;
        public string DisplayName => "Галерея";
        public Task<bool> TranslateReleaseAsync(ChangelogRelease release, CancellationToken cancellationToken, bool forceRetranslate = false) => Task.FromResult(false);
    }
}

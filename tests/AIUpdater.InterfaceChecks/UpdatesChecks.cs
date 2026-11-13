using AIUpdater.Bootstrap;
using AIUpdater.Models;
using AIUpdater.Services;
using AIUpdater.ViewModels;
using AIUpdater.Views;
using KeepShell.Bootstrap;
using KeepShell.Testing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AIUpdater.InterfaceChecks;

internal static partial class Program
{
    private static void CheckUpdatesGroupingFilteringAndPrefetch(
        VisualTestHost visualHost,
        MainWindow window,
        ShellViewModel shell,
        string output)
    {
        window.SetCurrentValue(FrameworkElement.WidthProperty, 1200d);
        window.SetCurrentValue(FrameworkElement.HeightProperty, 800d);
        shell.Navigate(SectionKey.Updates);
        visualHost.Settle();
        var updatesView = Descendants<UpdatesView>(window).Single();
        var groupCombo = (ComboBox)updatesView.FindName("GroupModeCombo");
        var sortCombo = (ComboBox)updatesView.FindName("SortModeCombo");
        var hideUninstalledToggle = (CheckBox)updatesView.FindName("HideUninstalledToggle");

        Check(groupCombo.Items.Count == 3 && groupCombo.SelectedItem?.ToString() == "По поставщику", "Default group mode selector is invalid");
        Check(sortCombo.Items.Count == 3 && sortCombo.SelectedItem?.ToString() == "По каталогу", "Default sort mode selector is invalid");
        Check(hideUninstalledToggle.IsChecked == false, "HideUninstalled should be unchecked by default");

        shell.Updates.GroupMode = UpdateGroupMode.ByState;
        visualHost.Settle();
        Check(shell.Updates.ShowVendorSubtitle, "State grouping did not enable vendor subtitles on cards");
        Check(shell.Updates.DisplayGroups.Count >= 3
              && shell.Updates.DisplayGroups[0].Name == "Доступны обновления"
              && shell.Updates.DisplayGroups.Any(group => group.Name == "Не установлены"),
            "State grouping did not bucket available updates first and uninstalled packages separately");
        Render((FrameworkElement)window.Content, Path.Combine(output, "updates-grouped-by-state.png"), 1200, 800);

        shell.Updates.GroupMode = UpdateGroupMode.Ungrouped;
        shell.Updates.SortMode = UpdateSortMode.UpdatesFirst;
        visualHost.Settle();
        Check(shell.Updates.DisplayGroups.Count == 1 && !shell.Updates.DisplayGroups[0].ShowHeader,
            "Ungrouped mode kept multiple groups or visible headers");
        var ungroupedCards = shell.Updates.DisplayGroups[0].Cards;
        Check(ungroupedCards[0].Stage.HasUpdate && ungroupedCards[^1].Stage.PackageStatus == PackageStatus.NotInstalled,
            "UpdatesFirst sort did not place available updates ahead of uninstalled components");

        shell.Updates.SortMode = UpdateSortMode.ByName;
        visualHost.Settle();
        var names = shell.Updates.DisplayGroups[0].Cards.Select(card => card.Stage.Name).ToArray();
        Check(names.SequenceEqual(names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)),
            "ByName sort did not order components alphabetically");

        foreach (var scale in new[] { 1.0, 1.6 })
        {
            window.SetCurrentValue(FrameworkElement.WidthProperty, 1000d);
            window.SetCurrentValue(FrameworkElement.HeightProperty, 700d);
            shell.Preferences.FontScale = scale;
            visualHost.Settle();
            Check(Descendants<ScrollViewer>(updatesView).All(scroll => scroll.ScrollableWidth <= 1),
                $"Updates toolbar/list overflowed horizontally at scale {scale}");
            Check(((ScrollViewer)updatesView.FindName("ComponentsScroll")).ActualHeight >= 250,
                $"Updates viewport collapsed at scale {scale}");
        }

        window.SetCurrentValue(FrameworkElement.WidthProperty, 1200d);
        window.SetCurrentValue(FrameworkElement.HeightProperty, 800d);
        shell.Preferences.FontScale = 1.0;

        hideUninstalledToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        visualHost.Settle();
        Check(shell.Updates.HideUninstalled
              && shell.Updates.DisplayGroups[0].Cards.All(card => card.Stage.PackageStatus != PackageStatus.NotInstalled)
              && shell.Updates.DisplayGroups[0].Cards.Any(card => card.Stage.PackageStatus == PackageStatus.None),
            "HideUninstalled failed to hide NotInstalled cards or hid unchecked (None) cards");

        var activeCodexCardView = Descendants<StageCardView>(updatesView)
            .First(card => ((StageCardViewModel)card.DataContext).Stage.Id == "Active Codex CLI");
        var activeCodexVm = (StageCardViewModel)activeCodexCardView.DataContext;
        activeCodexVm.ToggleDisabledCommand.Execute(null);
        visualHost.Settle();
        var cardBorder = (Border)activeCodexCardView.FindName("CardBorder");
        var cardMenu = cardBorder.ContextMenu!;
        var menuItems = cardMenu.Items.OfType<MenuItem>().ToArray();
        Check(activeCodexVm.Stage.IsDisabled
              && activeCodexVm.Stage.Status == "Отключено"
              && Math.Abs(cardBorder.Opacity - 0.55) < 0.05
              && activeCodexVm.ToggleDisabledHeader == "Включить компонент"
              && menuItems.Where(mi => mi.Header?.ToString() is "Проверить" or "Обновить" or "Установить" or "Удалить").All(mi => !mi.IsEnabled),
            "Disabling a component did not mute its card, update status to Отключено, or disable card actions");
        Render((FrameworkElement)window.Content, Path.Combine(output, "updates-disabled-and-filtered.png"), 1200, 800);

        activeCodexVm.ToggleDisabledCommand.Execute(null);
        hideUninstalledToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        shell.Updates.GroupMode = UpdateGroupMode.ByVendor;
        shell.Updates.SortMode = UpdateSortMode.Catalog;
        visualHost.Settle();
        Check(!activeCodexVm.Stage.IsDisabled && Math.Abs(cardBorder.Opacity - 1.0) < 0.01,
            "Re-enabling a component did not restore active appearance");

        CheckIsolatedPersistenceDispatchAndPrefetch(output);
        Console.WriteLine("PASS: updates grouping, sorting, hide-uninstalled filter, manual component disable/enable, partial vendor dispatch and background changelog prefetch/translation");
    }

    private static void CheckIsolatedPersistenceDispatchAndPrefetch(string output)
    {
        var workDir = Path.Combine(output, "updates-prefetch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var settingsPath = Path.Combine(workDir, "settings.toml");
        var resultsPath = Path.Combine(workDir, "results.json");
        var cacheDir = Path.Combine(workDir, "changelogs");
        var agentSettingsPath = Path.Combine(workDir, "agent-settings.json");

        var runner = new FakeUpdaterProcessRunner();
        var parser = new UpdaterOutputParser();
        var resultStore = new FileStageResultStore(resultsPath);
        var execution = new UpdaterExecutionService(resultStore, runner, parser);
        var scriptFile = Path.Combine(workDir, "Update-Codex.ps1");
        File.WriteAllText(scriptFile, "exit 0");
        execution.ConfigurePaths(scriptPath: scriptFile, logPath: Path.Combine(workDir, "main.log"), resultsPath: resultsPath);

        var claudeUrl = UpdateStage.GetChangelogSource("Claude Code").DownloadUrl!;
        var agyUrl = UpdateStage.GetChangelogSource("Antigravity CLI").DownloadUrl!;
        var dshUrl = UpdateStage.GetChangelogSource("DSH").DownloadUrl!;

        var fetcher = new TrackingChangelogFetcher(new Dictionary<string, (string Content, string ETag)>(StringComparer.OrdinalIgnoreCase)
        {
            [claudeUrl] = ("""
                ## 2.1.0

                ### Added
                - Added background changelog prefetch during version checks.

                ## 2.0.0

                ### Fixed
                - Fixed older CLI session handling.
                """, "\"claude-v210\""),
            [agyUrl] = ("[]", "\"agy-v1\""),
            [dshUrl] = ("{}", "\"dsh-v1\""),
        });

        var agentSettings = new TranslationAgentSettings(agentSettingsPath);
        agentSettings.Save(new TranslationAgentSelection { BackendId = "antigravity", Automatic = true });
        var translator = new GatedTrackingTranslator(agentSettings);
        var repository = new FileChangelogRepository(cacheDir);
        var changelogService = new ChangelogService(fetcher, ChangelogService.CreateDefaultParsers(), repository, translator);
        var dialogs = new SilentUpdaterDialogs();

        ISettingsStore store1 = new SettingsStore(settingsPath);
        using (var vm1 = new MainViewModel(execution, dialogs, store1, changelogService))
        {
            vm1.GroupMode = UpdateGroupMode.ByState;
            vm1.SortMode = UpdateSortMode.UpdatesFirst;
            vm1.HideUninstalled = true;
            vm1.ToggleStageDisabled(vm1.Stages.Single(s => s.Id == "Active Codex CLI"));
            vm1.ToggleStageDisabled(vm1.Stages.Single(s => s.Id == "DSH"));
        }
        store1.Close();

        ISettingsStore store2 = new SettingsStore(settingsPath);
        try
        {
            using var vm = new MainViewModel(execution, dialogs, store2, changelogService);
            using var changelogVm = new ChangelogViewModel(vm, changelogService, dialogs);
            Check(vm.GroupMode == UpdateGroupMode.ByState
                  && vm.SortMode == UpdateSortMode.UpdatesFirst
                  && vm.HideUninstalled
                  && vm.Stages.Single(s => s.Id == "Active Codex CLI").IsDisabled
                  && vm.Stages.Single(s => s.Id == "DSH").IsDisabled,
                "Saved grouping, sorting, hide-uninstalled or disabled stages did not round-trip across sessions");

            foreach (var stage in vm.Stages)
            {
                if (stage.Id is "Claude Code" or "Claude npm package")
                {
                    stage.SetCheckResult("CURRENT", "2.1.0", "2.1.0");
                }
                else if (stage.Id == "DSH")
                {
                    stage.SetCheckResult("CURRENT", "1.0.0", "1.0.0");
                }
                else if (stage.Id == "Codex plugins")
                {
                    stage.InvalidateResult();
                }
                else
                {
                    stage.SetCheckResult("NOT_INSTALLED");
                }
            }

            runner.StageResultOverride = stageId => stageId is "Claude Code" or "Claude npm package" ? "CURRENT" : "NOT_INSTALLED";
            translator.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Await(vm.RunUpdaterAsync(null));
            Check(!vm.IsRunning, "MainViewModel remained busy after version check finished");
            Check(!vm.WaitForChangelogPrefetchAsync().IsCompleted, "Background translation should still be running asynchronously behind the gate");

            Check(runner.Requests.All(req => req.OnlyStageId != "Active Codex CLI" && req.OnlyStageId != "DSH"),
                "CheckAll dispatched a manually disabled component");
            Check(runner.Requests.Any(req => req.Vendor == "OpenAI" && req.OnlyStageId == "Codex plugins"),
                "Unknown (never-checked) component was not checked once when HideUninstalled was active");
            Check(!runner.Requests.Any(req => req.Vendor == "OpenAI" && req.OnlyStageId is null),
                "Vendor with disabled/uninstalled stages ran whole-vendor script instead of targeted -OnlyStage");
            Check(!runner.Requests.Any(req => req.Vendor == "Google"),
                "Vendor whose stages were all known NotInstalled was still dispatched when HideUninstalled was active");

            var codexPlugins = vm.Stages.Single(s => s.Id == "Codex plugins");
            Check(codexPlugins.PackageStatus == PackageStatus.NotInstalled
                  && vm.DisplayGroups.SelectMany(g => g.Cards).All(c => c.Stage != codexPlugins),
                "Newly discovered NotInstalled stage remained visible while HideUninstalled was active");

            runner.Requests.Clear();
            translator.Gate.TrySetResult();
            Await(vm.WaitForChangelogPrefetchAsync());

            Check(fetcher.FetchCalls.GetValueOrDefault(agyUrl) == 0 && fetcher.FetchCalls.GetValueOrDefault(dshUrl) == 0,
                "Background prefetch fetched changelogs for NotInstalled or manually disabled components");
            Check(fetcher.FetchCalls.GetValueOrDefault(claudeUrl) >= 1,
                "Background prefetch did not fetch installed active component changelogs");
            Check(translator.TranslatedVersions.SequenceEqual(["2.1.0"]),
                $"Background auto-translate should translate only top-1 release once across paired stages, got: [{string.Join(", ", translator.TranslatedVersions)}]");

            var cachedClaude = repository.TryLoad("Claude Code");
            var cachedClaudeNpm = repository.TryLoad("Claude npm package");
            Check(cachedClaude is not null
                  && cachedClaude.ETag == "\"claude-v210\""
                  && cachedClaude.Releases[0].HasTranslation
                  && !cachedClaude.Releases[1].HasTranslation
                  && cachedClaudeNpm is not null
                  && cachedClaudeNpm.Releases[0].HasTranslation,
                "Prefetched cache missed ETag, failed to reuse top-1 translation on paired stage, or translated older releases");

            var fetchesBeforeOpen = fetcher.FetchCalls.GetValueOrDefault(claudeUrl);
            changelogVm.SelectedStage = vm.Stages.Single(s => s.Id == "Claude Code");
            Await(Task.Delay(80));
            Check(changelogVm.Releases.Count == 2
                  && changelogVm.LoadStatusText.StartsWith("Из кэша", StringComparison.Ordinal)
                  && fetcher.FetchCalls.GetValueOrDefault(claudeUrl) == fetchesBeforeOpen
                  && translator.TranslatedVersions.Count == 1,
                "Opening Что нового after prefetch triggered extra network fetch or LLM translation instead of immediate cache render");

            cachedClaude!.UpdatedAt = DateTimeOffset.UtcNow.AddHours(-7);
            repository.Save(cachedClaude);
            Await(vm.RunUpdaterAsync(vm.Stages.Single(s => s.Id == "Claude Code")));
            Await(vm.WaitForChangelogPrefetchAsync());
            Check(fetcher.LastConditionalETags.GetValueOrDefault(claudeUrl) == "\"claude-v210\""
                  && translator.TranslatedVersions.Count == 1,
                "Subsequent prefetch did not send If-None-Match ETag or re-translated an unchanged release");
        }
        finally
        {
            store2.Close();
        }
    }

    private sealed class FakeUpdaterProcessRunner : IUpdaterProcessRunner
    {
        public List<UpdaterProcessRequest> Requests { get; } = [];

        public Func<string, string> StageResultOverride { get; set; } = _ => "CURRENT";

        public Task<int> RunAsync(
            UpdaterProcessRequest request,
            Func<string, bool, Task> onOutputChunk,
            Func<bool, Task> onStopCheck,
            CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var stageIds = request.OnlyStageId is not null
                ? [request.OnlyStageId]
                : UpdateStage.CreateCatalog().Where(s => s.Vendor == request.Vendor).Select(s => s.Id).ToArray();

            return EmitResultsAsync(stageIds, onOutputChunk);
        }

        private async Task<int> EmitResultsAsync(string[] stageIds, Func<string, bool, Task> onOutputChunk)
        {
            foreach (var stageId in stageIds)
            {
                var status = StageResultOverride(stageId);
                var installed = status == "NOT_INSTALLED" ? null : "2.1.0";
                var payload = JsonSerializer.Serialize(new { Stage = stageId, Result = status, Installed = installed, Available = installed });
                await onOutputChunk($"[12:00:00] UI_RESULT {payload}\n", false);
            }

            return 0;
        }
    }

    private sealed class TrackingChangelogFetcher(
        IReadOnlyDictionary<string, (string Content, string ETag)> feeds) : IChangelogContentFetcher
    {
        private readonly object _gate = new();

        public Dictionary<string, int> FetchCalls { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> LastConditionalETags { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string> FetchAsync(ChangelogSource source, CancellationToken cancellationToken) =>
            DownloadUrlAsync(source.DownloadUrl ?? "", cancellationToken);

        public Task<(string Content, string? ETag, bool NotModified)> FetchWithETagAsync(
            ChangelogSource source,
            string? cachedETag,
            CancellationToken cancellationToken)
        {
            var url = source.DownloadUrl ?? "";
            lock (_gate)
            {
                FetchCalls[url] = FetchCalls.GetValueOrDefault(url) + 1;
                LastConditionalETags[url] = cachedETag;
            }

            if (!feeds.TryGetValue(url, out var feed))
            {
                throw new System.Net.Http.HttpRequestException($"Unmapped feed URL: {url}");
            }

            if (!string.IsNullOrEmpty(cachedETag) && string.Equals(cachedETag, feed.ETag, StringComparison.Ordinal))
            {
                return Task.FromResult<(string Content, string? ETag, bool NotModified)>((string.Empty, feed.ETag, true));
            }

            return Task.FromResult<(string Content, string? ETag, bool NotModified)>((feed.Content, feed.ETag, false));
        }

        public Task<string> DownloadUrlAsync(string url, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                FetchCalls[url] = FetchCalls.GetValueOrDefault(url) + 1;
            }

            return feeds.TryGetValue(url, out var feed)
                ? Task.FromResult(feed.Content)
                : throw new System.Net.Http.HttpRequestException($"Unmapped download URL: {url}");
        }

        public string? FindAgyExecutable() => null;

        public Task<string> RunCommandAsync(string executable, IReadOnlyList<string> arguments, int timeoutSeconds, CancellationToken cancellationToken) =>
            Task.FromResult("");
    }

    private sealed class GatedTrackingTranslator(TranslationAgentSettings settings) : IChangelogTranslator
    {
        public TranslationAgentSettings Settings => settings;

        public string DisplayName => "Stub Agent";

        public TaskCompletionSource? Gate { get; set; }

        public List<string> TranslatedVersions { get; } = [];

        public async Task<bool> TranslateReleaseAsync(ChangelogRelease release, CancellationToken cancellationToken, bool forceRetranslate = false)
        {
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            lock (TranslatedVersions)
            {
                TranslatedVersions.Add(release.Version);
            }

            release.TranslatedSummary = $"Перевод выпуска {release.Version}.";
            release.TranslatedGroups = release.Groups
                .Select(group => new ChangelogGroup
                {
                    Category = group.Category,
                    Title = ChangelogFormatter.CategoryTitle(group.Category),
                    Summary = "Краткая сводка",
                    Items = group.Items.Select(item => $"Переведено: {item}").ToList(),
                })
                .ToList();
            release.Translation = new TranslationProvenance(
                settings.Current.BackendId,
                settings.Current.Profile.Model,
                settings.Current.Profile.Effort,
                TranslationProvenance.Hash(release),
                1,
                DateTimeOffset.UtcNow);
            return true;
        }
    }

    private sealed class SilentUpdaterDialogs : IUpdaterDialogs
    {
        public void ShowMessage(string message, string title)
        {
        }

        public Task<bool> ConfirmAsync(string message, string title, string acceptText = "Продолжить") =>
            Task.FromResult(true);

        public void OpenChangelog(UpdateStage? stage)
        {
        }

        public void OpenDotNetManager()
        {
        }

        public void OpenExternal(string target, string errorPrefix)
        {
        }
    }
}

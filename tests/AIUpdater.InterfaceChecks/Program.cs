using AIUpdater.Models;
using AIUpdater.Services;
using AIUpdater.ViewModels;
using AIUpdater.Views;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AIUpdater.InterfaceChecks;

file static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Click(ButtonBase button)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (button.Command is { } command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Await(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static async Task AwaitIdle(MainViewModel viewModel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (viewModel.IsRunning)
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static void Render(FrameworkElement view, string path, int width, int height)
    {
        view.Measure(new(width, height));
        view.Arrange(new(0, 0, width, height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle((Brush)Application.Current.FindResource("WindowBrush"), null, new(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CheckStyles(string output)
    {
        foreach (var type in new[] { typeof(Button), typeof(CheckBox), typeof(TextBox), typeof(ToolTip), typeof(ContextMenu), typeof(MenuItem), typeof(Separator), typeof(GridSplitter), typeof(TabControl), typeof(TabItem), typeof(ListBox), typeof(ListBoxItem), typeof(ScrollViewer), typeof(ScrollBar), typeof(ProgressBar) })
        {
            var style = Application.Current.TryFindResource(type) as Style;
            Check(style is not null && style.Setters.OfType<Setter>().Any(setter => setter.Property == Control.TemplateProperty), $"Missing application template: {type.Name}");
        }

        var samples = new StackPanel();
        var boxes = new[]
        {
            new CheckBox { Content = "Не выбрано", IsChecked = false },
            new CheckBox { Content = "Выбрано", IsChecked = true },
            new CheckBox { Content = "Частичный выбор", IsThreeState = true, IsChecked = null },
            new CheckBox { Content = "Недоступно", IsEnabled = false },
            new CheckBox { Content = "Выбрано, недоступно", IsChecked = true, IsEnabled = false },
        };

        foreach (var box in boxes)
        {
            box.Padding = new(8);
            samples.Children.Add(box);
        }

        var text = new TextBox { IsReadOnly = true, Text = "Тёмное текстовое поле с выделением и меню копирования", Padding = new(10), Margin = new(0, 12, 0, 0) };
        samples.Children.Add(text);
        var gallery = new Border { Background = (Brush)Application.Current.FindResource("WindowBrush"), Padding = new(20), Child = samples };
        Render(gallery, Path.Combine(output, "checkbox-states.png"), 560, 300);
        var emptyBorder = (Border)boxes[0].Template.FindName("CheckBorder", boxes[0]);
        var checkedBorder = (Border)boxes[1].Template.FindName("CheckBorder", boxes[1]);
        Check(((SolidColorBrush)emptyBorder.Background).Color == Color.FromRgb(11, 19, 14), "Unchecked checkbox has a light background");
        Check(checkedBorder.Background == Application.Current.FindResource("AccentBrush"), "Checked checkbox has no accent fill");
        Check(((UIElement)boxes[1].Template.FindName("CheckMark", boxes[1])).Visibility == Visibility.Visible, "Checked glyph is missing");
        Check(((UIElement)boxes[2].Template.FindName("MixedMark", boxes[2])).Visibility == Visibility.Visible, "Indeterminate glyph is missing");
        Check(boxes[3].Opacity < boxes[0].Opacity, "Disabled checkbox looks active");
        var toggle = (IToggleProvider)new CheckBoxAutomationPeer(boxes[0]).GetPattern(PatternInterface.Toggle)!;
        toggle.Toggle();
        Check(boxes[0].IsChecked == true, "Custom checkbox broke the standard toggle action");
        text.SelectAll();
        Check(text.ContextMenu is { Items.Count: 2 }, "Text menu fell back to system commands");
        var textMenu = text.ContextMenu!;
        textMenu.PlacementTarget = text;
        textMenu.ApplyTemplate();
        textMenu.Measure(new(260, 120));
        textMenu.Arrange(new(0, 0, 260, 120));
        textMenu.UpdateLayout();
        Check(((MenuItem)textMenu.Items[0]).CommandTarget == text, "Copy command is not bound to the text field");
        Render(textMenu, Path.Combine(output, "text-menu.png"), 260, 90);
        var tooltip = new ToolTip { Content = "Причина последней проверки. Подсказка использует ту же тёмную палитру." };
        Render(tooltip, Path.Combine(output, "tooltip.png"), 350, 70);
        var changelog = new ChangelogWindow([]);
        var list = (ListBox)changelog.FindName("ComponentList");
        list.ItemsSource = new[] { new { Name = "ChatGPT / Codex" }, new { Name = "Claude Code" }, new { Name = "Antigravity CLI" } };
        list.SelectedIndex = 0;
        ((TextBox)changelog.FindName("ChangelogTextBox")).Text = "Пример содержимого окна «Что нового».\nПроверка тёмных рамок, списка, выделения и полосы прокрутки.";
        Render((FrameworkElement)changelog.Content, Path.Combine(output, "changelog.png"), 920, 600);
        Check(Descendants<ListBoxItem>(list).All(item => item.Template is not null), "Changelog rows lost their template");
        Render((FrameworkElement)changelog.Content, Path.Combine(output, "changelog-agent-settings.png"), 1120, 740);
        Check(((Button)changelog.FindName("TranslationSettingsButton")).IsEnabled, "Translation settings button is unavailable");
        Render((FrameworkElement)changelog.Content, Path.Combine(output, "changelog-agent-minimum.png"), 860, 540);
        var settingsButton = (Button)changelog.FindName("TranslationSettingsButton");
        var settingsPosition = settingsButton.TranslatePoint(new(), (UIElement)changelog.Content);
        Check(settingsPosition.X >= 0 && settingsPosition.X + settingsButton.ActualWidth <= 860, "Translation settings are clipped in the narrow changelog window");
        changelog.Close();
        var dialog = AppDialog.Create(null, "Удалить выбранный компонент?", "Удаление компонента", true);
        ((Grid)dialog.Content).Background = dialog.Background;
        Render((FrameworkElement)dialog.Content, Path.Combine(output, "confirmation.png"), 520, 170);
        var dialogButtons = Descendants<Button>((DependencyObject)dialog.Content).ToArray();
        Check(dialogButtons.Single(button => Equals(button.Content, "Отмена")) is { IsDefault: true, IsCancel: true }, "Delete confirmation lost its safe default");
        Check(!dialogButtons.Single(button => Equals(button.Content, "Удалить")).IsDefault, "Delete became the default action");
        dialog.Close();
    }

    private static void CheckIcon(string output)
    {
        using var resource = Application.GetResourceStream(new("pack://application:,,,/AIUpdater;component/Assets/AIUpdater.ico")).Stream;
        var decoder = BitmapDecoder.Create(resource, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        Check(decoder.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(sizes), "ICO is missing native Windows sizes");
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 20, 17)), null, new(0, 0, 620, 300));
            context.DrawRectangle(Brushes.WhiteSmoke, null, new(0, 150, 620, 150));
            var offset = 12;
            foreach (var frame in decoder.Frames.Where(frame => frame.PixelWidth <= 128))
            {
                var pixels = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var buffer = new byte[pixels.PixelWidth * pixels.PixelHeight * 4];
                pixels.CopyPixels(buffer, pixels.PixelWidth * 4, 0);
                Check(buffer[3] == 0, "Icon corners lost transparency");
                Check(buffer.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 200), "ICO frame is empty");
                context.DrawImage(frame, new(offset, 12, frame.PixelWidth, frame.PixelHeight));
                context.DrawImage(frame, new(offset, 162, frame.PixelWidth, frame.PixelHeight));
                offset += frame.PixelWidth + 18;
            }
        }

        var bitmap = new RenderTargetBitmap(620, 300, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, "icon-sizes.png"));
        encoder.Save(stream);
    }

    private static void CheckChangelog(string output, IReadOnlyCollection<UpdateStage> stages)
    {
        using var provider = ServiceCollectionExtensions.CreateDefaultServiceProvider();
        var parsers = provider.GetServices<IChangelogFormatParser>().ToArray();
        Check(parsers.Length == 9, "DI container did not register all 9 IChangelogFormatParser strategies");
        var resolvedMainVm = provider.GetRequiredService<MainViewModel>();
        using var resolvedChangelogVm = provider.GetRequiredService<ChangelogViewModel>();
        Check(resolvedMainVm.Stages.Count == 17 && resolvedChangelogVm.HasStages, "DI container did not resolve MainViewModel and ChangelogViewModel");

        var cacheDir = Path.Combine(output, "changelog-cache-" + Guid.NewGuid().ToString("N"));
        var repository = new FileChangelogRepository(cacheDir);
        var fetcher = provider.GetRequiredService<IChangelogContentFetcher>();
        var translator = new AgentChangelogTranslator();
        var changelogService = new ChangelogService(fetcher, parsers, repository, translator);

        foreach (var item in stages)
        {
            var source = item.GetChangelogSource();
            Check(source.DownloadUrl is not null && source.Format != "none" && !string.IsNullOrWhiteSpace(source.Note),
                $"Stage '{item.Id}' is missing a real changelog source");
        }

        var htmlSample = """
            <main>
              <h1>ChatGPT &amp; Codex changelog</h1>
              <h2>November 2026</h2>
              <p>2026-11-06</p>
              <h2>1.21.6 <span>Latest</span></h2>
              <p>Google Antigravity IDE release notes and new capabilities for workspace agents.</p>
              <details><summary>Fixes (13)</summary><ul><li>Fixed crash when opening workspace</li></ul></details>
            </main>
            """;
        var htmlParsed = changelogService.ParseReleases(htmlSample, "html");
        Check(htmlParsed.Count == 1
              && htmlParsed[0].Version == "1.21.6"
              && !htmlParsed[0].Title.Contains("Latest", StringComparison.OrdinalIgnoreCase)
              && htmlParsed[0].Groups.Any(g => g.Category == ChangeCategory.Fixed && g.Items.Contains("Fixed crash when opening workspace"))
              && !htmlParsed[0].Groups.SelectMany(g => g.Items).Any(i => i.Contains("Es (13)", StringComparison.OrdinalIgnoreCase) || i.Contains("Fixes (13)", StringComparison.OrdinalIgnoreCase)),
            "HTML changelog parser failed to skip page header, strip Latest badge, or classify <summary>Fixes (13)</summary>");

        var codexAppHtml = """
            <ul>
              <li data-codex-topics="codex-cli">
                <time>2026-10-08</time>
                <h3>Codex CLI 0.167.0</h3>
                <article>
                  <h2>New Features</h2>
                  <ul><li>Open the agent command center with <code>codex agents --no-daemon</code>.</li></ul>
                  <h2>Bug Fixes</h2>
                  <ul><li>Fixed CLI crash on startup.</li></ul>
                </article>
              </li>
              <li data-codex-topics="general">
                <time>2026-10-26</time>
                <h3>GPT-6.1 Terra in Work and Codex</h3>
                <article>
                  <p>GPT-6.1 Terra is now available across ChatGPT Work and
                  the Codex desktop application.</p>
                </article>
              </li>
              <li data-codex-topics="codex-app">
                <time>2026-10-08</time>
                <h3>Codex app 26.1007</h3>
                <article>
                  <h2>New features</h2>
                  <ul><li>Add, preview, and remove attachments without leaving the full-screen composer.</li></ul>
                  <h2>Improvements and bug fixes</h2>
                  <ul><li>Fixed window focus issue when switching
                  threads in ChatGPT Desktop.</li></ul>
                </article>
              </li>
            </ul>
            """;
        var codexAppParsed = changelogService.ParseReleases(codexAppHtml, "codex-app");
        Check(codexAppParsed.Count == 2
              && codexAppParsed[0].PublishedAt == "2026-10-26"
              && codexAppParsed[0].Title.Contains("GPT-6.1 Terra", StringComparison.Ordinal)
              && codexAppParsed[1].Version == "26.1007"
              && codexAppParsed[1].Title.Contains("Codex app 26.1007", StringComparison.Ordinal)
              && codexAppParsed[1].Groups.Count == 2
              && codexAppParsed[1].Groups.Any(g => g.Category == ChangeCategory.Added)
              && codexAppParsed[1].Groups.Any(g => g.Category == ChangeCategory.Fixed && g.Items.Contains("Fixed window focus issue when switching threads in ChatGPT Desktop."))
              && codexAppParsed[1].Summary is null,
            "codex-app parser failed to include general topics, preserve wrapped HTML sentences, filter out codex-cli, or split New features / Bug fixes");
        Check(ChangelogFormatter.VersionsMatch("26.1007", "26.1007.7773.0") && !ChangelogFormatter.VersionsMatch("26.1007", "26.1103.7773.0"),
            "CalVer MSIX 4-part version matching failed");

        var antigravityHubHtml = """
            <section id="panel-hub">
              <article class="rn-row release-entry" id="rel-hub-2.31.0">
                <time class="rn-date-text">November 9, 2026</time>
                <div class="rn-card">
                  <h3 class="rn-headline">/swarm and split-view plugins</h3>
                  <div class="rn-summary"><p>Adds multi-agent swarm support and split view.</p></div>
                  <div class="rn-categories">
                    <details><summary>Improvements (1)</summary><ul><li>Added /swarm command support.</li></ul></details>
                    <details><summary>Fixes (1)</summary><ul><li>Fixed browser download prompt.</li></ul></details>
                  </div>
                </div>
              </article>
            </section>
            <section id="panel-cli">
              <article class="rn-row release-entry" id="rel-cli-1.5.5">
                <time class="rn-date-text">November 10, 2026</time>
                <div class="rn-card"><h3 class="rn-headline">CLI 1.5.5</h3><details><summary>Fixes (1)</summary><ul><li>CLI only fix.</li></ul></details></div>
              </article>
            </section>
            """;
        var agDesktopParsed = changelogService.ParseReleases(antigravityHubHtml, "antigravity-desktop");
        Check(agDesktopParsed.Count == 1
              && agDesktopParsed[0].Version == "2.31.0"
              && agDesktopParsed[0].PublishedAt == "2026-11-09"
              && agDesktopParsed[0].Summary == "Adds multi-agent swarm support and split view."
              && agDesktopParsed[0].Groups.Count == 2,
            "antigravity-desktop parser failed to isolate panel-hub articles from CLI entries or parse date/summary");

        var nodeMd = """
            ## 2026-10-28, Version 22.22.2 'Jod' (LTS)
            ### Notable Changes
            * \[[`fe2a6b2be8`](https://github.com/nodejs/node/commit/fe2a6b2be8)] - **crypto**: add support for [CVE-2026-99999](https://cve.org/CVE-2026-99999) mitigation
            """;
        var nodeParsed = changelogService.ParseReleases(nodeMd, "markdown");
        Check(nodeParsed.Count == 1
              && nodeParsed[0].Groups[0].Items[0].Contains("`fe2a6b2be8` – crypto: add support for CVE-2026-99999 mitigation", StringComparison.Ordinal),
            "Markdown changelog parser did not clean escaped commit brackets or inline URLs");

        var agyParsed = changelogService.ParseReleases("1.5.2:\n  · Added: Headless execution mode\n  · Fixed: EBADENGINE crash on stderr\n1.5.1:\n  · Improved: Faster startup", "agy");
        Check(agyParsed.Count == 2 && agyParsed[0].Version == "1.5.2" && agyParsed[0].Groups.Count == 2, "agy changelog parser failed to group releases");

        var dshJson = """
            {
              "dist-tags": { "latest": "0.2.2-rc.1" },
              "time": { "0.2.1-rc.5": "2026-10-01T10:00:00Z", "0.2.2-rc.1": "2026-11-05T12:00:00Z" },
              "versions": {
                "0.2.1-rc.5": { "engines": { "node": ">=22.0.0" }, "dependencies": { "@deepseek-ai/dsh-win32-x64": "0.2.1-rc.5" } },
                "0.2.2-rc.1": { "engines": { "node": ">=24.9.0" }, "dependencies": { "@deepseek-ai/dsh-win32-x64": "0.2.2-rc.1", "@deepseek-ai/dsh-mcp": "0.2.2-rc.1" } }
              }
            }
            """;
        var dshParsed = changelogService.ParseReleases(dshJson, "npm-registry");
        Check(dshParsed.Count == 2 && dshParsed[0].Version == "0.2.2-rc.1"
              && dshParsed[0].Groups.Any(g => g.Category == ChangeCategory.Added)
              && dshParsed[0].Groups.Any(g => g.Category == ChangeCategory.Changed && g.Items.Any(i => i.Contains(">=24.9.0", StringComparison.Ordinal))),
            "DSH npm registry parser failed to extract version diff and Node engine requirement");

        var dotnetJson = """
            {
              "releases": [
                {
                  "release-version": "10.0.5",
                  "release-date": "2026-10-13",
                  "security": true,
                  "sdk": { "version": "10.0.303" },
                  "sdks": [ { "version": "10.0.303" }, { "version": "10.0.114" } ],
                  "runtime": { "version": "10.0.5" },
                  "cve-list": [ { "cve-id": "CVE-2026-0001" } ]
                }
              ]
            }
            """;
        var dotnetParsed = changelogService.ParseReleases(dotnetJson, "dotnet-releases");
        Check(dotnetParsed.Count == 1 && dotnetParsed[0].Groups.Any(g => g.Category == ChangeCategory.Security), ".NET releases parser missed SDK or CVE details");

        var atomXml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>tag:github.com,2008:Repository/1/v2.48.1</id>
                <updated>2026-10-15T10:00:00Z</updated>
                <title>v2.48.1</title>
                <content>&lt;ul&gt;&lt;li&gt;Added: SSH agent support for &lt;code&gt;git fetch&lt;/code&gt;&lt;/li&gt;&lt;/ul&gt;</content>
              </entry>
            </feed>
            """;
        var atomParsed = changelogService.ParseReleases(atomXml, "atom");
        Check(atomParsed.Count == 1 && atomParsed[0].Version == "2.48.1" && atomParsed[0].Groups[0].Category == ChangeCategory.Added,
            "Atom changelog parser failed to parse Atom feed entry");

        var ghJson = """[{"tag_name":"v7.5.4","name":"v7.5.4 Release","published_at":"2026-10-20T08:00:00Z","body":"### Bug Fixes\n- Fixed startup race condition"}]""";
        var ghParsed = changelogService.ParseReleases(ghJson, "github-releases");
        Check(ghParsed.Count == 1 && ghParsed[0].Version == "7.5.4" && ghParsed[0].Groups[0].Category == ChangeCategory.Fixed,
            "GitHub releases parser failed to parse github-releases JSON");
        Check(changelogService.ParseReleases("   ", "markdown").Count == 0, "Empty input returned non-empty releases");

        var cachedRelease = new ChangelogRelease
        {
            Version = agyParsed[1].Version,
            Title = agyParsed[1].Title,
            Groups = agyParsed[1].Groups,
            TranslatedTitle = "Antigravity CLI v1.5.1",
            TranslatedGroups = [new() { Category = ChangeCategory.Changed, Title = "Изменено и улучшено", Items = ["Ускорен запуск CLI"] }],
        };
        var merged = changelogService.MergeReleases(
            changelogService.ParseReleases("1.5.2:\n  · Added: Headless execution mode\n  · Fixed: EBADENGINE crash on stderr\n1.5.1:\n  · Improved: Faster startup", "agy"),
            [cachedRelease]);
        Check(merged.Count == 2 && merged[1].HasTranslation && merged[1].TranslatedGroups![0].Items[0] == "Ускорен запуск CLI",
            "Delta merge discarded cached translation of an existing version");

        var doc = new ChangelogDocument
        {
            StageId = "Antigravity CLI",
            UpdatedAt = DateTimeOffset.UtcNow,
            Releases = agyParsed,
        };
        changelogService.SaveCachedDocument(doc);
        var loadedDoc = changelogService.TryLoadCachedDocument("Antigravity CLI");
        Check(loadedDoc is not null && loadedDoc.Releases.Count == 2 && !loadedDoc.Releases[0].HasTranslation && !loadedDoc.Releases[1].HasTranslation,
            "Changelog disk cache round-trip failed");

        translator.PromptRunner = (_, _) => Task.FromResult<string?>(
            "[Summary]\nПоддержка фонового режима без окон и исправление обработки предупреждений npm в stderr.\n[Added]\nSummary: Фоновый режим выполнения CLI без всплывающих окон.\n- Режим выполнения без консольного окна\n[Fixed]\nSummary: Устранено ложное падение на предупреждениях EBADENGINE.\n- Исправлено падение на предупреждениях `EBADENGINE` в stderr");
        var translated = AwaitResult(changelogService.TranslateReleaseAsync(loadedDoc!, loadedDoc!.Releases[0], CancellationToken.None));
        Check(translated
              && loadedDoc!.Releases[0].HasTranslation
              && loadedDoc.Releases[0].TranslatedGroups!.Count == 2
              && !string.IsNullOrWhiteSpace(loadedDoc.Releases[0].TranslatedSummary)
              && loadedDoc.Releases[0].TranslatedGroups!.All(g => !string.IsNullOrWhiteSpace(g.Summary)),
            "Agent translation did not populate translated groups or summaries on the target release");
        var reloadedAfterTranslation = changelogService.TryLoadCachedDocument("Antigravity CLI");
        Check(reloadedAfterTranslation is not null && reloadedAfterTranslation.Releases[0].HasTranslation && !reloadedAfterTranslation.Releases[1].HasTranslation,
            "Translated release was not persisted to disk cache");

        var agyStage = stages.First(s => s.Id == "Antigravity CLI");
        var changelogWindow = new ChangelogWindow(stages, agyStage, changelogService);
        var changelogVm = changelogWindow.ViewModel;
        var componentList = (ListBox)changelogWindow.FindName("ComponentList");
        Check(componentList.Items.Groups?.Count == 5, "ChangelogWindow did not group components by Vendor into 5 groups");
        var searchBox = (TextBox)changelogWindow.FindName("ComponentSearchBox");
        searchBox.Text = "DeepSeek";
        Check(componentList.Items.Count == 1, "ComponentSearchBox filter did not narrow component list to DeepSeek");
        searchBox.Text = "";
        Check(componentList.Items.Count == stages.Count, "Clearing ComponentSearchBox did not restore full component list");
        var installedOnlyCheckBox = (CheckBox)changelogWindow.FindName("InstalledOnlyCheckBox");
        installedOnlyCheckBox.IsChecked = true;
        Check(componentList.Items.Count > 0 && componentList.Items.Count < stages.Count,
            "InstalledOnlyCheckBox did not filter out uninstalled components");
        installedOnlyCheckBox.IsChecked = false;
        Check(componentList.Items.Count == stages.Count,
            "Unchecking InstalledOnlyCheckBox did not restore all components");

        changelogVm.RenderDocument(reloadedAfterTranslation!, agyStage);
        var cardsPanel = (ItemsControl)changelogWindow.FindName("ReleaseCardsPanel");
        Check(cardsPanel.Items.Count == 2 && changelogVm.Releases.Count == 2,
            "ChangelogWindow did not render structured release cards");
        Render((FrameworkElement)changelogWindow.Content, Path.Combine(output, "changelog-cards.png"), 1120, 740);
        Check(Descendants<Border>(cardsPanel).Any(border => Equals(border.Tag, "1.5.2")),
            "ChangelogWindow DataTemplate did not materialize release card borders");
        Check(Descendants<Infrastructure.FormattedTextBlock>(cardsPanel).Any(block => block.Inlines.Count > 1),
            "FormattedTextBlock did not split inline backtick code spans into runs");

        Check(Descendants<ReleaseCardView>(cardsPanel).Count() == 2 && Descendants<ReleaseGroupView>(cardsPanel).Any(),
            "ChangelogWindow did not materialize ReleaseCardView and ReleaseGroupView controls");

        var firstCardVm = changelogVm.Releases[0];
        var firstGroupVm = firstCardVm.Groups[0];
        Check(!firstGroupVm.IsCollapsed && firstGroupVm.IsExpanded && firstGroupVm.ChevronText == "▾", "Group should start expanded");
        firstGroupVm.ToggleCollapseCommand.Execute(null);
        Check(firstGroupVm.IsCollapsed && !firstGroupVm.IsExpanded && firstGroupVm.ChevronText == "▸",
            "ReleaseGroupViewModel ToggleCollapseCommand did not collapse group");
        firstGroupVm.ToggleCollapseCommand.Execute(null);
        Check(!firstGroupVm.IsCollapsed && firstGroupVm.IsExpanded && firstGroupVm.ChevronText == "▾", "ReleaseGroupViewModel ToggleCollapseCommand did not re-expand group");

        firstCardVm.ToggleCollapseCommand.Execute(null);
        Check(firstCardVm.IsCollapsed && !firstCardVm.IsExpanded && firstCardVm.ExpandButtonText.StartsWith("Развернуть", StringComparison.Ordinal),
            "ReleaseCardViewModel ToggleCollapseCommand did not collapse release card");
        firstCardVm.ToggleCollapseCommand.Execute(null);
        Check(!firstCardVm.IsCollapsed && firstCardVm.IsExpanded && firstCardVm.ExpandButtonText == "Свернуть",
            "ReleaseCardViewModel ToggleCollapseCommand did not re-expand release card");

        firstCardVm.ToggleCardLanguageCommand.Execute(null);
        Check(changelogVm.Releases[0].CardLanguageButtonText == "Русский (ИИ)",
            "Per-card language toggle did not switch card to original language");
        changelogVm.Releases[0].ToggleCardLanguageCommand.Execute(null);
        Check(changelogVm.Releases[0].CardLanguageButtonText == "Оригинал",
            "Per-card language toggle did not restore Russian AI overview");

        var secondReleaseProcessed = AwaitResult(changelogVm.TranslateReleaseOnDemandAsync(reloadedAfterTranslation!.Releases[1], true));
        var reprocessedAgain = AwaitResult(changelogVm.TranslateReleaseOnDemandAsync(reloadedAfterTranslation.Releases[0], true));
        Check(secondReleaseProcessed && reprocessedAgain && reloadedAfterTranslation.Releases[1].HasTranslation,
            "Repeated on-demand release processing failed or threw ObjectDisposedException");

        var dshStage = stages.First(s => s.Id == "DSH");
        dshStage.SetCheckResult("CURRENT", "0.2.2-rc.1", "0.2.2-rc.1");
        changelogService.SaveCachedDocument(new ChangelogDocument
        {
            StageId = "DSH",
            UpdatedAt = DateTimeOffset.UtcNow,
            Releases = dshParsed,
        });
        translator.PromptRunner = async (_, ct) =>
        {
            await Task.Delay(500, ct);
            return "[Summary]\nТест";
        };
        var inFlightTranslation = changelogVm.TranslateReleaseOnDemandAsync(reloadedAfterTranslation.Releases[0], true);
        changelogVm.SelectStage(dshStage);
        var canceledResult = AwaitResult(inFlightTranslation);
        Check(!canceledResult && ReferenceEquals(changelogVm.SelectedStage, dshStage) && changelogVm.Releases.Any(r => r.ReleaseKey == "0.2.2-rc.1"),
            "Stage switch during in-flight translation hijacked SelectedStage or document back to previous stage");
        changelogVm.RenderDocument(reloadedAfterTranslation, agyStage);

        var langBtn = (Button)changelogWindow.FindName("LanguageToggleButton");
        Check(langBtn.Content.ToString()!.Contains("Русский", StringComparison.Ordinal), "Language button did not show Russian AI state");
        Click(langBtn);
        Check(langBtn.Content.ToString()!.Contains("Оригинал", StringComparison.Ordinal), "Language toggle did not switch to original");
        var zoomInBtn = (Button)changelogWindow.FindName("ZoomInButton");
        var zoomResetBtn = (Button)changelogWindow.FindName("ZoomResetButton");
        Click(zoomInBtn);
        Check(zoomResetBtn.Content.ToString() != "100%", "Changelog zoom in did not update zoom percentage");
        Click(zoomResetBtn);
        Check(zoomResetBtn.Content.ToString() == "100%", "Changelog zoom reset did not restore 100%");

        var sourceBtn = (Button)changelogWindow.FindName("SourceButton");
        var changelogRoot = (FrameworkElement)changelogWindow.Content;
        changelogRoot.Measure(new(1120, 740));
        changelogRoot.Arrange(new(0, 0, 1120, 740));
        changelogRoot.UpdateLayout();
        Check(sourceBtn.TranslatePoint(new(0, 0), changelogRoot).Y < 60, "SourceButton was not relocated to the ChangelogWindow header");
        Check(!Descendants<Button>(changelogRoot).Any(b => Equals(b.Content, "Закрыть")), "Redundant Close button still exists in ChangelogWindow");

        var stubFetcher = new StubChangelogContentFetcher(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://api.github.com/repos/openai/codex/releases?per_page=60"] = """
                [
                  {"tag_name":"rust-v0.169.0-alpha.9","name":"Release 0.169.0-alpha.9","prerelease":true,"html_url":"https://github.com/openai/codex/releases/tag/rust-v0.169.0-alpha.9","published_at":"2026-11-10T10:00:00Z","body":"Release 0.169.0-alpha.9"},
                  {"tag_name":"rust-v0.169.0-alpha.10","name":"Release 0.169.0-alpha.10","prerelease":true,"html_url":"https://github.com/openai/codex/releases/tag/rust-v0.169.0-alpha.10","published_at":"2026-11-10T11:00:00Z","body":"Release 0.169.0-alpha.10"},
                  {"tag_name":"libcap-v2.75","name":"libcap 2.75","prerelease":true,"published_at":"2026-11-09T10:00:00Z","body":"Dependency"},
                  {"tag_name":"rust-v0.167.1","name":"0.167.1","prerelease":false,"html_url":"https://github.com/openai/codex/releases/tag/rust-v0.167.1","published_at":"2026-11-10T09:00:00Z","body":"## Changelog\nFull Changelog: https://github.com/openai/codex/compare/rust-v0.167.0...rust-v0.167.1"},
                  {"tag_name":"rust-v0.167.0","name":"0.167.0","prerelease":false,"html_url":"https://github.com/openai/codex/releases/tag/rust-v0.167.0","published_at":"2026-11-06T09:00:00Z","body":"### New Features\n- Added multi-agent worktree support"}
                ]
                """,
            ["https://api.github.com/repos/openai/codex/compare/rust-v0.167.0...rust-v0.167.1"] = """
                {"commits":[{"commit":{"message":"fix: restrict legacy incremental tools (#60040)"}},{"commit":{"message":"feat: add opt-in incremental tools for Responses (#59832)"}}]}
                """,
            ["https://api.github.com/repos/openai/codex/compare/rust-v0.169.0-alpha.9...rust-v0.169.0-alpha.10"] = """
                {"commits":[{"commit":{"message":"feat(tui): stream subagent progress in status bar"}}]}
                """,
            ["https://api.github.com/repos/openai/codex/compare/rust-v0.167.1...rust-v0.169.0-alpha.9"] = """
                {"commits":[{"commit":{"message":"fix(core): avoid duplicate tool retries"}}]}
                """,
            ["https://developers.openai.com/codex/changelog/"] = codexAppHtml,
            ["https://api.github.com/repos/google-antigravity/antigravity-cli/releases?per_page=15"] = """
                [
                  {"tag_name":"v1.5.5","name":"1.5.5","prerelease":false,"published_at":"2026-11-10T12:00:00Z","body":"### Fixes\n- Fixed MCP reconnect handling"},
                  {"tag_name":"v1.5.4","name":"1.5.4","prerelease":false,"published_at":"2026-11-09T12:00:00Z","body":"### Features\n- Added headless JSON output"}
                ]
                """,
        });
        var stubService = new ChangelogService(stubFetcher, parsers, new FileChangelogRepository(Path.Combine(output, "stub-cache")), new AgentChangelogTranslator());

        var isolatedStages = UpdateStage.CreateCatalog();
        var codexCliStage = isolatedStages.First(s => s.Id == "Active Codex CLI");
        codexCliStage.SetCheckResult("CURRENT", "0.167.1", "0.167.1");
        var (codexDoc, _) = AwaitResult(stubService.LoadDocumentAsync(codexCliStage, true, CancellationToken.None));
        Check(!codexDoc.Releases.Any(r => r.Version.Contains("2.75", StringComparison.Ordinal)),
            "Non-CLI dependency tag libcap-v2.75 leaked into Codex CLI releases");
        var patch01671 = codexDoc.Releases.Single(r => r.Version == "0.167.1");
        Check(!patch01671.Groups.SelectMany(g => g.Items).Any(i => i.StartsWith("Опубликован выпуск", StringComparison.Ordinal))
              && patch01671.Groups.Any(g => g.Category == ChangeCategory.Added),
            "Compare commit enrichment did not replace placeholder text on 0.167.1");

        changelogVm.RenderDocument(codexDoc, codexCliStage);
        var channelBtn = (Button)changelogWindow.FindName("ChannelToggleButton");
        changelogRoot.UpdateLayout();
        Check(changelogVm.HasChannelSwitch
              && channelBtn.Visibility == Visibility.Visible
              && changelogVm.SelectedChannel == ReleaseChannel.Stable
              && changelogVm.Releases.Count == 2
              && changelogVm.Releases[0].ReleaseKey == "0.167.1",
            "Stable-installed Codex CLI did not default to Stable channel or mixed alpha releases");
        Click(channelBtn);
        Check(changelogVm.SelectedChannel == ReleaseChannel.Prerelease
              && changelogVm.Releases.Count == 2
              && changelogVm.Releases[0].ReleaseKey == "0.169.0-alpha.10"
              && changelogVm.Releases[1].ReleaseKey == "0.169.0-alpha.9"
              && !changelogVm.Releases[0].Groups.SelectMany(g => g.Items).Any(i => i.StartsWith("Опубликован выпуск", StringComparison.Ordinal)),
            "Switching to Prerelease channel did not order alpha.10 before alpha.9 or missed compare commit descriptions");

        var codexNpmStage = isolatedStages.First(s => s.Id == "Codex npm package");
        codexNpmStage.SetCheckResult("CURRENT", "0.169.0-alpha.10", "0.169.0-alpha.10");
        changelogVm.RenderDocument(codexDoc, codexNpmStage);
        Check(changelogVm.SelectedChannel == ReleaseChannel.Prerelease,
            "Prerelease-installed stage did not auto-select Prerelease channel");

        var codexDesktopStage = isolatedStages.First(s => s.Id == "Codex desktop");
        codexDesktopStage.SetCheckResult("AVAILABLE", "26.1103.7773.0", info: "Microsoft Store не передаёт номер новой версии до установки");
        var (codexDesktopDoc, _) = AwaitResult(stubService.LoadDocumentAsync(codexDesktopStage, true, CancellationToken.None));
        changelogVm.RenderDocument(codexDesktopDoc, codexDesktopStage);
        Check(codexDesktopDoc.Releases[0].Version == "26.1103.7773.0"
              && codexDesktopDoc.Releases[0].PublishedAt == "2026-11-03"
              && changelogVm.Releases[0].Badges.Any(b => b.Text == "Установлено")
              && changelogVm.Releases[0].Badges.Any(b => b.Text == "Есть обновление"),
            "Codex desktop did not prepend installed/available Microsoft Store build card");

        var isolatedAgyStage = isolatedStages.First(s => s.Id == "Antigravity CLI");
        isolatedAgyStage.SetCheckResult("CURRENT", "1.5.5", "1.5.5");
        var (agyGhDoc, _) = AwaitResult(stubService.LoadDocumentAsync(isolatedAgyStage, true, CancellationToken.None));
        Check(agyGhDoc.Releases.Count == 2
              && agyGhDoc.Releases[0].Version == "1.5.5"
              && agyGhDoc.Releases[0].Title == "Antigravity CLI v1.5.5"
              && !agyGhDoc.Releases.Any(r => r.Version.StartsWith("2.", StringComparison.Ordinal)),
            "Antigravity CLI did not load 1.5.5 from GitHub releases or mixed Desktop 2.x entries");

        changelogWindow.Close();
    }

    private static T AwaitResult<T>(Task<T> task)
    {
        Await(task);
        return task.GetAwaiter().GetResult();
    }

    private static void CheckTranslationAgentSettings(string output)
    {
        var settings = new TranslationAgentSettings(Path.Combine(output, "agent-settings-ui.json"));
        settings.Save(new());
        var viewModel = new TranslationAgentViewModel(settings);
        var dialog = new TranslationAgentWindow(viewModel);
        var root = (FrameworkElement)dialog.Content;
        Render(root, Path.Combine(output, "translation-settings.png"), 660, 580);
        var agents = (ComboBox)dialog.FindName("TranslationAgentComboBox");
        var model = (ComboBox)dialog.FindName("TranslationModelComboBox");
        var effort = (ComboBox)dialog.FindName("TranslationEffortComboBox");
        var executable = (TextBox)dialog.FindName("TranslationExecutablePathBox");
        var automatic = (CheckBox)dialog.FindName("AutomaticTranslationCheckBox");
        var editor = (TextBox)model.Template.FindName("PART_EditableTextBox", model);
        Check(agents.Items.Count == 4 && viewModel.Backends.Any(agent => agent.Name == "Claude Code"), "Agent names are missing");
        Check(agents.SelectionBoxItem.ToString() == "Antigravity", "Agent selector shows a technical record instead of its name");
        Check(editor.Text == "gemini-3.8-flash-low", "Saved model is not visible in the input");
        Check(effort.Text == "Низкий", "Effort is not shown with a readable title");
        editor.Text = "custom-gemini-model";
        Check(viewModel.Model == "custom-gemini-model", "Typing in the model input does not update the profile");
        agents.SelectedValue = "codex";
        editor.Text = "custom-codex-model";
        effort.SelectedValue = "high";
        executable.Text = @"C:\Agent Tools\codex.cmd";
        automatic.IsChecked = false;
        Check(viewModel.BackendId == "codex" && viewModel.Effort == "high", "Agent or effort selection did not update");
        Check(settings.Current.BackendId == "antigravity", "Editing applied settings before Save");
        agents.SelectedValue = "antigravity";
        Check(editor.Text == "custom-gemini-model", "Switching agents discarded an unsaved model");
        agents.SelectedValue = "codex";
        Check(editor.Text == "custom-codex-model" && executable.Text == @"C:\Agent Tools\codex.cmd", "Agent draft did not round-trip");
        viewModel.SaveCommand.Execute(null);
        var loaded = new TranslationAgentSettings(settings.FilePath).Current;
        Check(loaded.BackendId == "codex" && !loaded.Automatic && loaded.Profile.Effort == "high", "Save lost agent, effort or automatic mode");
        Check(loaded.Profiles["antigravity"].Model == "custom-gemini-model", "Save lost the other agent draft");
        Check(viewModel.SavedDescription.Contains("Codex") && viewModel.SavedDescription.Contains("custom-codex-model"), "Current agent summary is stale");
        editor.SelectAll();
        Render(root, Path.Combine(output, "translation-settings-selection.png"), 600, 520);
        Check(model.ActualWidth >= 490 && editor.ActualHeight >= 20, "Model input is cramped at the minimum window size");
        Check(editor.SelectionTextBrush == Application.Current.FindResource("TextBrush"), "Selected model text has unreadable colors");
        foreach (var control in new FrameworkElement[] { agents, model, effort, executable, (Button)dialog.FindName("SaveTranslationSettingsButton") })
        {
            var position = control.TranslatePoint(new(), root);
            Check(position.X >= 0 && position.X + control.ActualWidth <= root.ActualWidth + 1 && position.Y + control.ActualHeight <= root.ActualHeight + 1,
                "Agent settings control is clipped at the minimum size");
        }
        executable.Text = Path.Combine(output, "missing-cli", "codex.exe");
        Await(viewModel.DetectCommand.ExecuteAsync(null));
        Check(viewModel.Status.StartsWith("Файл не найден") && viewModel.Model == "custom-codex-model", "Missing CLI did not produce a useful status or erased the model");
        var fakeCliDirectory = Path.Combine(output, "fake-cli");
        Directory.CreateDirectory(fakeCliDirectory);
        var fakeCli = Path.Combine(fakeCliDirectory, "codex.cmd");
        File.WriteAllText(fakeCli, "@echo codex 9.8.7\r\n");
        executable.Text = fakeCli;
        Await(viewModel.DetectCommand.ExecuteAsync(null));
        Check(viewModel.DetectedPath == fakeCli && viewModel.Status.Contains("9.8.7") && editor.Text == "custom-codex-model", "CLI detection lost its path, version or manually entered model");
        editor.Text = "discarded-model";
        dialog.Close();
        Check(viewModel.Model == "custom-codex-model" && settings.Current.Profile.Model == "custom-codex-model", "Cancel did not restore saved settings");
        Console.WriteLine("PASS: translation settings input, profile drafts, effort labels, save, cancel, minimum layout");
    }

    private static void CheckAgentTranslation(string output)
    {
        var settings = new TranslationAgentSettings(Path.Combine(output, "agent-profiles.json"));
        settings.Save(new() { Automatic = false, BackendId = "claude-code", Profiles = new() { ["claude-code"] = new() { Model = "test-model" } } });
        var translator = new AgentChangelogTranslator(settings);
        var release = new ChangelogRelease { Version = "1", Title = "Original", Groups = [new() { Category = ChangeCategory.Fixed, Items = ["Fixed startup"] }] };
        const string valid = "[Summary]\nИсправлен запуск.\n[Fixed]\nSummary: Исправлен запуск.\n- Исправлен запуск";
        translator.PromptRunner = (_, _) => Task.FromResult<string?>(valid);
        Check(AwaitResult(translator.TranslateReleaseAsync(release, CancellationToken.None)), "Selected backend translation failed");
        Check(release.Translation?.BackendId == "claude-code" && release.Translation.RequestedModel == "test-model", "Translation provenance missing");
        var manyItems = new ChangelogRelease { Version = "large", Groups = [new() { Category = ChangeCategory.Fixed, Items = Enumerable.Range(0, 300).Select(index => index + new string('x', 200)).ToList() }] };
        var calls = 0;
        translator.PromptRunner = (prompt, _) => { calls++; Check(prompt.Length < 45_000, "Release was not split into bounded requests"); return Task.FromResult<string?>(valid); };
        Check(AwaitResult(translator.TranslateReleaseAsync(manyItems, CancellationToken.None)) && calls == 3, "Large release was not translated in chunks plus combined summary");
        translator.PromptRunner = (_, _) => Task.FromResult<string?>("diagnostics without translation sections");
        Check(!AwaitResult(translator.TranslateReleaseAsync(release, CancellationToken.None, true)), "Malformed translation was accepted");
        var previous = release.TranslatedGroups;
        translator.PromptRunner = (_, _) => Task.FromResult<string?>(null);
        Check(!AwaitResult(translator.TranslateReleaseAsync(release, CancellationToken.None, true)) && ReferenceEquals(previous, release.TranslatedGroups), "Failed retry erased translation");
        var pending = new TaskCompletionSource<string?>();
        translator.PromptRunner = (_, _) => pending.Task;
        var late = translator.TranslateReleaseAsync(release, CancellationToken.None, true);
        settings.Save(settings.Current with { BackendId = "opencode" });
        pending.SetResult(valid.Replace("Исправлен", "Изменён"));
        Check(!AwaitResult(late) && ReferenceEquals(previous, release.TranslatedGroups), "Old backend response was applied after selection changed");
        var loaded = new TranslationAgentSettings(settings.FilePath);
        Check(loaded.Current.BackendId == "opencode" && loaded.Current.Profiles["claude-code"].Model == "test-model", "Per-backend settings did not persist");
        release.Groups[0].Items.Add("Fixed another issue");
        Check(release.IsTranslationStale, "Changed source did not invalidate translation provenance");
        var repository = new FileChangelogRepository(Path.Combine(output, "agent-cache"));
        var document = new ChangelogDocument { StageId = "test", Releases = [release] };
        repository.Save(document);
        var cacheFile = Directory.GetFiles(repository.CacheDirectory, "*.json").Single();
        File.WriteAllText(cacheFile, File.ReadAllText(cacheFile).Replace("\"SchemaVersion\": 8", "\"SchemaVersion\": 7"));
        Check(repository.TryLoad("test")?.Releases[0].HasTranslation == true, "Version 7 cache migration lost translation");
        Console.WriteLine("PASS: agent selection, settings persistence, provenance, stale source, failed retry, late response, cache v7");
    }
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        TranslationAgentSettings.Default = new(Path.Combine(output, "translation-settings.json"));
        TranslationAgentSettings.Default.Save(TranslationAgentSettings.Default.Current with { Automatic = false });
        var app = new App();
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new MainWindow();
        var viewModel = window.ViewModel;
        Check(ReferenceEquals(window.DataContext, viewModel), "MainWindow DataContext is not MainViewModel");
        viewModel.ConfigurePaths(resultsPath: Path.Combine(output, "results.json"));
        foreach (var item in viewModel.Stages)
        {
            item.InvalidateResult();
        }

        var stage = viewModel.Stages.Single(item => item.Id == "Claude Code");
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        stage.Queue();
        stage.SetRunning(OperationState.Checking);
        Check(stage.HasUpdate && stage.InstalledVersion == "2.1.299", "Queue/check lost the package result");
        stage.SetFailed();
        Check(stage.HasUpdate && stage.VersionDisplayText == "v2.1.299 → v2.2.1", "Failure lost known versions");
        stage.SetCompleted();
        Check(stage.Status == "Есть обновление", "Completion hid the package result");
        viewModel.Stages[3].SetCheckResult("DIRECT", info: "Плагинов: 2");
        Check(viewModel.Stages[3].CanUpdate, "Unsupported comparison blocked an explicit plugin update");
        Check(viewModel.Stages[3].CanSelectForUpdate && !viewModel.Stages[3].IsSelectedForUpdate, "Direct update was selected automatically");
        Check(viewModel.Stages[3].Status == "Обновляется напрямую" && viewModel.Stages[3].VersionDisplayText == "Плагинов: 2", "Direct update has no explanation");
        Check(stage.VersionBrush == Brushes.LightSlateGray && stage.VersionTargetBrush == Brushes.Gold, "Update versions have ambiguous colors");
        Await(viewModel.SelectOrCheckStageAsync(stage));
        Check(!stage.IsSelectedForUpdate, "Card click did not deselect an available update");
        Await(viewModel.SelectOrCheckStageAsync(stage));
        Check(stage.IsSelectedForUpdate, "Card click did not select an available update");
        viewModel.IsRunning = true;
        Await(viewModel.SelectOrCheckStageAsync(stage));
        Check(stage.IsSelectedForUpdate, "Busy card changed selection");
        viewModel.IsRunning = false;
        var summary = MainViewModel.CheckSummary([stage]);
        Check(summary == "Доступно обновлений: 1", "Summary retained zero counters");
        var updateButton = (Button)window.FindName("UpdateButton");
        Check(updateButton.IsEnabled && updateButton.Content.ToString()!.EndsWith("(1)"), "Selection count did not refresh");
        Await(viewModel.SelectOrCheckStageAsync(viewModel.Stages[3]));
        Check(viewModel.Stages[3].IsSelectedForUpdate && updateButton.Content.ToString()!.EndsWith("(2)"), "Direct card click did not join the bulk selection");
        Await(viewModel.SelectOrCheckStageAsync(viewModel.Stages[3]));
        stage.IsSelectedForUpdate = false;
        Check(!updateButton.IsEnabled, "Deselection still permits bulk updates");
        var run = viewModel.CreateVendorRun("Anthropic", stage);
        var line = "[12:34:56] UI_RESULT {\"Stage\":\"Claude Code\",\"Result\":\"CURRENT\",\"Installed\":\"2.2.1\",\"Available\":\"2.2.1\"}\r\n";
        viewModel.HandleOutputChunk(line[..25], false, run);
        viewModel.HandleOutputChunk(line[25..], false, run);
        Check(stage.Status == "Актуально" && stage.InstalledVersion == "2.2.1" && !stage.HasUpdate, "Chunked JSON result failed");
        var pluginStage = viewModel.Stages[3];
        var pluginRun = viewModel.CreateVendorRun("OpenAI", pluginStage);
        viewModel.HandleOutputLine("[12:34:56] UI_RESULT {\"Stage\":\"Codex plugins\",\"Result\":\"DIRECT\",\"Info\":\"Плагинов: 2\"}", false, pluginRun);
        Check(pluginStage.CheckResult == "DIRECT" && pluginStage.Info == "Плагинов: 2", "Structured direct result was not accepted");
        var npmStage = viewModel.Stages[2];
        pluginRun = viewModel.CreateVendorRun("OpenAI", npmStage);
        viewModel.HandleOutputLine("[12:34:56] UI_RESULT {\"Stage\":\"Codex npm package\",\"Result\":\"ERROR\",\"Installed\":\"1.0.0\",\"Info\":\"Не удалось получить доступную версию\"}", false, pluginRun);
        Check(npmStage.Status == "Ошибка проверки" && npmStage.StatusBrush == Brushes.LightCoral && npmStage.InstalledVersion == "1.0.0", "Check failure was confused with absence");
        viewModel.Stages[6].SetCheckResult("NOT_INSTALLED");
        Check(viewModel.Stages[6].Status == "Не установлено" && !viewModel.Stages[6].CanUpdate, "Absent package still permits Update");
        var errorSummary = MainViewModel.CheckSummary([npmStage, viewModel.Stages[6]]);
        Check(errorSummary == "Ошибок проверки: 1 · Не установлено: 1", "Summary mixed absence and check failures");
        Check(!((TextBox)window.FindName("LogTextBox")).Text.Contains("UI_RESULT"), "Protocol leaked into the visible journal");
        var warningRun = viewModel.CreateVendorRun("Anthropic", stage);
        stage.SetRunning(OperationState.Updating);
        warningRun.ActiveStage = stage;
        viewModel.HandleOutputLine("npm warn EBADENGINE Unsupported engine", true, warningRun);
        Check(!warningRun.SawError && stage.OperationState == OperationState.Updating, "Non-fatal stderr warning marked the stage as failed");
        stage.SetCompleted();
        viewModel.HandleOutputLine("[12:34:56] UI_RESULT {\"Stage\":\"Codex desktop\",\"Result\":\"CURRENT\"}", false, run);
        Check(run.SawError, "Cross-vendor result was accepted");
        viewModel.HandleOutputLine("[12:34:56] UI_RESULT {bad json}", false, run);
        Check(stage.InstalledVersion == "2.2.1", "Malformed JSON destroyed the last valid result");
        viewModel.SaveResults();
        stage.InvalidateResult();
        viewModel.LoadResults();
        Check(stage.Status == "Актуально" && stage.CheckedAt is not null && stage.InstalledVersion == "2.2.1", "Snapshot round trip failed");
        Check(viewModel.Stages[3].CheckResult == "DIRECT" && viewModel.Stages[3].Info == "Плагинов: 2", "Direct-update details were not persisted");
        Check(!viewModel.Stages[3].IsSelectedForUpdate, "Restored direct update was automatically selected");
        Check(npmStage.CheckResult == "ERROR" && npmStage.Info == "Не удалось получить доступную версию", "Check error was not persisted");
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        viewModel.Stages[0].SetCheckResult("CURRENT", "26.1013.1");
        viewModel.Stages[4].SetCheckResult("NOT_INSTALLED");
        viewModel.Stages[9].SetCheckResult("DIRECT", "1.3.5");
        Check(viewModel.Stages[9].VersionBrush == Brushes.LightSlateGray, "Uncompared version looks current");
        foreach (var vendor in viewModel.Vendors)
        {
            viewModel.RefreshVendorSummary(vendor);
        }

        var root = (FrameworkElement)window.Content;
        foreach (var (width, height) in new[] { (1072, 740), (932, 620) })
        {
            root.Measure(new(width, height));
            root.Arrange(new(0, 0, width, height));
            root.UpdateLayout();
            var tabs = (FrameworkElement)window.FindName("LogTabs");
            Check(tabs.TranslatePoint(new(0, tabs.ActualHeight), root).Y <= root.ActualHeight,
                $"Journal extends below the window: {width}x{height}, bottom={tabs.TranslatePoint(new(0, tabs.ActualHeight), root).Y}, available={root.ActualHeight}");

            var splitter = (GridSplitter)window.FindName("LogSplitter");
            Check(splitter.ActualHeight >= 8, "GridSplitter between cards and journal collapsed");
            var mainLogBox = (TextBox)window.FindName("LogTextBox");
            Check(mainLogBox.ActualHeight >= 60, $"Main log viewport is too small ({mainLogBox.ActualHeight}px) at {width}x{height}");
            var tabControl = (TabControl)window.FindName("LogTabs");
            tabControl.SelectedIndex = 1;
            root.UpdateLayout();
            Check(ReferenceEquals(viewModel.LogPanel.SelectedVendor, viewModel.Vendors[0]), "Selecting vendor log tab did not update LogPanelViewModel.SelectedVendor");
            var vendorLogBox = Descendants<TextBox>(tabControl).First(box => box != mainLogBox);
            Check(vendorLogBox.ActualHeight >= 55, $"Vendor log viewport is too small ({vendorLogBox.ActualHeight}px) at {width}x{height}");
            tabControl.SelectedIndex = 0;
            root.UpdateLayout();
            Check(viewModel.LogPanel.SelectedVendor is null, "Selecting general log tab did not clear LogPanelViewModel.SelectedVendor");

            Check(Descendants<StageCardView>(root).Count() == viewModel.Stages.Count, "MainWindow did not materialize StageCardView for every stage");
            Check(Descendants<LogPanelView>(root).Count() == 1, "MainWindow did not materialize LogPanelView");
            var logPanelView = Descendants<LogPanelView>(root).Single();
            logPanelView.RegisterNames(window);
            var firstMenuButton = Descendants<Button>(root).First(button => Equals(button.Content, "⋮"));
            var firstCardBorder = Descendants<Border>(root).First(border => border.ContextMenu is not null);
            firstMenuButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var cardMenu = firstCardBorder.ContextMenu!;
            Check(ReferenceEquals(cardMenu.PlacementTarget, firstCardBorder) && cardMenu.DataContext is StageCardViewModel,
                "StageCardView menu button did not bind ContextMenu PlacementTarget to StageCardViewModel");
            Check(cardMenu.Items.OfType<MenuItem>().Count() == 5 && cardMenu.Items.OfType<MenuItem>().All(item => item.Command is not null),
                "StageCardView ContextMenu items are missing bound commands");
            cardMenu.IsOpen = false;
            Check(Descendants<Button>(root).Count(button => Equals(button.Content, "⋮")) == viewModel.Stages.Count, "Missing visible card menus");
            Check(Descendants<Border>(root).Count(border => border.ContextMenu is not null) == viewModel.Stages.Count, "Card context menus missing");
            var directChoices = Descendants<CheckBox>(root).Where(box => box.DataContext is UpdateStage { PackageStatus: PackageStatus.DirectUpdate }).ToArray();
            Check(directChoices.Length == 2 && directChoices.All(box => box.Visibility == Visibility.Visible && box.IsChecked == false), "Direct-update checkboxes are hidden or checked by default");
            var cards = Descendants<Border>(root).Where(border => border.ContextMenu is not null).ToArray();
            Check(cards.Length == 17 && cards.Max(card => card.ActualWidth) - cards.Min(card => card.ActualWidth) < 1, "Vendor card grid widths differ");
            foreach (var row in Descendants<Button>(root).Where(button => Equals(button.Tag, "Stage")).GroupBy(button => Math.Round(button.TranslatePoint(new(0, 0), root).Y)))
            {
                var positions = row.Select(button => Descendants<TextBlock>(button).First().TranslatePoint(new(0, 0), root).Y).ToArray();
                Check(positions.Max() - positions.Min() < 1, "Card titles are not aligned");
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"interface-{width}.png"));
            encoder.Save(file);
            var stopButton = (Button)window.FindName("StopButton");
            stopButton.Visibility = Visibility.Visible;
            root.Measure(new(width, height));
            root.Arrange(new(0, 0, width, height));
            root.UpdateLayout();
            Check(tabs.TranslatePoint(new(0, tabs.ActualHeight), root).Y <= root.ActualHeight, "Busy layout extends below the window");
            stopButton.Visibility = Visibility.Collapsed;
        }

        Check(((Button)window.FindName("StopButton")).Visibility == Visibility.Collapsed, "Idle stop button is visible");
        var logZoomIn = (Button)window.FindName("LogZoomInButton");
        var logZoomOut = (Button)window.FindName("LogZoomOutButton");
        var initialLogSize = ((TextBox)window.FindName("LogTextBox")).FontSize;
        Click(logZoomIn);
        Check(Math.Abs(((TextBox)window.FindName("LogTextBox")).FontSize - (initialLogSize + 1)) < 0.01, "Log zoom in did not increase font size");
        Click(logZoomOut);
        Check(Math.Abs(((TextBox)window.FindName("LogTextBox")).FontSize - initialLogSize) < 0.01, "Log zoom out did not restore font size");
        CheckChangelog(output, viewModel.Stages);
        CheckTranslationAgentSettings(output);
        CheckAgentTranslation(output);
        var fixture = Path.Combine(output, "fixture.ps1");
        var calls = Path.Combine(output, "calls.txt");
        File.WriteAllText(calls, "");
        File.Delete(Path.Combine(output, "fail"));
        File.Delete(Path.Combine(output, "wait"));
        File.Delete(Path.Combine(output, "missing"));
        File.Delete(Path.Combine(output, "error-result"));
        File.WriteAllText(fixture, """
                                   param($Vendor, $LogPath, $Action, $OnlyStage, [switch]$CheckOnly)
                                   [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
                                   Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value "$Action|$OnlyStage|$CheckOnly"
                                   Write-Output "[12:34:56] === $OnlyStage ==="
                                   if (Test-Path (Join-Path $PSScriptRoot 'fail')) { exit 5 }
                                   if (Test-Path (Join-Path $PSScriptRoot 'missing')) { exit 0 }
                                   if (Test-Path (Join-Path $PSScriptRoot 'wait')) { Start-Sleep -Seconds 30 }
                                   $result = if (Test-Path (Join-Path $PSScriptRoot 'error-result')) { 'ERROR' } else { 'CURRENT' }
                                   $json = @{ Stage = $OnlyStage; Result = $result; Installed = '2.2.1'; Available = '2.2.1' } | ConvertTo-Json -Compress
                                   Write-Output "[12:34:56] UI_RESULT $json"
                                   Write-Output "[12:34:56] === COMPLETED: $OnlyStage ==="
                                   """);

        viewModel.ConfigurePaths(scriptPath: fixture, logPath: Path.Combine(output, "main.log"));
        foreach (var vendor in viewModel.Vendors)
        {
            vendor.LogPath = Path.Combine(output, vendor.Name + ".log");
        }

        var other = viewModel.Stages.Single(item => item.Id == "Claude npm package");
        var untouched = viewModel.Stages[0].InstalledVersion;
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        other.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        Await(viewModel.RunUpdaterAsync(null, "Update", false, [stage, other]));
        Check(File.ReadAllLines(calls).SequenceEqual(new[] { "Update|Claude Code|False", "Update|Claude npm package|False" }), "Bulk update dispatched unselected stages or changed mode");
        Check(stage.Status == "Актуально" && other.Status == "Актуально", "Post-update results missing");
        Check(viewModel.Stages[0].InstalledVersion == untouched, "Selected update reset another vendor");
        File.WriteAllText(calls, "");
        var terminal = viewModel.Stages.Single(item => item.Id == "Windows Terminal");
        terminal.SetCheckResult("AVAILABLE", "1.0.0", "2.2.1");
        Await(viewModel.RunUpdaterAsync(terminal, "Update", false));
        Check(File.ReadAllLines(calls).SequenceEqual(new[] { "Update|Windows Terminal|False" }) && terminal.Status == "Актуально", "Development component dispatch failed");
        Check(viewModel.Stages[0].InstalledVersion == untouched, "Development update reset an AI component");
        File.WriteAllText(calls, "");
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        var selectedPlugin = viewModel.Stages[7];
        selectedPlugin.SetCheckResult("DIRECT", info: "Плагинов: 10");
        Await(viewModel.SelectOrCheckStageAsync(selectedPlugin));
        Click(updateButton);
        Await(AwaitIdle(viewModel));
        Check(File.ReadAllLines(calls).SequenceEqual(new[] { "Update|Claude Code|False", "Update|Claude plugins|False" }), "Bulk button omitted the selected direct update or included unchecked cards");
        File.WriteAllText(Path.Combine(output, "fail"), "");
        Await(viewModel.RunUpdaterAsync(stage, "Update", false));
        Check(stage.OperationState == OperationState.Failed && other.Status == "Актуально", "Failed update affected another card");
        Check(stage.InstalledVersion == "2.2.1", "Failed update lost the last known version");
        File.Delete(Path.Combine(output, "fail"));
        File.WriteAllText(Path.Combine(output, "missing"), "");
        Await(viewModel.RunUpdaterAsync(stage, "Update", false));
        Check(stage.OperationState == OperationState.Failed, "Missing result was reported successful");
        File.Delete(Path.Combine(output, "missing"));
        File.WriteAllText(Path.Combine(output, "wait"), "");
        var stopped = viewModel.RunUpdaterAsync(null, "Update", false, [stage, other]);
        Click((Button)window.FindName("StopButton"));
        Await(stopped);
        Check(stage.OperationState == OperationState.Stopped && other.OperationState == OperationState.Stopped, "Stop did not finish running/queued cards");
        File.Delete(Path.Combine(output, "wait"));
        File.WriteAllText(Path.Combine(output, "error-result"), "");
        Await(viewModel.RunUpdaterAsync(stage, "Update", true));
        Check(stage.Status == "Ошибка проверки" && ((TextBlock)window.FindName("OverallStatusText")).Text.Contains("Ошибок проверки: 1"), "Check completion overwrote its failure result");
        File.Delete(Path.Combine(output, "error-result"));
        Console.WriteLine("PASS: independent states, selection, chunked JSON, vendor guard, malformed payload, persistence, two WPF widths, selected dispatch, post-update check, failure isolation, stop running/queued cards");
        var darkOutput = Path.Combine(Path.GetDirectoryName(output)!, "dark-controls");
        Directory.CreateDirectory(darkOutput);
        CheckStyles(darkOutput);
        Console.WriteLine("PASS: application control templates, checkbox states and accessible toggle, text menu, changelog layout, themed confirmation with safe default");
        CheckIcon(darkOutput);
        Console.WriteLine("PASS: packaged ICO sizes, transparency and nonempty frames");
    }

    private sealed class StubChangelogContentFetcher(IReadOnlyDictionary<string, string> responses) : IChangelogContentFetcher
    {
        public Task<string> FetchAsync(ChangelogSource source, CancellationToken cancellationToken) =>
            DownloadUrlAsync(source.DownloadUrl ?? "", cancellationToken);

        public async Task<(string Content, string? ETag, bool NotModified)> FetchWithETagAsync(
            ChangelogSource source,
            string? cachedETag,
            CancellationToken cancellationToken) =>
            (await DownloadUrlAsync(source.DownloadUrl ?? "", cancellationToken), "\"stub-etag\"", false);

        public Task<string> DownloadUrlAsync(string url, CancellationToken cancellationToken) =>
            responses.TryGetValue(url, out var content)
                ? Task.FromResult(content)
                : throw new System.Net.Http.HttpRequestException($"Unexpected URL in stub fetcher: {url}");

        public string? FindAgyExecutable() => null;

        public Task<string> RunCommandAsync(string executable, IReadOnlyList<string> arguments, int timeoutSeconds, CancellationToken cancellationToken) =>
            Task.FromResult("");
    }
}

using AIUpdater.Bootstrap;
using AIUpdater.Services;
using AIUpdater.ViewModels;
using AIUpdater.Views;
using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AIUpdater.InterfaceChecks;

internal static partial class Program
{
    private static void CaptureCachedViews(VisualTestHost host, MainWindow window, ShellViewModel shell, IServiceProvider provider, string output)
    {
        var source = AppPaths.Default;
        new FileStageResultStore(source.ResultsFile).Load(shell.Updates.Stages, message => throw new InvalidOperationException(message));
        foreach (var vendor in shell.Updates.Vendors) shell.Updates.RefreshVendorSummary(vendor);
        var stage = shell.Updates.Stages[0];
        var cached = new FileChangelogRepository(source.ChangelogDirectory).TryLoad(stage.Id) ?? throw new InvalidOperationException("Нет сохранённой истории первого компонента");
        cached.UpdatedAt = DateTimeOffset.UtcNow;
        provider.GetRequiredService<IChangelogService>().SaveCachedDocument(cached);
        shell.Changelog.SelectStage(shell.Updates.Stages[1]);
        shell.ShowChangelog(stage);
        foreach (var width in new[] { 1200, 1000 })
        foreach (var scale in new[] { 1.0, 1.6 })
        {
            window.SetCurrentValue(FrameworkElement.WidthProperty, (double)width);
            window.SetCurrentValue(FrameworkElement.HeightProperty, width == 1200 ? 800d : 700d);
            shell.Preferences.FontScale = scale;
            foreach (var key in new[] { SectionKey.Updates, SectionKey.DotNet, SectionKey.Changelog })
            {
                shell.Navigate(key);
                if (key == SectionKey.DotNet) Await(shell.DotNet.EnsureLoadedAsync());
                host.Settle();
                FrameworkElement view = key switch
                {
                    SectionKey.Updates => Descendants<UpdatesView>(window).Single(),
                    SectionKey.DotNet => Descendants<DotNetView>(window).Single(),
                    _ => Descendants<ChangelogView>(window).Single(),
                };
                var scrolls = key == SectionKey.Changelog
                    ? Descendants<ScrollViewer>((ListBox)view.FindName("ComponentList"))
                    : Descendants<ScrollViewer>(view);
                Check(scrolls.All(scroll => scroll.ScrollableWidth <= 1), "Cached user data caused horizontal overflow");
                Render((FrameworkElement)window.Content, Path.Combine(output, $"cached-{key}-{width}-{(int)(scale * 100)}.png"), width, width == 1200 ? 800 : 700);
            }
        }
    }

    private static void CheckBindingSensor()
    {
        using var sensor = BindingErrorSink.Attach();
        var target = new TextBlock();
        target.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("MissingBindingSensorProperty") { Source = new object() });
        target.Measure(new(100, 40));
        target.UpdateLayout();
        Check(sensor.Count > 0, "The binding sensor did not detect a deliberately missing path");
        System.Windows.Data.BindingOperations.ClearAllBindings(target);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AIUpdater.slnx"))) root = root.Parent;
        Check(root is not null, "Source checkout was not found for platform gateway checks");
        var violations = KeepShell.Testing.Rules.PlatformCallRule.Check(Directory.GetFiles(Path.Combine(root!.FullName, "src", "AIUpdater", "ViewModels"), "*.cs", SearchOption.AllDirectories), root.FullName);
        Check(violations.Count == 0, $"Direct platform calls in ViewModels: {string.Join(Environment.NewLine, violations)}");
    }

    private static void CheckOperationNavigation(VisualTestHost host, MainWindow window, ShellViewModel shell, string output)
    {
        Check(shell.IsBusy, "The operation fixture did not start");
        Await(WaitForActiveStage(shell.Updates));
        host.Settle();
        Check(shell.Updates.RunTotal == 2 && shell.Updates.CompletedCount == 0 && shell.Updates.ActiveCount == 1 && shell.Updates.QueuedCount == 1, "Operation progress did not distinguish the active component and queued work");
        Check(shell.Updates.ProgressMax == 2 && shell.Updates.ActiveOperationText.Contains("Claude"), "The operation panel has no total or current component");
        var updates = Descendants<UpdatesView>(window).Single();
        Check(((ItemsControl)updates.FindName("StageItemsControl")).IsEnabled && Descendants<StageCardView>(updates).All(view => ((TextBlock)view.FindName("StageTitle")).IsEnabled), "Running operations dimmed the component list");
        Render((FrameworkElement)window.Content, Path.Combine(output, "shell-updates-running.png"), 1000, 700);
        var selected = shell.Changelog.SelectedStage;
        shell.ShowSettings();
        host.Settle();
        var draft = shell.SettingsPage.Agent.Model;
        shell.SettingsPage.Agent.Model = "operation-draft";
        shell.Navigate(SectionKey.Logs);
        host.Settle();
        Check(shell.IsBusy && ReferenceEquals(Descendants<LogsView>(window).Single().DataContext, shell.Updates.LogPanel), "Navigation interrupted the running operation or detached its log");
        Check(shell.Changelog.SelectedStage == selected, "Navigation changed the selected changelog component");
        var close = shell.RequestCloseAsync();
        host.Settle();
        var confirmation = (MessageDialogViewModel)Descendants<MessageDialogView>(window).Single(view => view.IsVisible).DataContext;
        confirmation.CancelCommand.Execute(null);
        Check(!AwaitResult(close) && shell.IsBusy, "Cancelling close stopped the running operation");
        shell.ShowSettings();
        host.Settle();
        Check(shell.SettingsPage.Agent.Model == "operation-draft", "Running operation navigation discarded the settings draft");
        shell.SettingsPage.Agent.Model = draft;
        var status = Descendants<StatusBarView>(window).Single();
        Click(Descendants<Button>(status).Single(button => button.IsVisible));
        Console.WriteLine("PASS: navigation during an operation, shared status stop, safe close cancellation and retained drafts");
    }

    private static async Task WaitForActiveStage(MainViewModel updates)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (updates.IsRunning && (updates.ActiveCount == 0 || !updates.ActiveOperationText.Contains("37%"))) await Task.Delay(20, timeout.Token);
    }

    private static void CheckParallelOperationLayout(VisualTestHost host, MainWindow window, ShellViewModel shell, string output)
    {
        Await(WaitForParallelStages(shell.Updates));
        shell.Navigate(SectionKey.Updates);
        host.Settle();
        var updates = Descendants<UpdatesView>(window).Single();
        var status = Descendants<StatusBarView>(window).Single();
        var cards = Descendants<StageCardView>(updates).Where(card => card.IsVisible).ToArray();
        Check(shell.Updates.RunTotal == 17 && shell.Updates.CompletedCount == 1 && shell.Updates.QueuedCount == 12, "Parallel fixture did not reproduce the owner's running queue");
        Check(cards.Count(card => ((TextBlock)card.FindName("OperationDetailText")).Text.Contains("37%")) == 4, "Per-component download details were lost or assigned to queued work");
        foreach (var width in new[] { 1200, 1000 })
        foreach (var scale in new[] { 1.0, 1.6 })
        {
            window.SetCurrentValue(FrameworkElement.WidthProperty, (double)width);
            window.SetCurrentValue(FrameworkElement.HeightProperty, width == 1200 ? 800d : 700d);
            shell.Preferences.FontScale = scale;
            host.Settle();
            Check(status.ActualHeight <= 48 * scale, "Parallel stage names expanded the shared status bar");
            Check(((Border)updates.FindName("OperationPanel")).ActualHeight <= 4, "Progress displaced the component list");
            Check(((ScrollViewer)updates.FindName("ComponentsScroll")).ActualHeight >= 250, "The component viewport collapsed during parallel work");
            Check(!((TextBlock)status.FindName("OperationStatusText")).Text.Contains('\n'), "The common status contains explicit line breaks");
            Check(Descendants<ScrollViewer>(updates).All(scroll => scroll.ScrollableWidth <= 1), "Parallel work caused horizontal overflow");
            Render((FrameworkElement)window.Content, Path.Combine(output, $"parallel-updates-{width}-{(int)(scale * 100)}.png"), width, width == 1200 ? 800 : 700);
            ((CheckBox)updates.FindName("ActiveOnlyToggle")).SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            host.Settle();
            var activeCards = Descendants<StageCardView>(updates).Where(card => card.IsVisible).ToArray();
            Render((FrameworkElement)window.Content, Path.Combine(output, $"parallel-active-{width}-{(int)(scale * 100)}.png"), width, width == 1200 ? 800 : 700);
            Check(activeCards.Length == 4 && activeCards.Select(card => ((StageCardViewModel)card.DataContext).Stage.Vendor).Distinct().Count() == 4,
                $"The active filter did not bring all four vendors into one list: visible={activeCards.Length}, model={shell.Updates.ShowActiveOnly}, all={((ItemsControl)updates.FindName("StageItemsControl")).IsVisible}, running={((ItemsControl)updates.FindName("RunningItemsControl")).Items.Count}");
            ((CheckBox)updates.FindName("ActiveOnlyToggle")).SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            host.Settle();
        }
        shell.Preferences.FontScale = 1;
        shell.Navigate(SectionKey.Logs);
        host.Settle();
        Check(status.IsVisible && shell.OperationCaption.Contains("В работе: 4"), "Progress disappeared when navigating away from updates");
        Render((FrameworkElement)window.Content, Path.Combine(output, "parallel-logs-1000.png"), 1000, 700);
        shell.Navigate(SectionKey.Updates);
        shell.Updates.ShowActiveOnly = true;
        Console.WriteLine("PASS: 17 components, four simultaneous processes, current row details, compact global progress and navigation at two widths and scales");
    }

    private static async Task WaitForParallelStages(MainViewModel updates)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (updates.IsRunning && (updates.ActiveCount != 4 || updates.CompletedCount != 1
               || updates.Vendors.SelectMany(vendor => vendor.Cards).Cast<StageCardViewModel>().Count(card => card.OperationDetail.Contains("37%")) != 4))
            await Task.Delay(20, timeout.Token);
    }

    private static void CheckShellWindow(VisualTestHost host, MainWindow window, ShellViewModel shell, string output)
    {
        Check(window.Content is KeepShell.Views.ShellLayout { ModalCoversShell: true }, "The application must use the real ShellLayout, including its modal surface");
        Check(shell is ShellViewModelBase
              && shell.Updates is IPageHeader && shell.Updates is IPageStatus && shell.Updates is IPageRefresh
              && shell.DotNet is IPageHeader && shell.DotNet is IPageRefresh,
            "The shell and pages do not expose the framework contracts");
        Check(typeof(IView<MainViewModel>).IsAssignableFrom(typeof(UpdatesView))
              && typeof(IView<DotNetViewModel>).IsAssignableFrom(typeof(DotNetView))
              && typeof(IView<DotNetPackageRowViewModel>).IsAssignableFrom(typeof(DotNetPackageRowView))
              && typeof(IView<ChangelogViewModel>).IsAssignableFrom(typeof(ChangelogView))
              && typeof(IView<LogPanelViewModel>).IsAssignableFrom(typeof(LogsView))
              && typeof(IView<SettingsViewModel>).IsAssignableFrom(typeof(SettingsView)), "Page pairs are not declared for the KeepShell generator");
        var updates = Descendants<UpdatesView>(window).Single();
        foreach (var (width, height) in new[] { (1200, 800), (1000, 700) })
        {
            window.SetCurrentValue(FrameworkElement.WidthProperty, (double)width);
            window.SetCurrentValue(FrameworkElement.HeightProperty, (double)height);
            shell.Navigate(SectionKey.Updates);
            host.Settle();
            var cards = Descendants<StageCardView>(updates).ToArray();
            Check(cards.Length == 17, "The update page did not materialize every tool card");
            var widths = cards.Select(card => card.ActualWidth).ToArray();
            Check(widths.Max() - widths.Min() < 1, "Vendor card widths differ");
            var positions = cards.Select(card => ((TextBlock)card.FindName("StageTitle")).TranslatePoint(new(), updates).X).ToArray();
            Check(positions.Max() - positions.Min() < 1, "Component name columns are not aligned");
            Check(Descendants<ScrollViewer>(updates).All(viewer => viewer.ExtentWidth <= viewer.ViewportWidth + 1), "The component list overflows horizontally");
            var menuButton = (Button)cards[0].FindName("CardMenuButton");
            menuButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var cardMenu = ((Border)cards[0].FindName("CardBorder")).ContextMenu;
            Check(cardMenu.DataContext is StageCardViewModel && cardMenu.Items.OfType<MenuItem>().Count() == 5, "Card actions lost their shared context");
            cardMenu.IsOpen = false;
            var directChoices = Descendants<CheckBox>(updates).Where(box => box.DataContext is AIUpdater.Models.UpdateStage { PackageStatus: AIUpdater.Models.PackageStatus.DirectUpdate }).ToArray();
            Check(directChoices.Length == 2 && directChoices.All(box => box.IsChecked == false), "Direct updates became selected automatically");
            Render((FrameworkElement)window.Content, Path.Combine(output, $"shell-updates-{width}.png"), width, height);

            var dotNetCard = cards.Single(card => ((StageCardViewModel)card.DataContext).Stage.Id == ".NET SDK 10");
            ((StageCardViewModel)dotNetCard.DataContext).InstallCommand.Execute(null);
            host.Settle();
            Await(shell.DotNet.EnsureLoadedAsync());
            host.Settle();
            var dotNetView = Descendants<DotNetView>(window).Single();
            Check(dotNetView.IsVisible && ReferenceEquals(dotNetView.DataContext, shell.DotNet), "General .NET card action did not navigate to the .NET manager section");
            Check(shell.DotNet.Channels.Select(ch => ch.Info.Channel).SequenceEqual(["11.0", "10.0", "12.0"]), "Active and preview .NET channels were not ordered or EOL leaked in by default");
            var net11Sdk = shell.DotNet.Channels.Single(ch => ch.Info.Channel == "11.0").Packages.Single(pkg => pkg.Entry.Kind == AIUpdater.Models.DotNetPackageKind.Sdk);
            Check(net11Sdk.AvailableText == "11.0.101" && net11Sdk.PrimaryActionText == "Установить" && net11Sdk.CanInstallPrimary, ".NET 11 SDK install action is missing");
            var net10RowView = Descendants<DotNetPackageRowView>(dotNetView)
                .Single(row => row.DataContext is DotNetPackageRowViewModel { Entry: { Channel: "10.0", Kind: AIUpdater.Models.DotNetPackageKind.Sdk } });
            var net10MenuBtn = (Button)net10RowView.FindName("RowMenuButton");
            net10MenuBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var net10Menu = ((Border)net10RowView.FindName("RowBorder")).ContextMenu!;
            var menuItems = net10Menu.Items.OfType<MenuItem>().ToArray();
            Check(menuItems.Any(mi => mi.Header?.ToString()?.Contains("10.0.102") == true && mi.IsEnabled)
                  && menuItems.Any(mi => mi.Header?.ToString()?.Contains("10.0.302") == true && !mi.IsEnabled),
                "Standalone vs Visual Studio ownership was not reflected in uninstall menu items");
            net10Menu.IsOpen = false;
            var eolToggle = (CheckBox)dotNetView.FindName("EolToggle");
            eolToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            host.Settle();
            Check(shell.DotNet.Channels.Any(ch => ch.Info.Channel == "8.0" && ch.BadgeText == "EOL"), "EOL toggle did not reveal .NET 8 EOL branch");
            eolToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            var installedOnlyToggle = (CheckBox)dotNetView.FindName("InstalledOnlyToggle");
            installedOnlyToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            host.Settle();
            Check(shell.DotNet.Channels.Select(ch => ch.Info.Channel).SequenceEqual(["10.0"]), "Installed-only toggle did not filter uninstalled .NET channels");
            installedOnlyToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            host.Settle();
            Check(Descendants<ScrollViewer>(dotNetView).All(viewer => viewer.ExtentWidth <= viewer.ViewportWidth + 1 && viewer.ScrollableWidth <= 1),
                ".NET package list overflows horizontally");
            Render((FrameworkElement)window.Content, Path.Combine(output, $"shell-dotnet-{width}.png"), width, height);

            shell.Navigate(SectionKey.Logs);
            host.Settle();
            var logs = Descendants<LogPanelView>(window).Single();
            Check(ReferenceEquals(logs.DataContext, shell.Updates.LogPanel), "Logs and update operations use different state instances");
            var tabs = (TabControl)logs.FindName("LogTabs");
            var general = (TextBox)logs.FindName("LogTextBox");
            Check(general.ActualHeight >= 150, "The log page has no usable viewport");
            tabs.SelectedIndex = 1;
            host.Settle();
            Check(ReferenceEquals(shell.Updates.LogPanel.SelectedVendor, shell.Updates.Vendors[0]), "Vendor selection did not reach the shared log model");
            tabs.SelectedIndex = 0;
            host.Settle();
            var size = general.FontSize;
            Click((Button)logs.FindName("LogZoomInButton"));
            Check(general.FontSize == size + 1, "Log zoom did not update the view");
            Click((Button)logs.FindName("LogZoomOutButton"));
            Render((FrameworkElement)window.Content, Path.Combine(output, $"shell-logs-{width}.png"), width, height);

            shell.ShowSettings();
            host.Settle();
            var settingsView = Descendants<SettingsView>(window).Single();
            Check(settingsView.IsVisible && ReferenceEquals(settingsView.DataContext, shell.SettingsPage), "Generated settings view did not open");
            var modelBefore = shell.SettingsPage.Agent.Model;
            shell.SettingsPage.Agent.Model = "draft-retained-through-navigation";
            shell.Navigate(SectionKey.Changelog);
            host.Settle();
            Check(Descendants<ChangelogView>(window).Single().IsVisible, "Generated changelog view did not open");
            CheckChangelogOverflow(host, window, shell, output, width);
            Render((FrameworkElement)window.Content, Path.Combine(output, $"shell-changelog-{width}.png"), width, height);
            shell.ShowSettings();
            host.Settle();
            Check(shell.SettingsPage.Agent.Model == "draft-retained-through-navigation", "Navigation discarded agent setting drafts");
            shell.SettingsPage.Agent.Model = modelBefore;
            Render((FrameworkElement)window.Content, Path.Combine(output, $"shell-settings-{width}.png"), width, height);
            shell.Navigate(SectionKey.Updates);
            host.Settle();
            Check(ReferenceEquals(updates, Descendants<UpdatesView>(window).Single()), "Returning to a retained page created a second view");
        }

        shell.Preferences.NavCollapsed = true;
        host.Settle();
        Check(shell.IsNavCollapsed && shell.NavWidth == 56, "The shell did not apply collapsed navigation preferences");
        shell.Preferences.NavCollapsed = false;
        shell.Preferences.ShowPageHeader = false;
        Check(shell.EffectivePageHeader is null, "The shell did not apply the page header preference");
        shell.Preferences.ShowPageHeader = true;
        shell.Preferences.FontScale = 1.6;
        host.Settle();
        Check(FontScaleManager.Current == 1.6 && Descendants<ScrollViewer>(updates).All(viewer => viewer.ExtentWidth <= viewer.ViewportWidth + 1), "Font scaling caused horizontal overflow in the component list");
        shell.Navigate(SectionKey.DotNet);
        host.Settle();
        var scaledDotNet = Descendants<DotNetView>(window).Single();
        Check(Descendants<ScrollViewer>(scaledDotNet).All(viewer => viewer.ExtentWidth <= viewer.ViewportWidth + 1 && viewer.ScrollableWidth <= 1),
            "Font scaling caused horizontal overflow in the .NET manager");
        Render((FrameworkElement)window.Content, Path.Combine(output, "shell-dotnet-1000-160.png"), 1000, 700);
        shell.Navigate(SectionKey.Updates);
        shell.Preferences.FontScale = 1;
        host.Settle();

        var confirmationModel = new MessageDialogViewModel("Удаление компонента", "Удалить выбранный компонент?", true, "Удалить");
        var confirmation = shell.Modal.ShowAsync(confirmationModel);
        host.Settle();
        Check(!confirmation.IsCompleted && Descendants<MessageDialogView>(window).Any(view => view.IsVisible), "Confirmation did not use the shell modal host");
        Check(!shell.Navigate(SectionKey.Logs), "Navigation escaped an active modal");
        Render((FrameworkElement)window.Content, Path.Combine(output, "shell-confirmation.png"), 1000, 700);
        confirmationModel.TryAccept();
        Check(!AwaitResult(confirmation), "Enter accepted deletion instead of the safe default");
        var accepted = shell.Modal.ShowAsync(confirmationModel);
        confirmationModel.AcceptCommand.Execute(null);
        Check(AwaitResult(accepted), "Explicit confirmation did not complete the awaiting operation");
        host.Settle();
        Check(Application.Current.Windows.Count == 1, "A page or confirmation opened an additional window");
        CheckAgentSettingsMigration(output);
        Console.WriteLine("PASS: real ShellLayout, generated page pairs, navigation retention, singleton state and in-shell confirmation");
    }

    private static void CheckChangelogOverflow(VisualTestHost host, MainWindow window, ShellViewModel shell, string output, int width)
    {
        var component = shell.Updates.Stages[0];
        var original = (component.CheckResult, component.InstalledVersion, component.AvailableVersion, component.Info);
        component.SetCheckResult("AVAILABLE", "26.1103.7773.0 (Microsoft Store, very long installed build)", "26.1104.8888.0");
        shell.Changelog.RenderDocument(new AIUpdater.Models.ChangelogDocument
        {
            StageId = component.Id,
            Releases = [new()
            {
                Version = "26.1103.7773.0",
                Title = "ChatGPT / Codex App 26.1103 (Microsoft Store 26.1103.7773.0), extended release description",
                Groups = [new() { Category = AIUpdater.Models.ChangeCategory.Changed, Title = "Изменено и улучшено", Items = ["Установлен пакет Microsoft Store `OpenAI.Codex : v26.1103.7773.0`, подробности этого выпуска доступны в официальном источнике."] }],
            }],
        }, component);
        var view = Descendants<ChangelogView>(window).Single();
        foreach (var scale in new[] { 1.0, 1.6 })
        {
            shell.Preferences.FontScale = scale;
            host.Settle();
            var list = (ListBox)view.FindName("ComponentList");
            Check(Descendants<ScrollViewer>(list).All(scroll => scroll.ScrollableWidth <= 1 && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed), "Long installed versions caused horizontal scrolling in the grouped component list");
            var releases = (ScrollViewer)view.FindName("ReleasesScrollViewer");
            Check(releases.ScrollableWidth <= 1, "Release cards overflow their viewport");
            Render((FrameworkElement)window.Content, Path.Combine(output, $"changelog-overflow-{width}-{(int)(scale * 100)}.png"), width, 700);
        }
        shell.Preferences.FontScale = 1;
        component.SetCheckResult(original.CheckResult ?? "UNKNOWN", original.InstalledVersion, original.AvailableVersion, info: original.Info);
        host.Settle();
    }

    private static void CheckAgentSettingsMigration(string output)
    {
        var paths = new AppPaths(Path.Combine(output, "legacy-agent-migration", Guid.NewGuid().ToString("N")), AppContext.BaseDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        var legacy = new TranslationAgentSelection { BackendId = "codex", Automatic = false,
            Profiles = new() { ["codex"] = new() { Model = "legacy-model", Effort = "high", ExecutablePath = @"C:\Agent Tools\codex.cmd" } } };
        var original = JsonSerializer.Serialize(legacy);
        File.WriteAllText(paths.LegacyAgentSettingsFile, original);
        ISettingsStore firstStore = new SettingsStore(paths.SettingsFile);
        var migrated = new TranslationAgentSettings(firstStore, paths);
        Check(migrated.Current.BackendId == "codex" && migrated.Current.Profile.Model == "legacy-model" && !migrated.Current.Automatic, "Legacy agent selection did not migrate into the shared store");
        migrated.Save(migrated.Current with { Automatic = true });
        firstStore.Close();
        ISettingsStore reopened = new SettingsStore(paths.SettingsFile);
        var loaded = new TranslationAgentSettings(reopened, paths);
        Check(loaded.Current.Automatic && loaded.Current.Profile.Effort == "high", "Reopening reapplied the old JSON over saved settings");
        Check(File.ReadAllText(paths.LegacyAgentSettingsFile) == original, "Migration changed the rollback copy of the old JSON");
        reopened.Close();
    }
}

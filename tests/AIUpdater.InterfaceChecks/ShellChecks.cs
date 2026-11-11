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

    private static void CheckOperationNavigation(VisualTestHost host, MainWindow window, ShellViewModel shell)
    {
        Check(shell.IsBusy, "The operation fixture did not start");
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

    private static void CheckShellWindow(VisualTestHost host, MainWindow window, ShellViewModel shell, string output)
    {
        Check(window.Content is KeepShell.Views.ShellLayout { ModalCoversShell: true }, "The application must use the real ShellLayout, including its modal surface");
        Check(shell is ShellViewModelBase && shell.Updates is IPageHeader && shell.Updates is IPageStatus && shell.Updates is IPageRefresh,
            "The shell and update page do not expose the framework contracts");
        Check(typeof(IView<MainViewModel>).IsAssignableFrom(typeof(UpdatesView))
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
            Check(Descendants<UniformGrid>(updates).All(panel => panel.Columns == (width == 1200 ? 4 : 3)), "Card columns did not adapt to the shell content width");
            var widths = cards.Select(card => card.ActualWidth).ToArray();
            Check(widths.Max() - widths.Min() < 1, "Vendor card widths differ");
            foreach (var row in cards.GroupBy(card => Math.Round(card.TranslatePoint(new(), updates).Y)))
            {
                var positions = row.Select(card => ((TextBlock)card.FindName("StageTitle")).TranslatePoint(new(), updates).Y).ToArray();
                Check(positions.Max() - positions.Min() < 1, "Card titles are not aligned");
            }
            var menuButton = Descendants<Button>(cards[0]).First(button => Equals(button.Content, "⋮"));
            menuButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var cardMenu = ((Border)cards[0].FindName("CardBorder")).ContextMenu;
            Check(cardMenu.DataContext is StageCardViewModel && cardMenu.Items.OfType<MenuItem>().Count() == 5, "Card actions lost their shared context");
            cardMenu.IsOpen = false;
            var directChoices = Descendants<CheckBox>(updates).Where(box => box.DataContext is AIUpdater.Models.UpdateStage { PackageStatus: AIUpdater.Models.PackageStatus.DirectUpdate }).ToArray();
            Check(directChoices.Length == 2 && directChoices.All(box => box.IsChecked == false), "Direct updates became selected automatically");
            Render((FrameworkElement)window.Content, Path.Combine(output, $"shell-updates-{width}.png"), width, height);

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
        Check(FontScaleManager.Current == 1.6 && Descendants<UniformGrid>(updates).All(panel => panel.Columns == 1), "Font scaling did not adjust the card layout");
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

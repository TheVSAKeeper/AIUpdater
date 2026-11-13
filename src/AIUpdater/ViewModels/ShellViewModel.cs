using AIUpdater.Bootstrap;
using AIUpdater.Models;
using AIUpdater.Services;
using KeepShell.Diagnostics;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;

namespace AIUpdater.ViewModels;

public sealed class ShellViewModel : ShellViewModelBase, IAppNavigator, IDisposable
{
    private readonly Dictionary<string, NavigationItem> _pages;
    private readonly NavigationItem _settingsItem;
    private readonly IUpdaterDialogs _dialogs;
    private readonly PerformanceMonitor _monitor;
    private readonly ILogger _logger;

    public ShellViewModel(MainViewModel updates, DotNetViewModel dotnet, ChangelogViewModel changelog,
        LogsViewModel appLogs, DiagnosticsViewModel diagnostics, PerformanceMonitor monitor, SettingsViewModel settings,
        ModalHostViewModel modal, ToastHostViewModel toasts, ShellPreferences preferences,
        AppNavigator navigator, IUpdaterDialogs dialogs, ILogger<ShellViewModel>? logger = null) : base(modal)
    {
        Updates = updates;
        DotNet = dotnet;
        Changelog = changelog;
        AppLogs = appLogs;
        Diagnostics = diagnostics;
        SettingsPage = settings;
        Toasts = toasts;
        Preferences = preferences;
        _monitor = monitor;
        _dialogs = dialogs;
        _logger = logger ?? NullLogger<ShellViewModel>.Instance;
        var updatesItem = new NavigationItem("Обновления", PackIconLucideKind.RefreshCw, updates, key: SectionKey.Updates) { KeepAlive = true };
        var dotnetItem = new NavigationItem(".NET", PackIconLucideKind.Layers, dotnet, key: SectionKey.DotNet) { KeepAlive = true };
        var changelogItem = new NavigationItem("Что нового", PackIconLucideKind.Newspaper, changelog, key: SectionKey.Changelog) { KeepAlive = true };
        var logsItem = new NavigationItem("Журнал", PackIconLucideKind.Terminal, updates.LogPanel, key: SectionKey.Logs) { KeepAlive = true, StartsGroup = true };
        var appLogsItem = new NavigationItem("Логи", PackIconLucideKind.ScrollText, appLogs, key: SectionKey.AppLogs) { KeepAlive = true };
        var diagnosticsItem = new NavigationItem("Диагностика", PackIconLucideKind.Gauge, diagnostics, key: SectionKey.Diagnostics);
        _settingsItem = new("Настройки", PackIconLucideKind.Settings, settings, key: SectionKey.Settings) { KeepAlive = true };
        foreach (var item in new[] { updatesItem, dotnetItem, changelogItem, logsItem, appLogsItem, diagnosticsItem }) Sections.Add(item);
        _pages = new[] { updatesItem, dotnetItem, changelogItem, logsItem, appLogsItem, diagnosticsItem, _settingsItem }.ToDictionary(item => item.Key, StringComparer.Ordinal);
        navigator.Attach(this);
        IsNavCollapsed = Preferences.NavCollapsed;
        Preferences.PropertyChanged += OnPreferencesChanged;
        Updates.PropertyChanged += OnUpdatesChanged;
        Selected = _pages.GetValueOrDefault(Preferences.LastPage) ?? updatesItem;
    }

    public MainViewModel Updates { get; }
    public DotNetViewModel DotNet { get; }
    public ChangelogViewModel Changelog { get; }
    public LogsViewModel AppLogs { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public SettingsViewModel SettingsPage { get; }
    public ToastHostViewModel Toasts { get; }
    public ShellPreferences Preferences { get; }
    public bool IsBusy => Updates.IsRunning;
    public string OperationCaption => Updates.StatusCaption;
    public override IPageHeader? EffectivePageHeader => Preferences.ShowPageHeader ? CurrentPageHeader : null;

    public bool Navigate(string key)
    {
        if (Modal.HasActive)
        {
            _logger.ShellNavigationBlockedByModal(key);
            return false;
        }
        if (!_pages.TryGetValue(key, out var page))
        {
            _logger.ShellNavigationUnknownKey(key);
            return false;
        }
        Selected = page;
        _logger.ShellNavigated(key);
        return true;
    }

    public void ShowDotNet() => Navigate(SectionKey.DotNet);

    public void ShowChangelog(UpdateStage? stage = null)
    {
        if (stage is not null) Changelog.SelectStage(stage);
        Navigate(SectionKey.Changelog);
    }

    public void ShowSettings()
    {
        SettingsPage.ShowTranslation();
        Navigate(SectionKey.Settings);
    }

    protected override void NavigateToSettings() => Navigate(SectionKey.Settings);
    protected override void OnSelectionChanged(NavigationItem? value)
    {
        if (value is not null) Preferences.LastPage = value.Key;
    }
    protected override void OnNavCollapsedChanged(bool value) => Preferences.NavCollapsed = value;

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellPreferences.NavCollapsed)) IsNavCollapsed = Preferences.NavCollapsed;
        OnPropertyChanged(nameof(EffectivePageHeader));
        OnPropertyChanged(nameof(ContentMargin));
    }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRunning))
        {
            _monitor.SetPhase(Updates.IsRunning ? "обновление" : null);
            OnPropertyChanged(nameof(IsBusy));
        }
        if (e.PropertyName == nameof(MainViewModel.StatusCaption)) OnPropertyChanged(nameof(OperationCaption));
    }

    public override async Task<bool> RequestCloseAsync()
    {
        if (!Updates.IsRunning) return true;
        var confirmed = await _dialogs.ConfirmAsync("Остановить текущую операцию и закрыть AI Updater?", "Закрытие приложения", "Остановить");
        _logger.ShellCloseConfirmedWhileBusy(confirmed);
        if (!confirmed) return false;
        await Updates.StopAsync();
        return true;
    }

    public void Dispose()
    {
        Preferences.PropertyChanged -= OnPreferencesChanged;
        Updates.PropertyChanged -= OnUpdatesChanged;
    }
}

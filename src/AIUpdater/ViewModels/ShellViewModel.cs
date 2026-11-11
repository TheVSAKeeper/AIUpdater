using AIUpdater.Bootstrap;
using AIUpdater.Models;
using AIUpdater.Services;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using System.ComponentModel;

namespace AIUpdater.ViewModels;

public sealed class ShellViewModel : ShellViewModelBase, IAppNavigator, IDisposable
{
    private readonly Dictionary<string, NavigationItem> _pages;
    private readonly NavigationItem _settingsItem;
    private readonly IUpdaterDialogs _dialogs;

    public ShellViewModel(MainViewModel updates, ChangelogViewModel changelog, SettingsViewModel settings,
        ModalHostViewModel modal, ToastHostViewModel toasts, ShellPreferences preferences,
        AppNavigator navigator, IUpdaterDialogs dialogs) : base(modal)
    {
        Updates = updates;
        Changelog = changelog;
        SettingsPage = settings;
        Toasts = toasts;
        Preferences = preferences;
        _dialogs = dialogs;
        var updatesItem = new NavigationItem("Обновления", PackIconLucideKind.RefreshCw, updates, key: SectionKey.Updates) { KeepAlive = true };
        var changelogItem = new NavigationItem("Что нового", PackIconLucideKind.Newspaper, changelog, key: SectionKey.Changelog) { KeepAlive = true };
        var logsItem = new NavigationItem("Журнал", PackIconLucideKind.ScrollText, updates.LogPanel, key: SectionKey.Logs) { KeepAlive = true, StartsGroup = true };
        _settingsItem = new("Настройки", PackIconLucideKind.Settings, settings, key: SectionKey.Settings) { KeepAlive = true };
        foreach (var item in new[] { updatesItem, changelogItem, logsItem }) Sections.Add(item);
        _pages = new[] { updatesItem, changelogItem, logsItem, _settingsItem }.ToDictionary(item => item.Key, StringComparer.Ordinal);
        navigator.Attach(this);
        IsNavCollapsed = Preferences.NavCollapsed;
        Preferences.PropertyChanged += OnPreferencesChanged;
        Updates.PropertyChanged += OnUpdatesChanged;
        Selected = _pages.GetValueOrDefault(Preferences.LastPage) ?? updatesItem;
    }

    public MainViewModel Updates { get; }
    public ChangelogViewModel Changelog { get; }
    public SettingsViewModel SettingsPage { get; }
    public ToastHostViewModel Toasts { get; }
    public ShellPreferences Preferences { get; }
    public bool IsBusy => Updates.IsRunning;
    public string OperationCaption => Updates.OverallStatusText;
    public override IPageHeader? EffectivePageHeader => Preferences.ShowPageHeader ? CurrentPageHeader : null;

    public bool Navigate(string key)
    {
        if (Modal.HasActive) return false;
        if (!_pages.TryGetValue(key, out var page)) return false;
        Selected = page;
        return true;
    }

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
        if (e.PropertyName == nameof(MainViewModel.IsRunning)) OnPropertyChanged(nameof(IsBusy));
        if (e.PropertyName == nameof(MainViewModel.OverallStatusText)) OnPropertyChanged(nameof(OperationCaption));
    }

    public override async Task<bool> RequestCloseAsync()
    {
        if (!Updates.IsRunning) return true;
        if (!await _dialogs.ConfirmAsync("Остановить текущую операцию и закрыть AI Updater?", "Закрытие приложения", "Остановить")) return false;
        await Updates.StopAsync();
        return true;
    }

    public void Dispose()
    {
        Preferences.PropertyChanged -= OnPreferencesChanged;
        Updates.PropertyChanged -= OnUpdatesChanged;
    }
}

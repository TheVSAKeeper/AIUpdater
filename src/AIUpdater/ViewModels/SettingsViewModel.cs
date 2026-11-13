using AIUpdater.Bootstrap;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Bootstrap;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUpdater.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IPageHeader
{
    public const string AppearanceKey = "appearance";
    public const string TranslationKey = "translation";
    private readonly ILogger _logger;

    public SettingsViewModel(
        ShellPreferences preferences,
        TranslationAgentViewModel agent,
        ISettingsStore settings,
        ILogger<SettingsViewModel>? logger = null)
    {
        Preferences = preferences;
        Agent = agent;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        Sections = new(new(AppearanceKey, "Внешний вид", PackIconLucideKind.Palette, "масштаб шрифт навигация"),
            new(TranslationKey, "Перевод", PackIconLucideKind.Languages, "агент модель CLI рассуждения"));
        Sections.Restore(settings.GetStringValue(SettingsKeys.SettingsSection));
        Sections.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsSectionList.SelectedPath)) settings.SetValue(SettingsKeys.SettingsSection, Sections.SelectedPath);
        };
        DiscardAgentChangesCommand = new RelayCommand(Agent.Reset);
        ResetAppearanceCommand = new RelayCommand(() =>
        {
            Preferences.FontScale = FontScaleManager.DefaultScale;
            Preferences.ShowPageHeader = true;
            Preferences.NavCollapsed = false;
            _logger.AppearanceResetToDefaults();
        });
    }

    public string PageTitle => "Настройки";
    public string PageDescription => "Внешний вид и обработка истории изменений";
    public ShellPreferences Preferences { get; }
    public TranslationAgentViewModel Agent { get; }
    public SettingsSectionList Sections { get; }
    public IRelayCommand DiscardAgentChangesCommand { get; }
    public IRelayCommand ResetAppearanceCommand { get; }
    public void ShowTranslation() => Sections.Restore(TranslationKey);
}

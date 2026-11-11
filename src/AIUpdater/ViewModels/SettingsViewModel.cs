using AIUpdater.Bootstrap;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Bootstrap;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;

namespace AIUpdater.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IPageHeader
{
    public const string AppearanceKey = "appearance";
    public const string TranslationKey = "translation";

    public SettingsViewModel(ShellPreferences preferences, TranslationAgentViewModel agent, ISettingsStore settings)
    {
        Preferences = preferences;
        Agent = agent;
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

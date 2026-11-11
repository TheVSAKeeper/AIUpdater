using AIUpdater.Bootstrap;
using KeepShell.Bootstrap;
using KeepShell.ViewModels;

namespace AIUpdater.ViewModels;

public sealed class ShellPreferences : ShellPreferencesBase
{
    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(SettingsKeys.ShowPageHeader, SettingsKeys.EnableToasts, SettingsKeys.FontScale))
    {
        _navCollapsed = settings.GetBool(SettingsKeys.NavCollapsed);
    }

    private bool _navCollapsed;
    public bool NavCollapsed
    {
        get => _navCollapsed;
        set { if (SetProperty(ref _navCollapsed, value)) PersistBool(SettingsKeys.NavCollapsed, value); }
    }

    public string LastPage
    {
        get => Settings.GetStringValue(SettingsKeys.LastPage) ?? SectionKey.Updates;
        set => Settings.SetValue(SettingsKeys.LastPage, value);
    }
}

using KeepShell.Bootstrap;

namespace AIUpdater.Bootstrap;

internal static class AppThemes
{
    internal const string Key = "aiupdater-dark";

    internal static void Initialize(ISettingsStore? settings = null)
    {
        ThemeManager.Register(new ThemeDefinition(Key,
            new Uri("pack://application:,,,/AIUpdater;component/Resources/Themes/DarkGreen.xaml"),
            new Uri("pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml"),
            IsDark: true, SuppressTransition: true));
        ThemeManager.Apply(Key);
        FontScaleManager.Initialize(settings?.GetDouble(SettingsKeys.FontScale, FontScaleManager.DefaultScale) ?? FontScaleManager.DefaultScale);
        ViewLocator.InstallIntoApplication();
    }
}

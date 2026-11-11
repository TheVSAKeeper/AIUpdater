using KeepShell.Bootstrap;

namespace AIUpdater.Infrastructure;

internal static class AppTheme
{
    internal const string Key = "aiupdater-dark";

    internal static void Initialize()
    {
        ThemeManager.Register(new ThemeDefinition(Key,
            new Uri("pack://application:,,,/AIUpdater;component/Resources/Themes/DarkGreen.xaml"),
            new Uri("pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml"),
            IsDark: true, SuppressTransition: true));
        ThemeManager.Apply(Key);
        FontScaleManager.Initialize(FontScaleManager.DefaultScale);
        ViewLocator.InstallIntoApplication();
    }
}

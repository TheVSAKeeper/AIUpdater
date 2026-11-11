using AIUpdater.Services;

namespace AIUpdater;

public partial class App
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services => _services ??= ServiceCollectionExtensions.CreateDefaultServiceProvider();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        Infrastructure.AppTheme.Initialize();
        MainWindow = new Views.MainWindow();
        MainWindow.Show();
    }
}

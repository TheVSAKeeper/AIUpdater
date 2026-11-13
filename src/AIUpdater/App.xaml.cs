using AIUpdater.Bootstrap;
using AIUpdater.Services;
using AIUpdater.Views;
using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AIUpdater;

public partial class App
{
    private ServiceProvider? _services;
    private ISettingsStore? _settings;
    private KeepShellLogging? _logging;
    private ILogger<App>? _logger;
    private SingleInstanceGuard? _instance;

    public static ServiceProvider ConfigureServices(ISettingsStore settings, KeepShellLogging logging, AppPaths paths, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddAIUpdater(settings, logging, paths);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var startedAt = Stopwatch.GetTimestamp();
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (e.Args.FirstOrDefault() == "--gallery")
            {
                Shutdown(await Automation.GalleryHost.RunAsync(e.Args.Skip(1)));
                return;
            }
            var paths = AppPaths.Default;
            _logging = KeepShellLogging.Bootstrap(new KeepShellLoggingOptions
            {
                LogsDirectory = Path.Combine(paths.LogsDirectory, "Diagnostics"), FileNamePrefix = "app-",
                FileSizeLimitBytes = 5 * 1024 * 1024, RetainedFileCountLimit = 5,
            });
            _logger = _logging.LoggerFactory.CreateLogger<App>();
            KeepShell.Services.StyledMessageBox.DefaultTitle = "AI Updater";
            new GlobalExceptionHandlers(() => _services?.GetService<KeepShell.Services.ErrorReportService>()).Attach(this);
            _instance = SingleInstanceGuard.Acquire("AIUpdater");
            if (!_instance.IsPrimaryInstance)
            {
                _logger.SecondaryInstanceDetected();
                Shutdown();
                return;
            }
            _settings = new SettingsStore(paths.SettingsFile);
            AppThemes.Initialize(_settings);
            _services = ConfigureServices(_settings, _logging, paths);
            _settings.WriteFailed += OnSettingsWriteFailed;
            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            var startupElapsed = Stopwatch.GetElapsedTime(startedAt);
            _services.GetRequiredService<PerformanceMonitor>().ReportStartup(startupElapsed);
            _services.GetRequiredService<DiagnosticsCollector>().CaptureMachine(window);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _logger.AppStarted((long)startupElapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            _logger?.AppStartupFailed(exception);
            if (e.Args.FirstOrDefault() == "--gallery")
            {
                Console.Error.WriteLine(exception);
                Shutdown(1);
                return;
            }
            KeepShell.Services.StyledMessageBox.Show(exception.Message, "Не удалось запустить AI Updater", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
            Shutdown(1);
        }
    }

    private void OnSettingsWriteFailed(object? sender, SettingsWriteFailedEventArgs e)
    {
        _logger?.SettingsWriteToastShown();
        _services?.GetRequiredService<IUiDispatcher>().Invoke(() =>
            _services.GetRequiredService<ToastHostViewModel>().Show("Не удалось сохранить настройки. Подробности записаны в журнал приложения.", StatusSeverity.Error, TimeSpan.FromSeconds(12)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.AppExiting(e.ApplicationExitCode);
        _settings?.Close();
        _services?.Dispose();
        _logging?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

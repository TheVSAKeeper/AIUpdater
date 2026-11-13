using AIUpdater.Bootstrap;
using AIUpdater.Services.Parsers;
using AIUpdater.ViewModels;
using AIUpdater.Views;
using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Services;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace AIUpdater.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIUpdater(this IServiceCollection services, ISettingsStore settings, KeepShellLogging logging, AppPaths paths)
    {
        services.AddSingleton(settings);
        services.AddSingleton(paths);
        services.AddKeepShellLogging(logging);
        services.AddKeepShell();
        services.AddKeepShellToasts();
        services.AddSingleton(new ErrorReportOptions
        {
            IssueRepo = static () => "TheVSAKeeper/AIUpdater",
            LogFileGlobs = ["app-*.log", "log-*.log"],
            SessionStartMarker = "Оболочка AI Updater запущена",
        });
        services.AddSingleton<ErrorReportService>();
        services.AddKeepShellDiagnostics(new DiagnosticsOptions
        {
            AppName = "AIUpdater",
            AppVersion = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "неизвестна",
            DataDirectory = paths.DataDirectory,
        });
        services.AddSingleton<IChangelogFormatParser, GitHubReleasesChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AtomChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AgyCliChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, DotNetReleasesChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, NpmRegistryChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, CodexAppChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AntigravityDesktopChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, HtmlChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, MarkdownChangelogParser>();
        services.AddSingleton<IChangelogRepository>(provider => new FileChangelogRepository(
            paths.ChangelogDirectory,
            provider.GetRequiredService<ILogger<FileChangelogRepository>>()));
        services.AddSingleton<IChangelogContentFetcher, ChangelogContentFetcher>();
        services.AddSingleton<TranslationAgentSettings>();
        services.AddSingleton<TranslationAgentViewModel>();
        services.AddSingleton<IChangelogTranslator, AgentChangelogTranslator>();
        services.AddSingleton<IChangelogService, ChangelogService>();
        services.AddSingleton<IStageResultStore>(provider => new FileStageResultStore(
            paths.ResultsFile,
            provider.GetRequiredService<ILogger<FileStageResultStore>>()));
        services.AddSingleton<IUpdaterProcessRunner, PowerShellProcessRunner>();
        services.AddSingleton<UpdaterOutputParser>();
        services.AddSingleton<IUpdaterExecutionService, UpdaterExecutionService>();
        services.AddSingleton<AppNavigator>();
        services.AddSingleton<IAppNavigator>(provider => provider.GetRequiredService<AppNavigator>());
        services.AddSingleton<IUpdaterDialogs, UpdaterDialogs>();
        services.AddSingleton<IDotNetPackageService>(provider => new DotNetPackageService(
            provider.GetRequiredService<IChangelogContentFetcher>(),
            provider.GetRequiredService<AppPaths>(),
            logger: provider.GetRequiredService<ILogger<DotNetPackageService>>()));
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DotNetViewModel>();
        services.AddSingleton<LogPanelViewModel>(provider => provider.GetRequiredService<MainViewModel>().LogPanel);
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<ChangelogViewModel>();
        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}

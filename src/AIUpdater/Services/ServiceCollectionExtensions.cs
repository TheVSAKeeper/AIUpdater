using AIUpdater.Bootstrap;
using AIUpdater.Services.Parsers;
using AIUpdater.ViewModels;
using AIUpdater.Views;
using KeepShell.Bootstrap;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IChangelogFormatParser, GitHubReleasesChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AtomChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AgyCliChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, DotNetReleasesChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, NpmRegistryChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, CodexAppChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AntigravityDesktopChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, HtmlChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, MarkdownChangelogParser>();
        services.AddSingleton<IChangelogRepository>(new FileChangelogRepository(paths.ChangelogDirectory));
        services.AddSingleton<IChangelogContentFetcher, ChangelogContentFetcher>();
        services.AddSingleton<TranslationAgentSettings>();
        services.AddSingleton<TranslationAgentViewModel>();
        services.AddSingleton<IChangelogTranslator, AgentChangelogTranslator>();
        services.AddSingleton<IChangelogService, ChangelogService>();
        services.AddSingleton<IStageResultStore>(new FileStageResultStore(paths.ResultsFile));
        services.AddSingleton<IUpdaterProcessRunner, PowerShellProcessRunner>();
        services.AddSingleton<UpdaterOutputParser>();
        services.AddSingleton<IUpdaterExecutionService, UpdaterExecutionService>();
        services.AddSingleton<AppNavigator>();
        services.AddSingleton<IAppNavigator>(provider => provider.GetRequiredService<AppNavigator>());
        services.AddSingleton<IUpdaterDialogs, UpdaterDialogs>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LogPanelViewModel>(provider => provider.GetRequiredService<MainViewModel>().LogPanel);
        services.AddSingleton<ChangelogViewModel>();
        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}

using AIUpdater.Models;
using AIUpdater.Services.Parsers;
using AIUpdater.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AIUpdater.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIUpdater(this IServiceCollection services)
    {
        services.AddSingleton<IChangelogFormatParser, GitHubReleasesChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AtomChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, AgyCliChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, DotNetReleasesChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, NpmRegistryChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, CodexAppChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, HtmlChangelogParser>();
        services.AddSingleton<IChangelogFormatParser, MarkdownChangelogParser>();

        services.AddSingleton<IChangelogRepository, FileChangelogRepository>();
        services.AddSingleton<IChangelogContentFetcher, ChangelogContentFetcher>();
        services.AddSingleton<IChangelogTranslator, AgyChangelogTranslator>();
        services.AddSingleton<IChangelogService, ChangelogService>();

        services.AddSingleton<IStageResultStore, FileStageResultStore>();
        services.AddSingleton<IUpdaterProcessRunner, PowerShellProcessRunner>();
        services.AddSingleton<UpdaterOutputParser>();
        services.AddSingleton<IUpdaterExecutionService, UpdaterExecutionService>();
        services.AddSingleton<IDialogService, WpfDialogService>();

        services.AddTransient<MainViewModel>();
        services.AddTransient(sp => new ChangelogViewModel(
            UpdateStage.CreateCatalog(),
            null,
            sp.GetRequiredService<IChangelogService>(),
            sp.GetRequiredService<IDialogService>()));

        return services;
    }

    public static ServiceProvider CreateDefaultServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddAIUpdater();
        return services.BuildServiceProvider();
    }
}

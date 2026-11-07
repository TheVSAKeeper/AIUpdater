using AIUpdater.Services;

namespace AIUpdater;

public partial class App
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services => _services ??= ServiceCollectionExtensions.CreateDefaultServiceProvider();
}

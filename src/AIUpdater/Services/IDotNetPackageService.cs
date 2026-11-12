using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IDotNetPackageService
{
    Task<DotNetCatalogSnapshot> LoadCatalogAsync(
        bool forceRefresh,
        bool includePrerelease,
        CancellationToken cancellationToken);
}

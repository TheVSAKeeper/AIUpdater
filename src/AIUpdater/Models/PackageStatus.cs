namespace AIUpdater.Models;

public enum PackageStatus
{
    None = 0,
    NotInstalled = 1,
    UpToDate = 2,
    UpdateAvailable = 3,
    Unknown = 4,
    DirectUpdate = 5,
    CheckFailed = 6,
}

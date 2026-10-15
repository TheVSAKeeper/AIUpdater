namespace AIUpdater.Models;

public enum OperationState
{
    None = 0,
    Queued = 1,
    Checking = 2,
    Installing = 3,
    Updating = 4,
    Uninstalling = 5,
    Failed = 6,
    Stopped = 7,
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AIUpdater;

public enum PackageStatus
{
    None = 0,
    NotInstalled = 1,
    UpToDate = 2,
    UpdateAvailable = 3,
    Unknown = 4,
    DirectUpdate = 5,
    CheckFailed = 6
}

public enum OperationState
{
    None = 0,
    Queued = 1,
    Checking = 2,
    Installing = 3,
    Updating = 4,
    Uninstalling = 5,
    Failed = 6,
    Stopped = 7
}

public sealed class UpdateStage : INotifyPropertyChanged
{
    private bool _isSelectedForUpdate;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Vendor { get; init; }
    public PackageStatus PackageStatus { get; private set; }
    public OperationState OperationState { get; private set; }
    public string? InstalledVersion { get; private set; }
    public string? AvailableVersion { get; private set; }
    public DateTimeOffset? CheckedAt { get; private set; }
    public string? Info { get; private set; }
    public bool HasUpdate => PackageStatus == PackageStatus.UpdateAvailable;
    public bool CanSelectForUpdate => HasUpdate || PackageStatus == PackageStatus.DirectUpdate;
    public bool CanUpdate => PackageStatus is PackageStatus.UpToDate or PackageStatus.UpdateAvailable or PackageStatus.Unknown or PackageStatus.DirectUpdate or PackageStatus.CheckFailed;
    public bool CanManageInstallation => !Id.EndsWith("plugins", StringComparison.Ordinal);
    public string? CheckResult => PackageStatus switch
    {
        PackageStatus.NotInstalled => "NOT_INSTALLED",
        PackageStatus.UpToDate => "CURRENT",
        PackageStatus.UpdateAvailable => "AVAILABLE",
        PackageStatus.Unknown => "UNKNOWN",
        PackageStatus.DirectUpdate => "DIRECT",
        PackageStatus.CheckFailed => "ERROR",
        _ => null
    };

    public bool IsSelectedForUpdate
    {
        get => _isSelectedForUpdate;
        set
        {
            if (_isSelectedForUpdate == value) return;
            _isSelectedForUpdate = value;
            Notify();
        }
    }

    public string VersionDisplayText => InstalledVersion is null
        ? AvailableVersion is null ? PackageStatus == PackageStatus.DirectUpdate ? Info ?? "" : "" : $"Доступна v{AvailableVersion}"
        : HasUpdate && AvailableVersion is not null
            ? $"v{InstalledVersion} → v{AvailableVersion}"
            : $"v{InstalledVersion}";

    public string VersionPrefixText => HasUpdate && InstalledVersion is not null && AvailableVersion is not null
        ? $"v{InstalledVersion} → " : VersionDisplayText;
    public string VersionTargetText => HasUpdate && InstalledVersion is not null && AvailableVersion is not null
        ? $"v{AvailableVersion}" : "";
    public Brush VersionBrush => PackageStatus == PackageStatus.UpToDate ? Brushes.MediumAquamarine : Brushes.LightSlateGray;
    public Brush VersionTargetBrush => HasUpdate ? Brushes.Gold : VersionBrush;
    public string CardActionName => CanSelectForUpdate ? $"Выбрать обновление: {Name}" : $"Проверить: {Name}";

    public string ResultDescription => CheckedAt is null ? "Проверка ещё не выполнялась"
        : $"Последняя проверка: {CheckedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}\n{VersionDisplayText}"
            + (string.IsNullOrWhiteSpace(Info) || Info == VersionDisplayText ? "" : $"\n{Info}");

    public string Status => OperationState switch
    {
        OperationState.Queued => "В очереди",
        OperationState.Checking => "Проверка…",
        OperationState.Installing => "Установка…",
        OperationState.Updating => "Обновление…",
        OperationState.Uninstalling => "Удаление…",
        OperationState.Failed => "Ошибка выполнения",
        OperationState.Stopped => "Остановлено",
        _ => PackageStatus switch
        {
            PackageStatus.NotInstalled => "Не установлено",
            PackageStatus.UpToDate => "Актуально",
            PackageStatus.UpdateAvailable => "Есть обновление",
            PackageStatus.Unknown => "Не удалось сравнить",
            PackageStatus.DirectUpdate => "Обновляется напрямую",
            PackageStatus.CheckFailed => "Ошибка проверки",
            _ => "Не проверено"
        }
    };

    public Brush StatusBrush => OperationState switch
    {
        OperationState.Failed => Brushes.LightCoral,
        OperationState.Checking or OperationState.Installing or OperationState.Updating or OperationState.Uninstalling => Brushes.Aquamarine,
        OperationState.Queued or OperationState.Stopped => Brushes.LightSlateGray,
        _ => PackageStatus switch
        {
            PackageStatus.UpdateAvailable => Brushes.Gold,
            PackageStatus.UpToDate => Brushes.MediumAquamarine,
            PackageStatus.CheckFailed => Brushes.LightCoral,
            _ => Brushes.LightSlateGray
        }
    };
    public Brush IndicatorBrush => StatusBrush;

    public void Queue()
    {
        OperationState = OperationState.Queued;
        Refresh();
    }

    public void SetRunning(OperationState state)
    {
        OperationState = state;
        Refresh();
    }

    public void SetCompleted()
    {
        OperationState = OperationState.None;
        Refresh();
    }

    public void SetFailed(bool stopped = false)
    {
        OperationState = stopped ? OperationState.Stopped : OperationState.Failed;
        Refresh();
    }

    public void SetCheckResult(string result, string? installedVersion = null, string? availableVersion = null,
        DateTimeOffset? checkedAt = null, string? info = null)
    {
        PackageStatus = result switch
        {
            "AVAILABLE" => PackageStatus.UpdateAvailable,
            "CURRENT" => PackageStatus.UpToDate,
            "NOT_INSTALLED" => PackageStatus.NotInstalled,
            "DIRECT" => PackageStatus.DirectUpdate,
            "ERROR" => PackageStatus.CheckFailed,
            _ => PackageStatus.Unknown
        };
        InstalledVersion = installedVersion;
        AvailableVersion = availableVersion;
        CheckedAt = checkedAt ?? DateTimeOffset.Now;
        Info = info;
        IsSelectedForUpdate = HasUpdate;
        Refresh();
    }

    public void InvalidateResult()
    {
        PackageStatus = PackageStatus.None;
        InstalledVersion = null;
        AvailableVersion = null;
        CheckedAt = null;
        Info = null;
        IsSelectedForUpdate = false;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private void Refresh() => Notify("");
}

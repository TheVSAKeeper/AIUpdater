using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AIUpdater;

public sealed class UpdateStage : INotifyPropertyChanged
{
    private string _status = "Ожидание";
    private Brush _statusBrush = Brushes.Gray;
    private Brush _indicatorBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Vendor { get; init; }
    public string? CheckResult { get; private set; }
    public bool CanManageInstallation => !Id.EndsWith("plugins", StringComparison.Ordinal);

    public void SetInstallationResult(bool installed)
    {
        SetCompleted();
        CheckResult = installed ? null : "NOT_INSTALLED";
        Status = installed ? "Установлено" : "Удалено";
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public Brush IndicatorBrush
    {
        get => _indicatorBrush;
        private set => SetField(ref _indicatorBrush, value);
    }

    public void Reset()
    {
        CheckResult = null;
        Status = "Ожидание";
        StatusBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    }

    public void SetRunning()
    {
        Status = "Выполняется";
        StatusBrush = new SolidColorBrush(Color.FromRgb(110, 231, 183));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(110, 231, 183));
    }

    public void SetCompleted()
    {
        Status = "Готово";
        StatusBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
    }

    public void SetFailed()
    {
        Status = "Ошибка";
        StatusBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
    }

    public void SetCheckResult(string result)
    {
        CheckResult = result;
        Status = result switch
        {
            "AVAILABLE" => "Есть обновление",
            "CURRENT" => "Актуально",
            "NOT_INSTALLED" => "Не установлено",
            _ => "Не удалось сравнить"
        };
        StatusBrush = result switch
        {
            "AVAILABLE" => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            "CURRENT" => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
        };
        IndicatorBrush = StatusBrush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

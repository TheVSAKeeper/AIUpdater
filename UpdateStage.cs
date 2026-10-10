using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AIUpdater;

public sealed class UpdateStage : INotifyPropertyChanged
{
    private string _status = "Ожидание";
    private Brush _statusBrush = Brushes.Gray;
    private Brush _indicatorBrush = new SolidColorBrush(Color.FromRgb(191, 198, 207));

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Vendor { get; init; }

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
        Status = "Ожидание";
        StatusBrush = new SolidColorBrush(Color.FromRgb(103, 113, 126));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(191, 198, 207));
    }

    public void SetRunning()
    {
        Status = "Выполняется";
        StatusBrush = new SolidColorBrush(Color.FromRgb(37, 99, 176));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(37, 99, 176));
    }

    public void SetCompleted()
    {
        Status = "Готово";
        StatusBrush = new SolidColorBrush(Color.FromRgb(22, 118, 90));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(22, 118, 90));
    }

    public void SetFailed()
    {
        Status = "Ошибка";
        StatusBrush = new SolidColorBrush(Color.FromRgb(180, 45, 45));
        IndicatorBrush = new SolidColorBrush(Color.FromRgb(180, 45, 45));
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

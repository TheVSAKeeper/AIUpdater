using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AIUpdater;

public sealed class VendorStatus(string name, string logPath) : INotifyPropertyChanged
{
    private string _status = "Ожидание";
    private string _detail = "Готов к проверке обновлений";
    private string _log = "";
    private Brush _brush = Brushes.Gray;

    public string Name { get; } = name;
    public IReadOnlyList<UpdateStage> Stages { get; init; } = [];
    public string LogPath { get; } = logPath;
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string Detail { get => _detail; set { _detail = value; Notify(); } }
    public string Log { get => _log; set { _log = value; Notify(); } }
    public Brush StatusBrush { get => _brush; private set { _brush = value; Notify(); } }

    public void SetStatus(string status, string detail, Brush brush)
    {
        Status = status;
        Detail = detail;
        StatusBrush = brush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

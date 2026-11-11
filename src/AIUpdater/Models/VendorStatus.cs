using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace AIUpdater.Models;

public sealed class VendorStatus(string name, string logPath) : ObservableObject
{
    public string Name { get; } = name;
    public IReadOnlyList<UpdateStage> Stages { get; init; } = [];
    public IReadOnlyList<object> Cards { get; init; } = [];
    public string LogPath { get; internal set; } = logPath;

    public const string IdleDetailText = "Готов к проверке обновлений";

    public string Status
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    } = "";

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    public string Detail
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasActiveDetail));
            }
        }
    } = IdleDetailText;

    public bool HasActiveDetail =>
        !string.IsNullOrWhiteSpace(Detail)
        && !string.Equals(Detail, IdleDetailText, StringComparison.Ordinal);

    public string Log
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public Brush StatusBrush
    {
        get;
        private set => SetProperty(ref field, value);
    } = Brushes.Gray;

    public void SetStatus(string status, string detail, Brush brush)
    {
        Status = status;
        Detail = detail;
        StatusBrush = brush;
    }
}

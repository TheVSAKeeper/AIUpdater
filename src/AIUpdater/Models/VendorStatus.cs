using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace AIUpdater.Models;

public sealed class VendorStatus(string name, string logPath) : ObservableObject
{
    public string Name { get; } = name;
    public IReadOnlyList<UpdateStage> Stages { get; init; } = [];
    public IReadOnlyList<object> Cards { get; init; } = [];
    public string LogPath { get; internal set; } = logPath;

    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    public string Detail
    {
        get;
        set => SetProperty(ref field, value);
    } = "Готов к проверке обновлений";

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

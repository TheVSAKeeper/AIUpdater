using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AIUpdater.ViewModels;

public sealed class LogPanelViewModel : ObservableObject, KeepShell.ViewModels.IPageHeader
{
    public string PageTitle => "Журнал";
    public string PageDescription => "Последняя операция и история по поставщикам";
    private readonly IUpdaterExecutionService _executionService;
    private readonly IUpdaterDialogs _dialogService;

    public LogPanelViewModel(
        ObservableCollection<VendorStatus> vendors,
        IUpdaterExecutionService executionService,
        IUpdaterDialogs dialogService)
    {
        Vendors = vendors;
        _executionService = executionService;
        _dialogService = dialogService;
        LogFolderToolTip = $"Скрипт: {_executionService.ScriptPath}\nЖурналы: {_executionService.LogDirectory}";

        OpenLogCommand = new RelayCommand<object>(OpenSelectedLog);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        ZoomInLogCommand = new RelayCommand(() => SetLogFontSize(LogFontSize + 1));
        ZoomOutLogCommand = new RelayCommand(() => SetLogFontSize(LogFontSize - 1));
    }

    public ObservableCollection<VendorStatus> Vendors { get; }

    public VendorStatus? SelectedVendor
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string GeneralLogText
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public double LogFontSize
    {
        get;
        private set => SetProperty(ref field, value);
    } = 12;

    public string LogFolderToolTip
    {
        get;
        set => SetProperty(ref field, value);
    }

    public IRelayCommand<object> OpenLogCommand { get; }

    public IRelayCommand OpenLogFolderCommand { get; }

    public IRelayCommand ZoomInLogCommand { get; }

    public IRelayCommand ZoomOutLogCommand { get; }

    public void SetLogFontSize(double size)
    {
        LogFontSize = Math.Clamp(size, 10, 20);
    }

    public void LoadLastLog()
    {
        if (_executionService.ReadPreviousLog(
                _executionService.LogPath,
                "История прошлого запуска\n") is { } previousLog)
        {
            GeneralLogText = previousLog;
        }
    }

    private void OpenSelectedLog(object? parameter)
    {
        var vendor = parameter as VendorStatus ?? SelectedVendor;
        var selectedPath = vendor?.LogPath ?? _executionService.LogPath;
        if (!_executionService.LogFileExists(selectedPath))
        {
            _dialogService.ShowMessage("Лог последнего запуска ещё не создан.", "AI Updater");
            return;
        }

        _dialogService.OpenExternal(selectedPath);
    }

    private void OpenLogFolder()
    {
        if (!string.IsNullOrEmpty(_executionService.LogDirectory))
        {
            _dialogService.OpenExternal(_executionService.LogDirectory);
        }
    }
}

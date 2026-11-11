using AIUpdater.Bootstrap;
using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.ComponentModel;
using System.Windows;

namespace AIUpdater.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsStore _settings;
    private bool _closePending;
    private static WindowPlacementKeys PlacementKeys => new(SettingsKeys.WindowLeft, SettingsKeys.WindowTop, SettingsKeys.WindowWidth, SettingsKeys.WindowHeight, SettingsKeys.WindowMaximized);

    public MainWindow(ShellViewModel viewModel, ISettingsStore settings)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        _settings = settings;
        WindowChromeTheming.Attach(this);
        WindowPlacement.Restore(this, settings, PlacementKeys);
    }

    public ShellViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ViewModel.IsBusy)
        {
            WindowPlacement.Save(this, _settings, PlacementKeys);
            base.OnClosing(e);
            return;
        }
        e.Cancel = true;
        if (_closePending) return;
        _closePending = true;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try { if (await ViewModel.RequestCloseAsync()) Close(); }
            finally { _closePending = false; }
        });
    }
}

using AIUpdater.Infrastructure;
using AIUpdater.ViewModels;
using Microsoft.Win32;
using System.Windows;

namespace AIUpdater.Views;

public partial class TranslationAgentWindow : Window
{
    public TranslationAgentWindow(TranslationAgentViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.Reset();
        DataContext = ViewModel;
        InitializeComponent();
        ModelChoicesButton.ContextMenu.DataContext = ViewModel;
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        ViewModel.Saved += OnSaved;
        Loaded += async (_, _) => await ViewModel.DetectCommand.ExecuteAsync(null);
        Closed += (_, _) =>
        {
            ViewModel.Saved -= OnSaved;
            ViewModel.DetectCommand.Cancel();
            if (DialogResult != true) ViewModel.Reset();
        };
    }

    public TranslationAgentViewModel ViewModel { get; }

    private void OnSaved()
    {
        if (IsVisible) DialogResult = true;
    }

    private void ModelChoices_Click(object sender, RoutedEventArgs e)
    {
        ModelChoicesButton.ContextMenu.PlacementTarget = ModelChoicesButton;
        ModelChoicesButton.ContextMenu.IsOpen = true;
    }

    private void ModelChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Header: string model }) ViewModel.Model = model;
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Программа агента",
            Filter = "Программы CLI (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) ViewModel.ExecutablePath = dialog.FileName;
    }
}

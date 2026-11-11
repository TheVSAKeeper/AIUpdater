using AIUpdater.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace AIUpdater.Views.Settings;

public partial class TranslationSettingsCard : UserControl
{
    public TranslationSettingsCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ModelChoicesButton.ContextMenu.DataContext = DataContext;
    }

    private async void ModelChoices_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TranslationAgentViewModel model || model.DetectCommand.IsRunning) return;
        var backend = model.BackendId;
        var path = model.ExecutablePath;
        await model.DetectCommand.ExecuteAsync(null);
        if (DataContext != model || model.BackendId != backend || model.ExecutablePath != path || !model.HasModels) return;
        ModelChoicesButton.ContextMenu.PlacementTarget = ModelChoicesButton;
        ModelChoicesButton.ContextMenu.IsOpen = true;
    }

    private void ModelChoice_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TranslationAgentViewModel model && sender is MenuItem { Header: string id }) model.Model = id;
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Программа агента", Filter = "Программы CLI (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|Все файлы (*.*)|*.*", CheckFileExists = true };
        if (DataContext is TranslationAgentViewModel model && picker.ShowDialog(Window.GetWindow(this)) == true) model.ExecutablePath = picker.FileName;
    }
}

using AIUpdater.Infrastructure;
using AIUpdater.Models;
using AIUpdater.Services;
using AIUpdater.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;

namespace AIUpdater.Views;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow(
        IEnumerable<UpdateStage> stages,
        UpdateStage? selected = null,
        IChangelogService? changelogService = null,
        IDialogService? dialogService = null)
        : this(new ChangelogViewModel(
            stages,
            selected,
            changelogService ?? App.Services.GetRequiredService<IChangelogService>(),
            dialogService ?? App.Services.GetRequiredService<IDialogService>()))
    {
    }

    public ChangelogWindow(ChangelogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        InitializeComponent();

        if (!ViewModel.HasStages)
        {
            ComponentList.DisplayMemberPath = "Name";
        }

        ViewModel.ScrollToHomeRequested += () => ReleasesScrollViewer.ScrollToHome();
        Closed += (_, _) => ViewModel.Dispose();
    }

    public ChangelogViewModel ViewModel { get; }

    private void TranslationSettings_Click(object sender, RoutedEventArgs e)
    {
        new TranslationAgentWindow(ViewModel.AgentSettings) { Owner = this }.ShowDialog();
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Delta > 0)
        {
            ViewModel.ZoomInCommand.Execute(null);
        }
        else
        {
            ViewModel.ZoomOutCommand.Execute(null);
        }

        e.Handled = true;
    }
}

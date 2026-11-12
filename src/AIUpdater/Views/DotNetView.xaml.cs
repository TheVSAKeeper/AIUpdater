using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.Windows;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class DotNetView : UserControl, IView<DotNetViewModel>
{
    public DotNetView()
    {
        InitializeComponent();
        Loaded += (_, _) => TriggerEnsureLoaded();
        IsVisibleChanged += (_, _) => TriggerEnsureLoaded();
    }

    private void TriggerEnsureLoaded()
    {
        if (IsVisible && DataContext is DotNetViewModel viewModel)
        {
            _ = viewModel.EnsureLoadedAsync();
        }
    }
}

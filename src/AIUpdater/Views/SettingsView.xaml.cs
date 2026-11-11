using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.Windows;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class SettingsView : UserControl, IView<SettingsViewModel>
{
    public SettingsView() => InitializeComponent();
    private void SuppressAutoScroll(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;
}

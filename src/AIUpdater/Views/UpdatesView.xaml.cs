using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class UpdatesView : UserControl, IView<MainViewModel>
{
    public UpdatesView() => InitializeComponent();
}

using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class LogsView : UserControl, IView<LogPanelViewModel>
{
    public LogsView() => InitializeComponent();
}

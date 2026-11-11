using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class MessageDialogView : UserControl, IView<MessageDialogViewModel>
{
    public MessageDialogView() => InitializeComponent();
}

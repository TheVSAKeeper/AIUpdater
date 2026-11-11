using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIUpdater.Views;

public partial class ChangelogView : UserControl, IView<ChangelogViewModel>
{
    public ChangelogView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ChangelogViewModel oldModel) oldModel.ScrollToHomeRequested -= ScrollToHome;
            if (e.NewValue is ChangelogViewModel model)
            {
                model.ScrollToHomeRequested += ScrollToHome;
                ComponentList.DisplayMemberPath = model.HasStages ? "" : "Name";
            }
        };
    }

    private void ScrollToHome() => ReleasesScrollViewer.ScrollToHome();

    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || DataContext is not ChangelogViewModel model) return;
        if (e.Delta > 0) model.ZoomInCommand.Execute(null);
        else model.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }
}

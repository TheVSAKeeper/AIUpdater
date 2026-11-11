using System.Windows;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class TitleBarView : UserControl
{
    public TitleBarView() => InitializeComponent();
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(Window.GetWindow(this));
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(Window.GetWindow(this));
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
        else SystemCommands.MaximizeWindow(window);
    }
}

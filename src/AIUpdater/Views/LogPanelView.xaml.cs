using AIUpdater.Models;
using AIUpdater.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIUpdater.Views;

public partial class LogPanelView : UserControl
{
    private LogPanelViewModel? _initializedViewModel;

    public LogPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => EnsureVendorTabs();
    }

    public LogPanelViewModel? ViewModel => DataContext as LogPanelViewModel;

    private void EnsureVendorTabs()
    {
        if (ViewModel is null || ReferenceEquals(_initializedViewModel, ViewModel))
        {
            return;
        }

        _initializedViewModel = ViewModel;
        while (LogTabs.Items.Count > 1)
        {
            LogTabs.Items.RemoveAt(1);
        }

        var headerTemplate = (DataTemplate)FindResource("VendorTabHeaderTemplate");
        foreach (var vendor in ViewModel.Vendors)
        {
            LogTabs.Items.Add(new TabItem
            {
                DataContext = vendor,
                Header = vendor,
                HeaderTemplate = headerTemplate,
                Content = vendor,
            });
        }
    }

    private void LogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.SelectedVendor = (LogTabs.SelectedItem as FrameworkElement)?.DataContext as VendorStatus;
        }
    }

    private void LogTabs_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control || ViewModel is null)
        {
            return;
        }

        if (e.Delta > 0)
        {
            ViewModel.ZoomInLogCommand.Execute(null);
        }
        else
        {
            ViewModel.ZoomOutLogCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.ScrollToEnd();
        }
    }
}

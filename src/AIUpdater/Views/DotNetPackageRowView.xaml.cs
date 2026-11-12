using AIUpdater.ViewModels;
using KeepShell.Bootstrap;
using System.Windows;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class DotNetPackageRowView : UserControl, IView<DotNetPackageRowViewModel>
{
    public DotNetPackageRowView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildContextMenu();
        RowBorder.ContextMenuOpening += (_, _) => RebuildContextMenu();
    }

    private void RebuildContextMenu()
    {
        RowMenu.Items.Clear();
        if (DataContext is not DotNetPackageRowViewModel row)
        {
            return;
        }

        RowMenu.DataContext = row;
        RowMenu.Items.Add(new MenuItem
        {
            Header = row.InstallMenuHeader,
            Command = row.InstallLatestCommand,
            IsEnabled = row.CanInstallLatest && row.CanInteract,
        });
        if (row.HasInstallVersionMenu)
        {
            var versionsMenu = new MenuItem
            {
                Header = "Установить версию",
                IsEnabled = row.CanInteract && row.InstallOptions.Any(option => option.IsEnabled),
            };
            foreach (var option in row.InstallOptions)
            {
                versionsMenu.Items.Add(new MenuItem
                {
                    Header = option.Header,
                    Command = option.Command,
                    IsEnabled = option.IsEnabled && row.CanInteract,
                });
            }

            RowMenu.Items.Add(versionsMenu);
        }

        RowMenu.Items.Add(new MenuItem
        {
            Header = "Обновить",
            Command = row.UpdateCommand,
            IsEnabled = row.CanUpdate && row.CanInteract,
        });

        if (row.UninstallOptions.Count == 0)
        {
            RowMenu.Items.Add(new MenuItem
            {
                Header = "Удалить",
                IsEnabled = false,
            });
        }
        else
        {
            foreach (var option in row.UninstallOptions)
            {
                RowMenu.Items.Add(new MenuItem
                {
                    Header = $"Удалить {option.Header}",
                    Command = option.Command,
                    IsEnabled = option.IsEnabled && row.CanInteract,
                    ToolTip = option.ToolTip,
                });
            }
        }

        RowMenu.Items.Add(new Separator());
        RowMenu.Items.Add(new MenuItem
        {
            Header = "История выпусков",
            Command = row.OpenReleaseNotesCommand,
        });
    }

    private void RowMenuButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RebuildContextMenu();
        RowMenu.PlacementTarget = RowBorder;
        RowMenu.IsOpen = true;
    }
}

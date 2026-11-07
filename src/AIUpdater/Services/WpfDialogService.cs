using AIUpdater.Models;
using AIUpdater.Views;
using System.Diagnostics;
using System.Windows;

namespace AIUpdater.Services;

public sealed class WpfDialogService(IChangelogService changelogService) : IDialogService
{
    private readonly IChangelogService _changelogService = changelogService;
    private ChangelogWindow? _changelogWindow;

    public bool Confirm(string message, string title)
    {
        var owner = GetActiveWindow();
        return owner is not null
            ? AppDialog.Show(owner, message, title, true)
            : AppDialog.Create(null, message, title, true).ShowDialog() == true;
    }

    public void ShowMessage(string message, string title)
    {
        var owner = GetActiveWindow();
        if (owner is not null)
        {
            AppDialog.Show(owner, message, title);
        }
        else
        {
            AppDialog.Create(null, message, title, false).ShowDialog();
        }
    }

    public void OpenChangelog(IEnumerable<UpdateStage> stages, UpdateStage? selectedStage)
    {
        if (_changelogWindow is not null)
        {
            if (selectedStage is not null)
            {
                _changelogWindow.ViewModel.SelectStage(selectedStage);
            }

            _changelogWindow.Activate();
            return;
        }

        var owner = GetActiveWindow();
        _changelogWindow = new ChangelogWindow(stages, selectedStage, _changelogService, this)
        {
            Owner = owner,
        };
        _changelogWindow.Closed += (_, _) => _changelogWindow = null;
        _changelogWindow.Show();
    }

    public void OpenExternal(string pathOrUrl, string errorTitle = "AI Updater")
    {
        try
        {
            Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, errorTitle);
        }
    }

    private static Window? GetActiveWindow()
    {
        if (Application.Current is null)
        {
            return null;
        }

        return Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
               ?? Application.Current.MainWindow;
    }
}

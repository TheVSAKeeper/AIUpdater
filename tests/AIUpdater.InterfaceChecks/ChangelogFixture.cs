using AIUpdater.Models;
using AIUpdater.Services;
using AIUpdater.ViewModels;
using AIUpdater.Views;
using System.Windows;

namespace AIUpdater.InterfaceChecks;

internal sealed class ChangelogFixture
{
    private readonly ChangelogView _view;
    internal ChangelogFixture(IEnumerable<UpdateStage> stages, UpdateStage? selected = null, IChangelogService? service = null)
    {
        ViewModel = new(stages, selected, service);
        _view = new() { DataContext = ViewModel };
        _view.Measure(new(1120, 740));
        _view.Arrange(new(0, 0, 1120, 740));
        _view.UpdateLayout();
    }
    internal ChangelogViewModel ViewModel { get; }
    internal FrameworkElement Content => _view;
    internal object FindName(string name) => _view.FindName(name);
    internal void Close() => ViewModel.Dispose();
}

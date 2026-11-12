using AIUpdater.Models;

namespace AIUpdater.Services;

public sealed class AppNavigator : IAppNavigator
{
    private IAppNavigator? _target;
    public void Attach(IAppNavigator target) => _target = target;
    private IAppNavigator Target => _target ?? throw new InvalidOperationException("Оболочка приложения ещё не запущена.");
    public bool Navigate(string key) => Target.Navigate(key);
    public void ShowDotNet() => Target.ShowDotNet();
    public void ShowChangelog(UpdateStage? stage = null) => Target.ShowChangelog(stage);
    public void ShowSettings() => Target.ShowSettings();
}

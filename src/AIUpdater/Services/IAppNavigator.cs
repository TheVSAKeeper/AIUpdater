using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IAppNavigator
{
    bool Navigate(string key);
    void ShowDotNet();
    void ShowChangelog(UpdateStage? stage = null);
    void ShowSettings();
}

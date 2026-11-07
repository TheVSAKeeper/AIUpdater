using AIUpdater.Models;

namespace AIUpdater.Services;

public interface IStageResultStore
{
    string ResultsPath { get; set; }

    void Load(IEnumerable<UpdateStage> stages, Action<string>? onError = null);

    void Save(IEnumerable<UpdateStage> stages, Action<string>? onError = null);
}

using AIUpdater.Models;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AIUpdater.Services;

public sealed class FileStageResultStore(string? resultsPath = null) : IStageResultStore
{
    public string ResultsPath { get; set; } = resultsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIUpdater",
        "Logs",
        "LastResults.json");

    public void Load(IEnumerable<UpdateStage> stages, Action<string>? onError = null)
    {
        if (!File.Exists(ResultsPath))
        {
            return;
        }

        var stageList = stages.ToList();
        try
        {
            var results = JsonSerializer.Deserialize<PackageResult[]>(File.ReadAllText(ResultsPath, Encoding.UTF8));
            foreach (var result in results ?? [])
            {
                if (result is null || result.CheckedAt is null || !UpdateStage.TryParseCheckResult(result.Result, out _))
                {
                    continue;
                }

                stageList.FirstOrDefault(stage => stage.Id == result.Stage)
                    ?.SetCheckResult(result.Result, result.Installed, result.Available, result.CheckedAt, result.Info);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            onError?.Invoke($"Не удалось загрузить результаты: {exception.Message}");
        }
    }

    public void Save(IEnumerable<UpdateStage> stages, Action<string>? onError = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(ResultsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var results = stages
                .Where(stage => stage.CheckResult is not null)
                .Select(stage => new PackageResult(
                    stage.Id,
                    stage.CheckResult!,
                    stage.InstalledVersion,
                    stage.AvailableVersion,
                    stage.CheckedAt,
                    stage.Info))
                .ToArray();

            var json = JsonSerializer.Serialize(results);
            var tempPath = ResultsPath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, ResultsPath, true);
            }
            catch (UnauthorizedAccessException)
            {
                File.WriteAllText(ResultsPath, json, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            onError?.Invoke($"Не удалось сохранить результаты: {exception.Message}");
        }
    }

    private sealed record PackageResult(
        string Stage,
        string Result,
        string? Installed,
        string? Available,
        DateTimeOffset? CheckedAt = null,
        string? Info = null);
}

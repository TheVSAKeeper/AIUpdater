using AIUpdater.Bootstrap;
using KeepShell.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Text.Json;

namespace AIUpdater.Services;

public sealed record TranslationAgentProfile
{
    public string Model { get; init; } = "";
    public string Effort { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
}

public sealed record TranslationAgentSelection
{
    public bool Automatic { get; init; } = true;
    public string BackendId { get; init; } = "antigravity";
    public Dictionary<string, TranslationAgentProfile> Profiles { get; init; } = new()
    {
        ["antigravity"] = new() { Model = "gemini-3.8-flash-low", Effort = "low" },
    };
    [System.Text.Json.Serialization.JsonIgnore]
    public TranslationAgentProfile Profile => Profiles.GetValueOrDefault(BackendId) ?? new();
}

public sealed class TranslationAgentSettings
{
    public static TranslationAgentSettings Default { get; internal set; } = new();
    private readonly object _gate = new();
    private readonly KeepShell.Bootstrap.ISettingsStore? _store;
    private readonly ILogger _logger;
    public string FilePath { get; }
    public int Generation { get; private set; }
    public TranslationAgentSelection Current { get; private set; }
    public event Action? Changed;

    public (TranslationAgentSelection Selection, int Generation) Snapshot()
    {
        lock (_gate) return (Current, Generation);
    }

    internal bool ApplyIfCurrent(int generation, Action apply)
    {
        lock (_gate)
        {
            if (generation != Generation) return false;
            apply();
            return true;
        }
    }

    internal TranslationAgentSettings(string? path = null, ILogger<TranslationAgentSettings>? logger = null)
    {
        _logger = logger ?? NullLogger<TranslationAgentSettings>.Instance;
        FilePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUpdater", "translation-agent.json");
        Current = new();
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<TranslationAgentSelection>(File.ReadAllText(FilePath));
                if (loaded is not null && loaded.Profiles is not null && AgentBackendFactory.Ids.Contains(loaded.BackendId)) Current = loaded;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.AgentSettingsLoadFailed(exception, FilePath);
        }
    }

    [ActivatorUtilitiesConstructor]
    public TranslationAgentSettings(KeepShell.Bootstrap.ISettingsStore settings, AppPaths paths, ILogger<TranslationAgentSettings>? logger = null)
        : this(paths.LegacyAgentSettingsFile, logger)
    {
        _store = settings;
        FilePath = settings.FilePath;
        var saved = settings.GetStringValue(SettingsKeys.TranslationSelection);
        if (saved is not null)
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<TranslationAgentSelection>(saved);
                if (loaded is not null && loaded.Profiles is not null && AgentBackendFactory.Ids.Contains(loaded.BackendId)) Current = loaded;
            }
            catch (JsonException exception)
            {
                _logger.AgentSettingsLoadFailed(exception, FilePath);
            }
        }
        else if (File.Exists(paths.LegacyAgentSettingsFile))
        {
            settings.SetValue(SettingsKeys.TranslationSelection, JsonSerializer.Serialize(Current));
            settings.Flush();
            _logger.AgentSettingsMigratedFromLegacy(paths.LegacyAgentSettingsFile, Current.BackendId);
        }
    }

    public void Save(TranslationAgentSelection selection)
    {
        if (!AgentBackendFactory.Ids.Contains(selection.BackendId)) throw new ArgumentException("Неизвестный агент.");
        int generation;
        lock (_gate)
        {
            if (_store is not null)
            {
                _store.SetValue(SettingsKeys.TranslationSelection, JsonSerializer.Serialize(selection));
                _store.Flush();
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temporary = FilePath + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(selection, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temporary, FilePath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            Current = selection;
            Generation++;
            generation = Generation;
        }
        _logger.AgentSettingsSaved(
            selection.BackendId,
            string.IsNullOrWhiteSpace(selection.Profile.Model) ? "по умолчанию" : selection.Profile.Model,
            string.IsNullOrWhiteSpace(selection.Profile.Effort) ? "по умолчанию" : selection.Profile.Effort,
            selection.Automatic,
            generation);
        Changed?.Invoke();
    }
}

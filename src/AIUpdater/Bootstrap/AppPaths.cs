using System.IO;

namespace AIUpdater.Bootstrap;

public sealed record AppPaths(string DataDirectory, string ScriptDirectory)
{
    public static AppPaths Default => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUpdater"), AppContext.BaseDirectory);
    public string SettingsFile => Path.Combine(DataDirectory, "settings.toml");
    public string LegacyAgentSettingsFile => Path.Combine(DataDirectory, "translation-agent.json");
    public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
    public string ResultsFile => Path.Combine(LogsDirectory, "LastResults.json");
    public string ChangelogDirectory => Path.Combine(DataDirectory, "Changelogs");
    public string DotNetMetadataDirectory => Path.Combine(DataDirectory, "DotNetMetadata");
}

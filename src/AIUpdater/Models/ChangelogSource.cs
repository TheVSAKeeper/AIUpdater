namespace AIUpdater.Models;

public sealed record ChangelogSource(string? PageUrl, string? DownloadUrl, string Format, string Note);

public sealed record ReleaseBadge(string Text, string Kind, double FontSize);

public enum ReleaseChannel
{
    None = 0,
    Stable = 1,
    Prerelease = 2,
}

public enum ChangeCategory
{
    None = 0,
    Added = 1,
    Changed = 2,
    Fixed = 3,
    Security = 4,
    Other = 5,
}

public sealed class ChangelogGroup
{
    public ChangeCategory Category { get; set; }
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public List<string> Items { get; set; } = [];
}

public sealed class ChangelogRelease
{
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string? PublishedAt { get; set; }
    public bool IsPrerelease { get; set; }
    public string? CompareUrl { get; set; }
    public string? Summary { get; set; }
    public List<ChangelogGroup> Groups { get; set; } = [];
    public string? TranslatedTitle { get; set; }
    public string? TranslatedSummary { get; set; }
    public List<ChangelogGroup>? TranslatedGroups { get; set; }
    public TranslationProvenance? Translation { get; set; }
    public bool IsTranslationStale => Translation is not null && Translation.SourceHash != TranslationProvenance.Hash(this);
    public bool HasTranslation => TranslatedGroups is { Count: > 0 };
}

public sealed class ChangelogDocument
{
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; set; }
    public string StageId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ETag { get; set; }
    public List<ChangelogRelease> Releases { get; set; } = [];
}

namespace AIUpdater.Models;

public sealed record ChangelogSource(string? PageUrl, string? DownloadUrl, string Format, string Note);

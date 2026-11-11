using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUpdater.Models;

public sealed record TranslationProvenance(string BackendId, string RequestedModel, string Effort, string SourceHash, int PromptVersion, DateTimeOffset CreatedAt)
{
    public static string Hash(ChangelogRelease release) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { release.Version, release.Title, Groups = release.Groups.Select(group => new { group.Category, group.Items }) }))));
}

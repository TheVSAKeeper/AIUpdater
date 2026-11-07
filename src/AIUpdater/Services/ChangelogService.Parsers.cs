using AIUpdater.Models;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AIUpdater.Services;

public static partial class ChangelogService
{
    private static List<ChangelogRelease> ParseGitHubReleases(string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            return ParseGitHubAtomReleases(content);
        }

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var elements = new List<JsonElement>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray().Take(12))
            {
                elements.Add(item);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            elements.Add(root);
        }

        var releases = new List<ChangelogRelease>();
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tag = element.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var date = element.TryGetProperty("published_at", out var dateProp) ? FormatIsoDate(dateProp.GetString()) : null;
            var body = element.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            var version = NormalizeVersion(tag) ?? NormalizeVersion(name) ?? tag ?? name ?? "Release";
            var title = !string.IsNullOrWhiteSpace(name) ? name! : !string.IsNullOrWhiteSpace(tag) ? tag! : version;
            var groups = ParseBodyIntoGroups(body);
            if (groups.Count == 0 && !string.IsNullOrWhiteSpace(title))
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Changed,
                    Title = CategoryTitle(ChangeCategory.Changed),
                    Items = [$"Опубликован выпуск {title}."],
                });
            }

            releases.Add(new()
            {
                Version = version,
                Title = title,
                PublishedAt = date,
                Groups = groups,
            });
        }

        if (releases.Count == 0)
        {
            throw new InvalidDataException("Источник не вернул описание выпуска.");
        }

        return releases;
    }

    private static List<ChangelogRelease> ParseGitHubAtomReleases(string xml)
    {
        var releases = new List<ChangelogRelease>();
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.w3.org/2005/Atom";
        foreach (var entry in doc.Descendants(ns + "entry").Take(12))
        {
            var title = entry.Element(ns + "title")?.Value?.Trim() ?? "";
            var updated = FormatIsoDate(entry.Element(ns + "updated")?.Value);
            var id = entry.Element(ns + "id")?.Value ?? "";
            var tagFromId = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : title;
            var htmlBody = entry.Element(ns + "content")?.Value ?? "";
            var plainBody = HtmlToMarkdownLines(htmlBody);
            var version = NormalizeVersion(tagFromId) ?? NormalizeVersion(title) ?? title;
            var groups = ParseBodyIntoGroups(plainBody);
            if (groups.Count == 0 && !string.IsNullOrWhiteSpace(title))
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Changed,
                    Title = CategoryTitle(ChangeCategory.Changed),
                    Items = [$"Опубликован выпуск {title}."],
                });
            }

            releases.Add(new()
            {
                Version = version,
                Title = string.IsNullOrWhiteSpace(title) ? $"v{version}" : title,
                PublishedAt = updated,
                Groups = groups,
            });
        }

        return releases;
    }

    private static List<ChangelogRelease> ParseAgyReleases(string content)
    {
        var releases = new List<ChangelogRelease>();
        var matches = Regex.Matches(content, @"(?m)^(?<ver>\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z.-]+)?)\s*:\s*$");
        if (matches.Count == 0)
        {
            return ParseMarkdownReleases(content);
        }

        for (var index = 0; index < matches.Count && releases.Count < 15; index++)
        {
            var version = matches[index].Groups["ver"].Value.Trim();
            var start = matches[index].Index + matches[index].Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : content.Length;
            var body = content[start..end].Trim();
            var groups = ParseBodyIntoGroups(body);
            if (groups.Count > 0)
            {
                releases.Add(new()
                {
                    Version = version,
                    Title = $"Antigravity CLI v{version}",
                    Groups = groups,
                });
            }
        }

        return releases;
    }

    private static List<ChangelogRelease> ParseMarkdownReleases(string content)
    {
        var cleaned = Regex.Replace(content, @"\A---\r?\n[\s\S]*?\r?\n---\r?\n", "");
        cleaned = Regex.Replace(cleaned, @"\{\{[<%][\s\S]*?[>%]\}\}", "");
        var headingPattern = new Regex(
            @"(?m)^#{1,3}\s+(?<line>(?:(?<date1>\d{4}-\d{2}-\d{2})[,\s–-]*(?:Version\s+)?)?\[?v?(?<ver>\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z._-]+)?)\]?(?<rest>[^\r\n]*))$",
            RegexOptions.IgnoreCase);

        var matches = headingPattern.Matches(cleaned);
        if (matches.Count == 0)
        {
            var fallbackGroups = ParseBodyIntoGroups(cleaned);
            return fallbackGroups.Count == 0
                ? []
                :
                [
                    new()
                    {
                        Version = "latest",
                        Title = "Последние изменения",
                        Groups = fallbackGroups,
                    },
                ];
        }

        var releases = new List<ChangelogRelease>();
        for (var index = 0; index < matches.Count && releases.Count < 12; index++)
        {
            var match = matches[index];
            var version = match.Groups["ver"].Value.Trim();
            var rest = match.Groups["rest"].Value.Trim();
            var dateMatch = Regex.Match(match.Groups["line"].Value, @"\b(\d{4}-\d{2}-\d{2})\b");
            var publishedAt = dateMatch.Success ? dateMatch.Groups[1].Value : null;
            var start = match.Index + match.Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : Math.Min(cleaned.Length, start + 18000);
            var section = cleaned[start..end].Trim();
            var groups = ParseBodyIntoGroups(section);
            if (groups.Count == 0)
            {
                continue;
            }

            var cleanRest = Regex.Replace(rest, @"^[\s()\[\]–\u2014,-]+|\b\d{4}-\d{2}-\d{2}\b|\bLatest\b", "", RegexOptions.IgnoreCase).Trim(' ', '-', '–', ',', '(', ')');
            var title = string.IsNullOrWhiteSpace(cleanRest) ? $"v{version}" : $"v{version} – {cleanRest}";
            releases.Add(new()
            {
                Version = version,
                Title = title,
                PublishedAt = publishedAt,
                Groups = groups,
            });
        }

        return releases;
    }

    private static List<ChangelogRelease> ParseDotNetReleases(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("releases", out var releasesArray)
            || releasesArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Метаданные выпусков .NET не найдены.");
        }

        var releases = new List<ChangelogRelease>();
        foreach (var item in releasesArray.EnumerateArray().Take(10))
        {
            var releaseVersion = item.TryGetProperty("release-version", out var rv) ? rv.GetString() ?? "" : "";
            var releaseDate = item.TryGetProperty("release-date", out var rd) ? rd.GetString() : null;
            var isSecurity = item.TryGetProperty("security", out var sec) && sec.ValueKind == JsonValueKind.True;
            var primarySdk = item.TryGetProperty("sdk", out var sdkObj) && sdkObj.TryGetProperty("version", out var sv)
                ? sv.GetString()
                : null;

            var sdkVersions = new List<string>();
            if (item.TryGetProperty("sdks", out var sdksArr) && sdksArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var sdkEntry in sdksArr.EnumerateArray())
                {
                    if (sdkEntry.TryGetProperty("version", out var v) && v.GetString() is { Length: > 0 } ver && !sdkVersions.Contains(ver))
                    {
                        sdkVersions.Add(ver);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(primarySdk))
            {
                sdkVersions.Add(primarySdk!);
            }

            var groups = new List<ChangelogGroup>();
            var componentItems = new List<string>();
            if (sdkVersions.Count > 0)
            {
                componentItems.Add($"Выпуски .NET SDK: {string.Join(", ", sdkVersions.Select(v => $"`{v}`"))}");
            }

            if (item.TryGetProperty("runtime", out var rtObj) && rtObj.TryGetProperty("version", out var rtv) && rtv.GetString() is { } rtVer)
            {
                componentItems.Add($".NET Runtime: `{rtVer}`");
            }

            if (item.TryGetProperty("aspnetcore-runtime", out var aspObj) && aspObj.TryGetProperty("version", out var aspv) && aspv.GetString() is { } aspVer)
            {
                componentItems.Add($"ASP.NET Core Runtime: `{aspVer}`");
            }

            if (item.TryGetProperty("windowsdesktop", out var wdObj) && wdObj.TryGetProperty("version", out var wdv) && wdv.GetString() is { } wdVer)
            {
                componentItems.Add($"Windows Desktop Runtime: `{wdVer}`");
            }

            if (componentItems.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Changed,
                    Title = CategoryTitle(ChangeCategory.Changed),
                    Items = componentItems,
                });
            }

            var securityItems = new List<string>();
            if (item.TryGetProperty("cve-list", out var cveArr) && cveArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var cve in cveArr.EnumerateArray())
                {
                    var id = cve.TryGetProperty("cve-id", out var cid) ? cid.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        securityItems.Add($"Исправлена уязвимость `{id}`");
                    }
                }
            }

            if (isSecurity && securityItems.Count == 0)
            {
                securityItems.Add("Выпуск содержит обновления безопасности.");
            }

            if (securityItems.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Security,
                    Title = CategoryTitle(ChangeCategory.Security),
                    Items = securityItems,
                });
            }

            var versionKey = primarySdk ?? releaseVersion;
            releases.Add(new()
            {
                Version = versionKey,
                Title = sdkVersions.Count > 0
                    ? $".NET {releaseVersion} (SDK {string.Join("; ", sdkVersions)})"
                    : $".NET {releaseVersion}",
                PublishedAt = releaseDate,
                Groups = groups,
            });
        }

        return releases;
    }

    private static List<ChangelogRelease> ParseNpmRegistryReleases(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (!root.TryGetProperty("versions", out var versionsObj) || versionsObj.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Реестр npm не вернул список версий пакета.");
        }

        var publishTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("time", out var timeObj) && timeObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in timeObj.EnumerateObject())
            {
                if (prop.Name is "created" or "modified")
                {
                    continue;
                }

                if (DateTimeOffset.TryParse(prop.Value.GetString(), out var parsed))
                {
                    publishTimes[prop.Name] = parsed;
                }
            }
        }

        var distTags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("dist-tags", out var tagsObj) && tagsObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var tag in tagsObj.EnumerateObject())
            {
                var ver = tag.Value.GetString();
                if (!string.IsNullOrWhiteSpace(ver))
                {
                    if (!distTags.TryGetValue(ver, out var list))
                    {
                        distTags[ver] = list = [];
                    }

                    list.Add(tag.Name);
                }
            }
        }

        var allVersions = versionsObj.EnumerateObject()
            .Select(prop => (Version: prop.Name, Element: prop.Value, Time: publishTimes.GetValueOrDefault(prop.Name, DateTimeOffset.MinValue)))
            .OrderByDescending(entry => entry.Time)
            .ToList();

        var releases = new List<ChangelogRelease>();
        for (var index = 0; index < Math.Min(10, allVersions.Count); index++)
        {
            var current = allVersions[index];
            var previous = index + 1 < allVersions.Count ? allVersions[index + 1] : default;
            var added = new List<string>();
            var changed = new List<string>();

            if (distTags.TryGetValue(current.Version, out var tags))
            {
                changed.Add($"Канал публикации npm: {string.Join(", ", tags.Select(t => $"`{t}`"))}");
            }

            var currentNodeEngine = ReadEngineNode(current.Element);
            var previousNodeEngine = previous.Element.ValueKind == JsonValueKind.Object ? ReadEngineNode(previous.Element) : null;
            if (!string.IsNullOrWhiteSpace(currentNodeEngine))
            {
                if (!string.IsNullOrWhiteSpace(previousNodeEngine) && previousNodeEngine != currentNodeEngine)
                {
                    changed.Add($"Требование к среде Node.js изменено: `{previousNodeEngine}` → `{currentNodeEngine}`");
                }
                else
                {
                    changed.Add($"Требуемая версия среды выполнения Node.js: `{currentNodeEngine}`");
                }
            }

            var currentDeps = ReadPackageDependencies(current.Element);
            var previousDeps = previous.Element.ValueKind == JsonValueKind.Object
                ? ReadPackageDependencies(previous.Element)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (depName, depVer) in currentDeps)
            {
                if (previousDeps.Count > 0 && !previousDeps.ContainsKey(depName))
                {
                    added.Add($"Добавлен модуль `{depName}` версии `{depVer}`");
                }
                else if (previousDeps.TryGetValue(depName, out var oldVer) && oldVer != depVer)
                {
                    changed.Add($"Обновлён компонент `{depName}`: `{oldVer}` → `{depVer}`");
                }
            }

            if (added.Count == 0 && changed.Count <= 2 && currentDeps.Count > 0)
            {
                var summaryDeps = currentDeps
                    .Where(kv => kv.Key.StartsWith("@deepseek-ai/", StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .Select(kv => $"`{kv.Key}@{kv.Value}`")
                    .ToList();

                if (summaryDeps.Count > 0)
                {
                    changed.Add($"Платформенные пакеты выпуска: {string.Join(", ", summaryDeps)}");
                }
            }

            var groups = new List<ChangelogGroup>();
            if (added.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Added,
                    Title = CategoryTitle(ChangeCategory.Added),
                    Items = added,
                });
            }

            if (changed.Count > 0)
            {
                groups.Add(new()
                {
                    Category = ChangeCategory.Changed,
                    Title = CategoryTitle(ChangeCategory.Changed),
                    Items = changed,
                });
            }

            releases.Add(new()
            {
                Version = current.Version,
                Title = $"DeepSeek Harness v{current.Version}",
                PublishedAt = current.Time > DateTimeOffset.MinValue ? current.Time.ToString("yyyy-MM-dd") : null,
                Groups = groups,
            });
        }

        return releases;
    }

    private static List<ChangelogRelease> ParseCodexAppHtmlReleases(string content)
    {
        var itemMatches = Regex.Matches(
            content,
            @"<li\b[^>]*data-codex-topics=""(?<topics>[^""]*)""[^>]*>(?<body>[\s\S]*?</article>)\s*</li>",
            RegexOptions.IgnoreCase);

        if (itemMatches.Count == 0)
        {
            return ParseHtmlReleases(content);
        }

        var releases = new List<ChangelogRelease>();
        foreach (Match match in itemMatches)
        {
            var topics = match.Groups["topics"].Value;
            if (!Regex.IsMatch(topics, @"\bcodex-app\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var bodyHtml = match.Groups["body"].Value;
            var date = Regex.Match(bodyHtml, @"<time\b[^>]*>([^<]+)</time>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var rawTitle = Regex.Match(bodyHtml, @"<h3\b[^>]*>([\s\S]*?)</h3>", RegexOptions.IgnoreCase).Groups[1].Value;
            var title = StripTags(Regex.Replace(rawTitle, @"<button\b[^>]*>[\s\S]*?</button>", "", RegexOptions.IgnoreCase));
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var articleMatch = Regex.Match(bodyHtml, @"<article\b[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase);
            var articleHtml = articleMatch.Success ? articleMatch.Groups[1].Value : bodyHtml;
            var articleLines = HtmlBodyToLines(articleHtml);
            var groups = ParseBodyIntoGroups(articleLines);
            if (groups.Count == 0)
            {
                continue;
            }

            var versionMatch = Regex.Match(title, @"\b(\d{2}\.\d+(?:\.\d+)*|\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\b");
            var versionKey = versionMatch.Success
                ? versionMatch.Groups[1].Value
                : !string.IsNullOrWhiteSpace(date) ? $"{date}-{title}" : title;

            var displayTitle = title.StartsWith("Codex app", StringComparison.OrdinalIgnoreCase)
                               || title.Contains("Codex", StringComparison.OrdinalIgnoreCase)
                               || title.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)
                ? title
                : $"ChatGPT / Codex App – {title}";

            releases.Add(new()
            {
                Version = versionKey,
                Title = displayTitle,
                PublishedAt = string.IsNullOrWhiteSpace(date) ? null : date,
                Groups = groups,
            });

            if (releases.Count >= 15)
            {
                break;
            }
        }

        return releases.Count > 0 ? releases : ParseHtmlReleases(content);
    }

    private static List<ChangelogRelease> ParseHtmlReleases(string content)
    {
        var main = Regex.Match(content, @"<main\b[^>]*>([\s\S]*?)</main>", RegexOptions.IgnoreCase);
        var articles = Regex.Matches(content, @"<article\b[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase);
        string bodyHtml;
        if (main.Success)
        {
            bodyHtml = main.Groups[1].Value;
        }
        else if (articles.Count > 0)
        {
            bodyHtml = string.Join("\n\n", articles.Select(a => a.Groups[1].Value));
        }
        else
        {
            var bodyMatch = Regex.Match(content, @"<body\b[^>]*>([\s\S]*?)</body>", RegexOptions.IgnoreCase);
            if (!bodyMatch.Success)
            {
                throw new InvalidDataException("Страница требует просмотра в браузере. Откройте официальный источник.");
            }

            bodyHtml = bodyMatch.Groups[1].Value;
        }

        var markdownLike = HtmlToMarkdownLines(bodyHtml);
        if (markdownLike.Length < 80)
        {
            throw new InvalidDataException("На странице нет доступного текста изменений. Откройте официальный источник.");
        }

        var parsed = ParseMarkdownReleases(markdownLike);
        if (parsed.Count > 0 && !(parsed.Count == 1 && parsed[0].Version == "latest"))
        {
            return parsed;
        }

        var sectionMatches = Regex.Matches(markdownLike, @"(?m)^##\s+(?<head>[^\r\n]+)$");
        if (sectionMatches.Count > 0)
        {
            var releases = new List<ChangelogRelease>();
            for (var index = 0; index < sectionMatches.Count && releases.Count < 12; index++)
            {
                var head = Regex.Replace(sectionMatches[index].Groups["head"].Value, @"\bLatest\b", "", RegexOptions.IgnoreCase).Trim(' ', '-', '–', ':');
                if (string.IsNullOrWhiteSpace(head)
                    || IsCategoryHeading(head)
                    || Regex.IsMatch(head, @"^(?:ChatGPT\s*&\s*Codex\s*changelog|Codex\s*changelog|Release\s*notes|Changelog|What's\s*new)$", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                var start = sectionMatches[index].Index + sectionMatches[index].Length;
                var end = index + 1 < sectionMatches.Count ? sectionMatches[index + 1].Index : markdownLike.Length;
                var block = markdownLike[start..end].Trim();
                var groups = ParseBodyIntoGroups(block);
                if (groups.Count == 0)
                {
                    continue;
                }

                if (Regex.IsMatch(head, @"^(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}$", RegexOptions.IgnoreCase)
                    && groups.Sum(g => g.Items.Count) <= 1)
                {
                    continue;
                }

                var versionMatch = Regex.Match(head, @"v?(\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z.-]+)?)");
                var dateMatch = Regex.Match(head + " " + block, @"\b(\d{4}-\d{2}-\d{2}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},\s+\d{4})\b", RegexOptions.IgnoreCase);
                releases.Add(new()
                {
                    Version = versionMatch.Success ? versionMatch.Groups[1].Value : head,
                    Title = head,
                    PublishedAt = dateMatch.Success ? dateMatch.Groups[1].Value : null,
                    Groups = groups,
                });
            }

            if (releases.Count > 0)
            {
                return releases;
            }
        }

        return parsed;
    }

    private static string HtmlToMarkdownLines(string html)
    {
        var text = Regex.Replace(html, @"<(script|style|nav|footer|svg|noscript|button)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h[1-3]\b[^>]*>([\s\S]*?)</h[1-3]>", m =>
        {
            var h = StripTags(m.Groups[1].Value);
            return IsCategoryHeading(h) ? $"\n### {h}\n" : $"\n## {h}\n";
        }, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h[4-6]\b[^>]*>([\s\S]*?)</h[4-6]>", m => $"\n### {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<summary\b[^>]*>([\s\S]*?)</summary>", m => $"\n### {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<code\b[^>]*>([\s\S]*?)</code>", m => $"`{StripTags(m.Groups[1].Value)}`", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|/p|/div|/li|/tr|/section|/article|/details)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " "));
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");
        return text.Trim();
    }

    private static string HtmlBodyToLines(string html)
    {
        var text = Regex.Replace(html, @"<(script|style|nav|footer|svg|noscript|button)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h[1-6]\b[^>]*>([\s\S]*?)</h[1-6]>", m => $"\n### {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<summary\b[^>]*>([\s\S]*?)</summary>", m => $"\n### {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<code\b[^>]*>([\s\S]*?)</code>", m => $"`{StripTags(m.Groups[1].Value)}`", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>([\s\S]*?)</li>", m => $"\n- {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<p\b[^>]*>([\s\S]*?)</p>", m => $"\n- {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|/div|/tr|/section|/article|/details)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " "));
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");
        return text.Trim();
    }

    private static bool IsCategoryHeading(string heading)
    {
        var clean = Regex.Replace(heading, @"\(\d+\)|[:–-]", "").Trim();
        return Regex.IsMatch(
            clean,
            @"^(?:New\s+features?|Features?|Added|What's\s+new|Highlights|Notable(?:\s+changes)?|Bug\s*fixes|Fixes|Fixed|Improvements?(?:\s+and\s+bug\s*fixes)?|Changed|Changes|Updated|Updates|Documentation|Docs|Chores?|Maintenance|Refactoring|Performance|Security|Breaking\s+changes|Deprecations?|Removed|Known\s+issues|Other(?:\s+changes)?|View\s+details|Details)$",
            RegexOptions.IgnoreCase);
    }

    private static string StripTags(string html)
    {
        return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();
    }

    private static List<ChangelogGroup> ParseBodyIntoGroups(string body)
    {
        var buckets = new Dictionary<ChangeCategory, List<string>>
        {
            [ChangeCategory.Added] = [],
            [ChangeCategory.Changed] = [],
            [ChangeCategory.Fixed] = [],
            [ChangeCategory.Security] = [],
            [ChangeCategory.Other] = [],
        };

        var currentSectionCategory = ChangeCategory.None;
        foreach (var rawLine in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("Full Changelog:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                var heading = line.TrimStart('#').Trim();
                if (!heading.Equals("View details", StringComparison.OrdinalIgnoreCase))
                {
                    currentSectionCategory = ClassifyHeading(heading);
                }

                continue;
            }

            var isBullet = line.StartsWith("- ", StringComparison.Ordinal)
                           || line.StartsWith("* ", StringComparison.Ordinal)
                           || line.StartsWith("• ", StringComparison.Ordinal)
                           || line.StartsWith("· ", StringComparison.Ordinal)
                           || Regex.IsMatch(line, @"^\d+\.\s+");

            var cleaned = isBullet
                ? Regex.Replace(line, @"^(?:[-*•·]|\d+\.)\s+", "").Trim()
                : line;

            if (cleaned.Length < 3)
            {
                continue;
            }

            if (IsCategoryHeading(cleaned)
                || Regex.IsMatch(cleaned, @"^(?:Latest|Today|View\s+details|\d{4}-\d{2}-\d{2})$", RegexOptions.IgnoreCase))
            {
                if (IsCategoryHeading(cleaned) && !cleaned.Equals("View details", StringComparison.OrdinalIgnoreCase))
                {
                    currentSectionCategory = ClassifyHeading(cleaned);
                }

                continue;
            }

            var (itemCategory, normalizedText) = ClassifyAndCleanItem(cleaned, currentSectionCategory);
            if (normalizedText.Length < 3)
            {
                continue;
            }

            var list = buckets[itemCategory];
            if (list.Count < 30 && !list.Contains(normalizedText, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(normalizedText);
            }
        }

        var groups = new List<ChangelogGroup>();
        foreach (var category in new[] { ChangeCategory.Added, ChangeCategory.Changed, ChangeCategory.Fixed, ChangeCategory.Security, ChangeCategory.Other })
        {
            if (buckets[category].Count > 0)
            {
                groups.Add(new ChangelogGroup
                {
                    Category = category,
                    Title = CategoryTitle(category),
                    Items = buckets[category],
                });
            }
        }

        return groups;
    }

    private static ChangeCategory ClassifyHeading(string heading)
    {
        if (Regex.IsMatch(heading, @"\b(improvements?\s+and\s+bug\s*fixes)\b", RegexOptions.IgnoreCase))
        {
            return ChangeCategory.None;
        }

        if (Regex.IsMatch(heading, @"\b(add|added|new|feature|features|highlights|notable|новое|добавлено)\b", RegexOptions.IgnoreCase))
        {
            return ChangeCategory.Added;
        }

        if (Regex.IsMatch(heading, @"\b(fix|fixed|fixes|bug|bugs|bugfix|bugfixes|исправлено|ошибки)\b", RegexOptions.IgnoreCase))
        {
            return ChangeCategory.Fixed;
        }

        if (Regex.IsMatch(heading, @"\b(security|cve|vulnerability|vulnerabilities|безопасность)\b", RegexOptions.IgnoreCase))
        {
            return ChangeCategory.Security;
        }

        if (Regex.IsMatch(heading, @"\b(change|changed|improve|improved|improvements|update|updated|refactor|perf|breaking|commits|documentation|docs|chore|chores|maintenance|изменено|улучшено)\b", RegexOptions.IgnoreCase))
        {
            return ChangeCategory.Changed;
        }

        return ChangeCategory.None;
    }

    private static (ChangeCategory Category, string Text) ClassifyAndCleanItem(string text, ChangeCategory sectionCategory)
    {
        text = text.Replace('\u2014', '–');
        text = Regex.Replace(text, @"\\([\[\]])", "$1");
        text = Regex.Replace(text, @"\[(?<label>[^\]]+)\]\(https?://[^)]+\)", "${label}");
        text = Regex.Replace(text, @"\*\*(?<bold>[^*]+)\*\*", "${bold}");
        text = Regex.Replace(text, @"^\[\s*(`[^`]+`)\s*\]\s*[-–]\s*", "$1 – ");

        var prefixMatch = Regex.Match(
            text,
            @"^(?<prefix>(?:Added|New|Feature|Fixed|Fix|Bugfix|Changed|Improved|Updated|Security|Removed|Deprecated)\b\s*[:–-]|(?:feat|fix|perf|refactor|chore)(?:\([^)]*\))?!?:)\s*(?<rest>.+)$",
            RegexOptions.IgnoreCase);

        if (prefixMatch.Success)
        {
            var prefix = prefixMatch.Groups["prefix"].Value;
            var rest = prefixMatch.Groups["rest"].Value.Trim();
            if (prefix.StartsWith("Add", StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith("New", StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith("Feat", StringComparison.OrdinalIgnoreCase))
            {
                return (ChangeCategory.Added, CapitalizeFirst(rest));
            }

            if (prefix.StartsWith("Fix", StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith("Bug", StringComparison.OrdinalIgnoreCase))
            {
                return (ChangeCategory.Fixed, CapitalizeFirst(rest));
            }

            if (prefix.StartsWith("Sec", StringComparison.OrdinalIgnoreCase))
            {
                return (ChangeCategory.Security, CapitalizeFirst(rest));
            }

            return (ChangeCategory.Changed, CapitalizeFirst(rest));
        }

        if (Regex.IsMatch(text, @"\bCVE-\d{4}-\d+\b", RegexOptions.IgnoreCase))
        {
            return (ChangeCategory.Security, text);
        }

        if (sectionCategory != ChangeCategory.None)
        {
            return (sectionCategory, text);
        }

        if (Regex.IsMatch(text, @"^(?:Add(?:ed|s)?|Introduce[ds]?|Support(?:ed|s)?|Enable[ds]?|Allow(?:ed|s)?|Нов(?:ое|ый|ая)|Добавлен)\b", RegexOptions.IgnoreCase))
        {
            return (ChangeCategory.Added, text);
        }

        if (Regex.IsMatch(text, @"^(?:Fix(?:ed|es)?|Resolve[ds]?|Prevent(?:ed|s)?|Correct(?:ed|s)?|Avoid(?:ed|s)?|Исправлен)\b", RegexOptions.IgnoreCase))
        {
            return (ChangeCategory.Fixed, text);
        }

        return (ChangeCategory.Changed, text);
    }
}

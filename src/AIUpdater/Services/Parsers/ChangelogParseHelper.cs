using AIUpdater.Models;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace AIUpdater.Services.Parsers;

public static class ChangelogParseHelper
{
    public static readonly IReadOnlyList<ChangeCategory> OrderedCategories =
    [
        ChangeCategory.Added,
        ChangeCategory.Changed,
        ChangeCategory.Fixed,
        ChangeCategory.Security,
        ChangeCategory.Other,
    ];

    public static List<ChangelogGroup> ParseBodyIntoGroups(string body, string? fallbackReleaseTitle = null)
    {
        var buckets = OrderedCategories.ToDictionary(category => category, _ => new List<string>());

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
                || Regex.IsMatch(cleaned, @"^(?:Latest|Today|View\s+details|Full\s+release\s+on\s+GitHub|\d{4}-\d{2}-\d{2})$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(cleaned, @"^Release\s+v?\d+\.\d+(?:\.\d+)*(?:-[0-9A-Za-z._-]+)?$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(cleaned, @"^\$\s*(?:npm|pnpm|yarn|brew|winget|curl|irm)\b", RegexOptions.IgnoreCase))
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
        foreach (var category in OrderedCategories)
        {
            if (buckets[category].Count > 0)
            {
                groups.Add(new ChangelogGroup
                {
                    Category = category,
                    Title = ChangelogFormatter.CategoryTitle(category),
                    Items = buckets[category],
                });
            }
        }

        if (groups.Count == 0 && !string.IsNullOrWhiteSpace(fallbackReleaseTitle))
        {
            groups.Add(new()
            {
                Category = ChangeCategory.Changed,
                Title = ChangelogFormatter.CategoryTitle(ChangeCategory.Changed),
                Items = [$"Опубликован выпуск {fallbackReleaseTitle}."],
            });
        }

        return groups;
    }

    public static bool IsPlaceholderOnlyGroups(IReadOnlyList<ChangelogGroup>? groups)
    {
        return groups is null
               || groups.Count == 0
               || (groups.Count == 1
                   && groups[0].Items.Count == 1
                   && groups[0].Items[0].StartsWith("Опубликован выпуск ", StringComparison.Ordinal));
    }

    public static string HtmlToMarkdownLines(string html)
    {
        var text = Regex.Replace(html, @"<(script|style|nav|footer|svg|noscript|button|pre)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"<h[1-3]\b[^>]*>([\s\S]*?)</h[1-3]>", m =>
        {
            var h = StripTags(m.Groups[1].Value);
            return IsCategoryHeading(h) ? $"\n### {h}\n" : $"\n## {h}\n";
        }, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h[4-6]\b[^>]*>([\s\S]*?)</h[4-6]>", m => $"\n### {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<summary\b[^>]*>([\s\S]*?)</summary>", m => $"\n### {StripTags(m.Groups[1].Value)}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<code\b[^>]*>([\s\S]*?)</code>", m => $"`{StripTags(m.Groups[1].Value)}`", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|p|/p|/div|/li|/tr|/section|/article|/details)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " "));
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    public static bool IsCategoryHeading(string heading)
    {
        var clean = Regex.Replace(heading, @"\(\d+\)|[:–-]", "").Trim();
        return Regex.IsMatch(
            clean,
            @"^(?:New\s+features?|Features?|Added|What's\s+new|Highlights|Notable(?:\s+changes)?|Bug\s*fixes|Fixes|Fixed|Improvements?(?:\s+and\s+bug\s*fixes)?|Changed|Changes|Updated|Updates|Documentation|Docs|Chores?|Maintenance|Refactoring|Performance|Security|Breaking\s+changes|Deprecations?|Removed|Known\s+issues|Other(?:\s+changes)?|View\s+details|Details)$",
            RegexOptions.IgnoreCase);
    }

    public static string StripTags(string html)
    {
        return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();
    }

    public static string? FormatIsoDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto)
            || DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out dto))
        {
            return dto.ToString("yyyy-MM-dd");
        }

        return raw;
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

    private static string CapitalizeFirst(string value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}

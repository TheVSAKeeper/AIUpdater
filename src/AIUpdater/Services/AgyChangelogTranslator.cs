using AIUpdater.Models;
using AIUpdater.Services.Parsers;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public sealed class AgyChangelogTranslator(
    IChangelogContentFetcher? fetcher = null,
    Func<string, CancellationToken, Task<string?>>? promptRunner = null) : IChangelogTranslator
{
    private readonly IChangelogContentFetcher _fetcher = fetcher ?? new ChangelogContentFetcher();

    public Func<string, CancellationToken, Task<string?>>? PromptRunner { get; set; } = promptRunner;

    public async Task<bool> TranslateReleaseAsync(
        ChangelogRelease release,
        CancellationToken cancellationToken,
        bool forceRetranslate = false)
    {
        if (release.Groups.Count == 0)
        {
            return false;
        }

        if (!forceRetranslate && release.HasTranslation && !string.IsNullOrWhiteSpace(release.TranslatedSummary))
        {
            return true;
        }

        var prompt = BuildPrompt(release);
        var rawTranslation = PromptRunner is not null
            ? await PromptRunner(prompt, cancellationToken)
            : await InvokeAgyFlashTranslationAsync(prompt, cancellationToken);

        if (string.IsNullOrWhiteSpace(rawTranslation))
        {
            return false;
        }

        var (translatedGroups, translatedSummary) = ParseTranslatedRelease(rawTranslation, release.Groups);
        if (translatedGroups.Count == 0)
        {
            return false;
        }

        release.TranslatedTitle = string.IsNullOrWhiteSpace(release.Title) ? release.Version : release.Title;
        release.TranslatedSummary = !string.IsNullOrWhiteSpace(translatedSummary)
            ? translatedSummary
            : ChangelogFormatter.BuildReleaseSummary(translatedGroups);
        release.TranslatedGroups = translatedGroups;
        return true;
    }

    private static string BuildPrompt(ChangelogRelease release)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Переведи на русский язык список изменений релиза ПО и составь краткие резюме.");
        promptBuilder.AppendLine("Правила:");
        promptBuilder.AppendLine("1. Сначала выведи секцию [Summary] и 1–2 лаконичных предложения на русском языке с общим резюме релиза.");
        promptBuilder.AppendLine("2. Далее сохраняй заголовки секций строго в квадратных скобках: [Added], [Changed], [Fixed], [Security], [Other].");
        promptBuilder.AppendLine("3. Сразу под заголовком каждой секции выведи строку \"Summary: <краткая суть этого блока в одну строку>\".");
        promptBuilder.AppendLine("4. Каждый пункт изменений начинай с \"- \".");
        promptBuilder.AppendLine("5. Названия пакетов, команд, флагов CLI, API и фрагменты кода в `обратных кавычках` не переводи.");
        promptBuilder.AppendLine("6. Не добавляй вступлений, пояснений или заключений.");
        promptBuilder.AppendLine();

        foreach (var group in release.Groups)
        {
            var tag = group.Category switch
            {
                ChangeCategory.Added => "Added",
                ChangeCategory.Fixed => "Fixed",
                ChangeCategory.Security => "Security",
                ChangeCategory.Other => "Other",
                _ => "Changed",
            };

            promptBuilder.AppendLine($"[{tag}]");
            foreach (var item in group.Items.Take(20))
            {
                promptBuilder.AppendLine($"- {item}");
            }

            promptBuilder.AppendLine();
        }

        return promptBuilder.ToString();
    }

    private static (List<ChangelogGroup> Groups, string? Summary) ParseTranslatedRelease(
        string translatedText,
        List<ChangelogGroup> originalGroups)
    {
        var buckets = ChangelogParseHelper.OrderedCategories.ToDictionary(category => category, _ => new List<string>());
        var groupSummaries = new Dictionary<ChangeCategory, string>();
        var releaseSummaryLines = new List<string>();
        var inReleaseSummary = false;

        var currentCategory = originalGroups.FirstOrDefault()?.Category ?? ChangeCategory.Changed;
        foreach (var rawLine in translatedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().Replace('\u2014', '–');
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            if (Regex.IsMatch(line, @"^\[(?:Summary|Резюме|Сводка)\]$", RegexOptions.IgnoreCase))
            {
                inReleaseSummary = true;
                continue;
            }

            var sectionMatch = Regex.Match(line, @"^\[(?<sec>Added|Changed|Fixed|Security|Other|Новое|Изменено|Исправлено|Безопасность|Прочее)\]$", RegexOptions.IgnoreCase);
            if (sectionMatch.Success)
            {
                inReleaseSummary = false;
                var token = sectionMatch.Groups["sec"].Value;
                currentCategory = token.ToLowerInvariant() switch
                {
                    "added" or "новое" => ChangeCategory.Added,
                    "fixed" or "исправлено" => ChangeCategory.Fixed,
                    "security" or "безопасность" => ChangeCategory.Security,
                    "other" or "прочее" => ChangeCategory.Other,
                    _ => ChangeCategory.Changed,
                };
                continue;
            }

            var summaryLineMatch = Regex.Match(line, @"^(?:Summary|Резюме|Сводка|Кратко)\s*:\s*(?<sum>.+)$", RegexOptions.IgnoreCase);
            if (summaryLineMatch.Success)
            {
                var summaryVal = summaryLineMatch.Groups["sum"].Value.Trim();
                if (inReleaseSummary)
                {
                    releaseSummaryLines.Add(summaryVal);
                }
                else if (!string.IsNullOrWhiteSpace(summaryVal))
                {
                    groupSummaries[currentCategory] = summaryVal;
                }

                continue;
            }

            var item = Regex.Replace(line, @"^(?:[-*•·]|\d+\.)\s+", "").Trim();
            if (item.Length < 3)
            {
                continue;
            }

            if (inReleaseSummary)
            {
                releaseSummaryLines.Add(item);
                continue;
            }

            buckets[currentCategory].Add(item);
        }

        var result = new List<ChangelogGroup>();
        foreach (var category in ChangelogParseHelper.OrderedCategories)
        {
            if (buckets[category].Count > 0)
            {
                var group = new ChangelogGroup
                {
                    Category = category,
                    Title = ChangelogFormatter.CategoryTitle(category),
                    Items = buckets[category],
                };
                group.Summary = groupSummaries.TryGetValue(category, out var customSum) && !string.IsNullOrWhiteSpace(customSum)
                    ? customSum
                    : ChangelogFormatter.BuildGroupSummary(group);
                result.Add(group);
            }
        }

        var releaseSummary = releaseSummaryLines.Count > 0
            ? string.Join(" ", releaseSummaryLines)
            : result.Count > 0 ? ChangelogFormatter.BuildReleaseSummary(result) : null;

        return (result, releaseSummary);
    }

    private async Task<string?> InvokeAgyFlashTranslationAsync(string prompt, CancellationToken cancellationToken)
    {
        var executable = _fetcher.FindAgyExecutable();
        if (executable is null)
        {
            return null;
        }

        try
        {
            return await _fetcher.RunCommandAsync(
                executable,
                ["--model", "gemini-3.8-flash-low", "--effort", "low", "--disable-slash-commands", "-p", prompt],
                35,
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or Win32Exception or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }
}

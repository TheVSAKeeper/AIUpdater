using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater;

public sealed record ChangelogSource(string? PageUrl, string? DownloadUrl, string Format, string Note);

public static class ChangelogService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Loaded, string Text)> Cache = new();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 3 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AIUpdater/1.0");
        return client;
    }

    public static ChangelogSource ForStage(string id) => id switch
    {
        "Codex desktop" => new("https://developers.openai.com/codex/changelog/", "https://developers.openai.com/codex/changelog/", "html", "Общая история ChatGPT / Codex; отдельная запись для каждой сборки Desktop может отсутствовать."),
        "Active Codex CLI" or "Codex npm package" => new("https://github.com/openai/codex/releases", "https://api.github.com/repos/openai/codex/releases/latest", "github", "Последний стабильный выпуск Codex CLI, включая npm-пакет."),
        "Claude Desktop" => new("https://support.claude.com/en/articles/12138966-release-notes", "https://support.claude.com/en/articles/12138966-release-notes", "html", "Общие release notes Claude. Не каждая сборка Desktop имеет отдельное описание."),
        "Claude Code" or "Claude npm package" => new("https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md", "https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md", "markdown", "Последние записи официального changelog Claude Code, включая npm-пакет."),
        "Antigravity Desktop" => new("https://antigravity.google/docs/changelog", "https://antigravity.google/docs/changelog", "html", "Официальная история изменений Antigravity Desktop."),
        "Antigravity CLI" => new(null, null, "agy", "Источник: команда agy changelog. История встроена в установленный CLI и может не включать более новые опубликованные версии."),
        "Codex plugins" => new("https://developers.openai.com/codex/changelog/", null, "none", "У каждого плагина своя история изменений в его репозитории или каталоге. Общего changelog установленных плагинов нет. Кнопка источника открывает общие изменения Codex."),
        "Claude plugins" => new("https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md", null, "none", "У каждого плагина своя история изменений в его репозитории или каталоге. Общего changelog установленных плагинов нет. Кнопка источника открывает изменения Claude Code."),
        _ => new(null, null, "none", "Подтверждённый публичный changelog для Antigravity CLI (agy) пока не указан. История Desktop не подменяет историю CLI.")
    };

    public static async Task<string> LoadAsync(ChangelogSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Format == "agy") return await LoadAgyAsync(cancellationToken);
        if (source.DownloadUrl is null) return source.Note;
        if (Cache.TryGetValue(source.DownloadUrl, out var cached) && DateTimeOffset.UtcNow - cached.Loaded < TimeSpan.FromMinutes(15)) return cached.Text;
        var content = await Client.GetStringAsync(source.DownloadUrl, cancellationToken);
        var text = Parse(content, source.Format);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Источник не содержит доступного текста изменений.");
        Cache[source.DownloadUrl] = (DateTimeOffset.UtcNow, text);
        return text;
    }

    private static async Task<string> LoadAgyAsync(CancellationToken cancellationToken)
    {
        var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Where(directory => !string.IsNullOrWhiteSpace(directory)).Select(directory => Path.Combine(directory.Trim('"'), "agy.exe")));
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null) throw new InvalidDataException("agy не установлен или не найден.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("changelog");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error);
            if (process.ExitCode != 0) throw new InvalidDataException($"agy changelog: {await error}");
            var text = await output;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("agy не вернул историю изменений.");
            return Parse(text, "markdown");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
        catch (Win32Exception exception) { throw new InvalidDataException("Не удалось запустить agy changelog.", exception); }
    }

    public static string Parse(string content, string format)
    {
        if (format == "github")
        {
            using var document = JsonDocument.Parse(content);
            var release = document.RootElement;
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("name", out var name)
                || !release.TryGetProperty("published_at", out var date) || !release.TryGetProperty("body", out var body))
                throw new InvalidDataException("Источник не вернул описание выпуска.");
            content = $"{name.GetString()}\nОпубликовано: {date.GetString()}\n\n{body.GetString()}";
        }
        if (format == "html")
        {
            var main = Regex.Match(content, @"<main\b[^>]*>([\s\S]*?)</main>", RegexOptions.IgnoreCase);
            var articles = Regex.Matches(content, @"<article\b[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase);
            if (main.Success) content = main.Groups[1].Value;
            else if (articles.Count > 0) content = string.Join("\n\n", articles.Select(article => article.Groups[1].Value));
            else throw new InvalidDataException("Страница требует просмотра в браузере. Откройте официальный источник.");
            content = Regex.Replace(content, @"<(script|style|nav|footer)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"<(br|/p|/div|/li|/h[1-6])\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"<li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
            content = WebUtility.HtmlDecode(Regex.Replace(content, "<[^>]+>", " "));
            content = Regex.Replace(content, @"[ \t]+", " ");
            content = Regex.Replace(content, @"\n\s*\n\s*\n", "\n\n").Trim();
            if (content.Length < 100) throw new InvalidDataException("На странице нет доступного текста изменений. Откройте официальный источник.");
        }
        // Avoid putting a megabyte-long changelog into the UI.
        if (content.Length > 40000)
        {
            var end = content.LastIndexOf('\n', 40000);
            content = content[..(end > 0 ? end : 40000)] + "\n\nПолная история доступна по кнопке «Открыть источник».";
        }
        return content.Trim();
    }
}

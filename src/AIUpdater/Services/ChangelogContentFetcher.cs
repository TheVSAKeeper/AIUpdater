using AIUpdater.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace AIUpdater.Services;

public sealed class ChangelogContentFetcher : IChangelogContentFetcher
{
    private const long MaxResponseBufferBytes = 32L * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _client;

    public ChangelogContentFetcher(HttpClient? client = null)
    {
        _client = client ?? SharedClient;
    }

    public async Task<string> FetchAsync(ChangelogSource source, CancellationToken cancellationToken)
    {
        var (content, _, _) = await FetchWithETagAsync(source, null, cancellationToken);
        return content;
    }

    public async Task<(string Content, string? ETag, bool NotModified)> FetchWithETagAsync(
        ChangelogSource source,
        string? cachedETag,
        CancellationToken cancellationToken)
    {
        if (string.Equals(source.Format, "agy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.DownloadUrl, "local://agy-changelog", StringComparison.OrdinalIgnoreCase))
        {
            return (await LoadAgyChangelogAsync(cancellationToken), null, false);
        }

        if (source.DownloadUrl is null || source.DownloadUrl.StartsWith("local://", StringComparison.OrdinalIgnoreCase))
        {
            return ("", null, false);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source.DownloadUrl);
        if (!string.IsNullOrWhiteSpace(cachedETag) && EntityTagHeaderValue.TryParse(cachedETag, out var etagValue))
        {
            request.Headers.IfNoneMatch.Add(etagValue);
        }

        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return ("", cachedETag, true);
            }

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return (content, response.Headers.ETag?.ToString(), false);
        }
        catch (HttpRequestException) when ((source.Format is "github" or "github-releases" or "antigravity-cli") && source.PageUrl is not null)
        {
            var atomContent = await DownloadUrlAsync(source.PageUrl.TrimEnd('/') + ".atom", cancellationToken);
            return (atomContent, null, false);
        }
    }

    public async Task<string> DownloadUrlAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public string? FindAgyExecutable()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.Combine(directory.Trim('"'), "agy.exe"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe"));

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<string> RunCommandAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error);
            if (process.ExitCode != 0)
            {
                throw new InvalidDataException($"{Path.GetFileNameWithoutExtension(executable)}: {await error}");
            }

            return (await output).Trim();
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }

            throw;
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException($"Не удалось запустить {executable}.", exception);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
            { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = MaxResponseBufferBytes };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AIUpdater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, text/html, application/xhtml+xml, application/xml;q=0.9, */*;q=0.8");
        return client;
    }

    private async Task<string> LoadAgyChangelogAsync(CancellationToken cancellationToken)
    {
        var executable = FindAgyExecutable() ?? throw new InvalidDataException("agy не установлен или не найден.");
        var text = await RunCommandAsync(executable, ["changelog"], 20, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("agy не вернул историю изменений.");
        }

        if (text.Length > 40000)
        {
            var end = text.LastIndexOf('\n', 40000);
            text = text[..(end > 0 ? end : 40000)] + "\n\nПолная история доступна по кнопке «Открыть источник».";
        }

        return text.Trim();
    }
}

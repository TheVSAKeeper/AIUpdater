using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AIUpdater.Services;

public sealed class PowerShellProcessRunner : IUpdaterProcessRunner
{
    public async Task<int> RunAsync(
        UpdaterProcessRequest request,
        Func<string, bool, Task> onOutputChunk,
        Func<bool, Task> onStreamCompleted,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        foreach (var argument in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", request.ScriptPath,
                     "-Vendor", request.Vendor, "-LogPath", request.LogPath,
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (request.CheckOnly)
        {
            process.StartInfo.ArgumentList.Add("-CheckOnly");
        }

        process.StartInfo.ArgumentList.Add("-Action");
        process.StartInfo.ArgumentList.Add(request.Action);
        if (request.OnlyStageId is not null)
        {
            process.StartInfo.ArgumentList.Add("-OnlyStage");
            process.StartInfo.ArgumentList.Add(request.OnlyStageId);
        }

        if (!string.IsNullOrWhiteSpace(request.DotNetPackageKind))
        {
            process.StartInfo.ArgumentList.Add("-DotNetPackageKind");
            process.StartInfo.ArgumentList.Add(request.DotNetPackageKind);
            if (!string.IsNullOrWhiteSpace(request.DotNetChannel))
            {
                process.StartInfo.ArgumentList.Add("-DotNetChannel");
                process.StartInfo.ArgumentList.Add(request.DotNetChannel);
            }

            if (!string.IsNullOrWhiteSpace(request.DotNetVersion))
            {
                process.StartInfo.ArgumentList.Add("-DotNetVersion");
                process.StartInfo.ArgumentList.Add(request.DotNetVersion);
            }

            if (request.IncludePrerelease)
            {
                process.StartInfo.ArgumentList.Add("-IncludePrerelease");
            }
        }

        process.Start();
        await using var registration = cancellationToken.Register(() =>
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
        });

        try
        {
            await Task.WhenAll(
                process.WaitForExitAsync(CancellationToken.None),
                PumpStreamAsync(process.StandardOutput, false, onOutputChunk, onStreamCompleted),
                PumpStreamAsync(process.StandardError, true, onOutputChunk, onStreamCompleted));

            return process.ExitCode;
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }

            throw;
        }
    }

    private static async Task PumpStreamAsync(
        StreamReader reader,
        bool isError,
        Func<string, bool, Task> onOutputChunk,
        Func<bool, Task> onStreamCompleted)
    {
        var buffer = new char[512];
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer);
                if (count == 0)
                {
                    break;
                }

                var chunk = new string(buffer, 0, count);
                await onOutputChunk(chunk, isError);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        await onStreamCompleted(isError);
    }
}

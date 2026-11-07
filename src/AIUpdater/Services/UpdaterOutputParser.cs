using AIUpdater.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUpdater.Services;

public sealed class UpdaterOutputParser
{
    public static string BuildCheckSummary(IEnumerable<UpdateStage> stages)
    {
        var selected = stages.ToArray();
        var available = selected.Count(stage => stage.HasUpdate);
        var unknown = selected.Count(stage => stage.PackageStatus == PackageStatus.Unknown);
        var errors = selected.Count(stage => stage.PackageStatus == PackageStatus.CheckFailed);
        var absent = selected.Count(stage => stage.PackageStatus == PackageStatus.NotInstalled);
        var direct = selected.Count(stage => stage.PackageStatus == PackageStatus.DirectUpdate);
        var uncheckedCount = selected.Count(stage => stage.PackageStatus == PackageStatus.None);
        var parts = new List<string>();
        if (available > 0)
        {
            parts.Add($"Доступно обновлений: {available}");
        }

        if (unknown > 0)
        {
            parts.Add($"Не удалось сравнить: {unknown}");
        }

        if (errors > 0)
        {
            parts.Add($"Ошибок проверки: {errors}");
        }

        if (direct > 0)
        {
            parts.Add($"Обновляются напрямую: {direct}");
        }

        if (absent > 0)
        {
            parts.Add($"Не установлено: {absent}");
        }

        if (uncheckedCount > 0)
        {
            parts.Add($"Не проверено: {uncheckedCount}");
        }

        if (selected.Length == 0)
        {
            return "Нет результатов проверки";
        }

        if (parts.Count == 0)
        {
            return "Все инструменты актуальны";
        }

        if (available > 0 && available < selected.Length && unknown + errors + direct + absent + uncheckedCount == 0)
        {
            parts.Add("Остальные актуальны");
        }

        return string.Join(" · ", parts);
    }

    public void HandleChunk(
        string chunk,
        bool isError,
        VendorRun run,
        OperationState operationState,
        VendorStatus? vendorStatus = null,
        Action<string, string?>? logLine = null)
    {
        var buffer = isError ? run.ErrorBuffer : run.OutputBuffer;
        buffer.Append(chunk);

        while (true)
        {
            var separatorIndex = FindLineSeparator(buffer);
            if (separatorIndex < 0)
            {
                return;
            }

            var line = buffer.ToString(0, separatorIndex);
            var removeLength = 1;
            if (buffer[separatorIndex] == '\r' && separatorIndex + 1 < buffer.Length && buffer[separatorIndex + 1] == '\n')
            {
                removeLength = 2;
            }

            buffer.Remove(0, separatorIndex + removeLength);
            HandleLine(line, isError, run, operationState, vendorStatus, logLine);
        }
    }

    public void FlushBuffer(
        bool isError,
        VendorRun run,
        OperationState operationState,
        VendorStatus? vendorStatus = null,
        Action<string, string?>? logLine = null)
    {
        var buffer = isError ? run.ErrorBuffer : run.OutputBuffer;
        if (buffer.Length == 0)
        {
            return;
        }

        var line = buffer.ToString();
        buffer.Clear();
        HandleLine(line, isError, run, operationState, vendorStatus, logLine);
    }

    public void HandleLine(
        string line,
        bool isError,
        VendorRun run,
        OperationState operationState,
        VendorStatus? vendorStatus = null,
        Action<string, string?>? logLine = null)
    {
        var trimmed = line.TrimEnd();
        if (trimmed.Length == 0)
        {
            return;
        }

        var progressMatch = Regex.Match(trimmed, @"(?<percent>\d{1,3})%");
        if (progressMatch.Success && (trimmed.Contains('█') || trimmed.Contains('▒')))
        {
            if (vendorStatus is not null)
            {
                vendorStatus.Detail = $"{run.ActiveStage?.Name ?? "Обновление"}: {progressMatch.Groups["percent"].Value}%";
            }

            return;
        }

        if (trimmed is "-" or "\\" or "|" or "/")
        {
            return;
        }

        var payload = Regex.Match(trimmed, @"^\[\d{2}:\d{2}:\d{2}\] UI_RESULT (?<json>.+)$");
        if (payload.Success)
        {
            try
            {
                var result = JsonSerializer.Deserialize<UiResultPayload>(payload.Groups["json"].Value);
                var stage = run.Stages.FirstOrDefault(item => item.Vendor == run.Vendor && item.Id == result?.Stage);
                if (stage is null || result is null || !UpdateStage.TryParseCheckResult(result.Result, out _))
                {
                    throw new JsonException("Неизвестный компонент или результат");
                }

                stage.SetCheckResult(result.Result, result.Installed, result.Available, info: result.Info);
                run.ResultStages.Add(stage.Id);
            }
            catch (JsonException exception)
            {
                run.SawError = true;
                run.ActiveStage?.SetFailed();
                logLine?.Invoke($"Не удалось прочитать результат проверки: {exception.Message}", run.Vendor);
            }

            return;
        }

        logLine?.Invoke(trimmed, run.Vendor);
        UpdateStageFromLine(trimmed, isError, run, operationState, vendorStatus);
    }

    private static void UpdateStageFromLine(
        string line,
        bool isError,
        VendorRun run,
        OperationState operationState,
        VendorStatus? vendorStatus)
    {
        _ = isError;
        var result = Regex.Match(line, @"=== (?<result>COMPLETED|FAILED): (?<stage>.+) ===");
        if (result.Success)
        {
            var finishedStage = run.Stages.FirstOrDefault(item => item.Vendor == run.Vendor && item.Id == result.Groups["stage"].Value);
            if (result.Groups["result"].Value == "FAILED")
            {
                finishedStage?.SetFailed();
                run.SawError = true;
            }
            else if (finishedStage?.OperationState != OperationState.Failed)
            {
                finishedStage?.SetCompleted();
            }

            return;
        }

        var stage = run.Stages.FirstOrDefault(item => item.Vendor == run.Vendor && line.Contains($"=== {item.Id} ===", StringComparison.OrdinalIgnoreCase));
        if (stage is not null)
        {
            if (run.ActiveStage is { OperationState: not OperationState.Failed })
            {
                run.ActiveStage.SetCompleted();
            }

            run.ActiveStage = stage;
            stage.SetRunning(operationState);
            if (vendorStatus is not null)
            {
                vendorStatus.Detail = stage.Name;
            }

            return;
        }

        if (line.Contains("Done with errors", StringComparison.OrdinalIgnoreCase))
        {
            run.SawError = true;
            return;
        }

        if (line.Contains(" failed:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("update failed for ", StringComparison.OrdinalIgnoreCase))
        {
            run.SawError = true;
            run.ActiveStage?.SetFailed();
        }
    }

    private static int FindLineSeparator(StringBuilder buffer)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] is '\r' or '\n')
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record UiResultPayload(
        string Stage,
        string Result,
        string? Installed,
        string? Available,
        string? Info = null);
}

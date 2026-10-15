using AIUpdater.Infrastructure;
using AIUpdater.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AIUpdater.Views;

public partial class MainWindow
{
    private readonly string _scriptPath;
    private readonly string _logPath;
    private readonly string _resultsPath;
    private readonly List<Process> _processes = [];
    private UpdateStage[] _runStages = [];
    private bool _isRunning;
    private bool _stopRequested;
    private bool _checkOnly;
    private string _action = "Update";
    private ChangelogWindow? _changelogWindow;
    private UpdateStage? _requestedStage;
    private bool _sawError;

    public MainWindow()
    {
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        InitializeComponent();
        Title = $"AI Updater v{typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}";
        DataContext = this;

        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUpdater", "Logs");
        Directory.CreateDirectory(logDirectory);
        _scriptPath = Path.Combine(AppContext.BaseDirectory, "Update-Codex.ps1");
        _logPath = Path.Combine(logDirectory, "Update-Codex-last.log");
        LogFolderButton.ToolTip = $"Скрипт: {_scriptPath}\nЖурналы: {logDirectory}";

        _resultsPath = Path.Combine(logDirectory, "LastResults.json");
        foreach (var stage in Stages)
        {
            stage.PropertyChanged += (_, _) => RefreshUpdateButton();
        }

        foreach (var vendor in Stages.Select(stage => stage.Vendor).Distinct())
        {
            var status = new VendorStatus(vendor, Path.Combine(logDirectory, $"Update-Codex-{vendor}-last.log"))
                { Stages = Stages.Where(stage => stage.Vendor == vendor).ToArray() };

            Vendors.Add(status);
            AddVendorTab(status);
            if (File.Exists(status.LogPath))
            {
                try { status.Log = "История прошлого запуска\n" + File.ReadAllText(status.LogPath, Encoding.UTF8); }
                catch (IOException) { }
            }
        }

        LoadLastLog();
        LoadResults();
        RefreshUpdateButton();
    }

    public ObservableCollection<VendorStatus> Vendors { get; } = [];

    public ObservableCollection<UpdateStage> Stages { get; } =
    [
        new() { Id = "Codex desktop", Vendor = "OpenAI", Name = "ChatGPT / Codex" },
        new() { Id = "Active Codex CLI", Vendor = "OpenAI", Name = "Codex CLI" },
        new() { Id = "Codex npm package", Vendor = "OpenAI", Name = "Codex npm" },
        new() { Id = "Codex plugins", Vendor = "OpenAI", Name = "Codex плагины" },
        new() { Id = "Claude Desktop", Vendor = "Anthropic", Name = "Claude Desktop" },
        new() { Id = "Claude Code", Vendor = "Anthropic", Name = "Claude Code" },
        new() { Id = "Claude npm package", Vendor = "Anthropic", Name = "Claude npm" },
        new() { Id = "Claude plugins", Vendor = "Anthropic", Name = "Claude плагины" },
        new() { Id = "Antigravity Desktop", Vendor = "Google", Name = "Antigravity Desktop" },
        new() { Id = "Antigravity CLI", Vendor = "Google", Name = "Antigravity CLI (agy)" },
    ];

    private string OperationName => _action switch
    {
        "Install" => "Установка",
        "Uninstall" => "Удаление",
        _ => _checkOnly ? "Проверка" : "Обновление",
    };

    private void ChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        var vendor = (LogTabs.SelectedItem as TabItem)?.DataContext as VendorStatus;
        OpenChangelog(vendor?.Stages.FirstOrDefault());
    }

    private void ChangelogMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateStage stage })
        {
            OpenChangelog(stage);
        }
    }

    private void CardMenuButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not DependencyObject element)
        {
            return;
        }

        while (element is not null)
        {
            if (element is Border { ContextMenu: { } menu } card)
            {
                menu.PlacementTarget = card;
                menu.IsOpen = true;
                return;
            }

            element = VisualTreeHelper.GetParent(element);
        }
    }

    private async void CheckComponent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateStage stage })
        {
            await RunUpdaterAsync(stage);
        }
    }

    private async void UpdateComponent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateStage stage })
        {
            await RunUpdaterAsync(stage, checkOnly: false);
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = Stages.Where(stage => stage.CanSelectForUpdate && stage.IsSelectedForUpdate).ToArray();
        if (selected.Length > 0)
        {
            await RunUpdaterAsync(null, checkOnly: false, selected: selected);
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUpdaterAsync(null);
    }

    private async void StageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: UpdateStage stage })
        {
            if (stage.CanSelectForUpdate)
            {
                stage.IsSelectedForUpdate = !stage.IsSelectedForUpdate;
                return;
            }

            await RunUpdaterAsync(stage);
        }
    }

    private async void ComponentAction_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning
            || sender is not MenuItem { DataContext: UpdateStage stage, Tag: string action }
            || !stage.CanManageInstallation)
        {
            return;
        }

        if (action == "Uninstall" && !AppDialog.Show(this, $"Удалить {stage.Name}?", "Удаление компонента", true))
        {
            return;
        }

        await RunUpdaterAsync(stage, action);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRunning)
        {
            return;
        }

        _stopRequested = true;
        foreach (var process in _processes.ToArray())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception exception) { AppendLine($"Не удалось остановить процесс: {exception.Message}"); }
        }

        AppendLine("Остановка запрошена пользователем.");
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = (LogTabs.SelectedItem as TabItem)?.DataContext is VendorStatus vendor
            ? vendor.LogPath
            : _logPath;

        if (!File.Exists(selectedPath))
        {
            AppDialog.Show(this, "Лог последнего запуска ещё не создан.", "AI Updater");
            return;
        }

        Process.Start(new ProcessStartInfo(selectedPath) { UseShellExecute = true });
    }

    private void LogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_logPath)!) { UseShellExecute = true });
    }

    private static string CheckSummary(IEnumerable<UpdateStage> stages)
    {
        var selected = stages.ToArray();
        var available = selected.Count(stage => stage.CheckResult == "AVAILABLE");
        var unknown = selected.Count(stage => stage.CheckResult == "UNKNOWN");
        var errors = selected.Count(stage => stage.CheckResult == "ERROR");
        var absent = selected.Count(stage => stage.CheckResult == "NOT_INSTALLED");
        var direct = selected.Count(stage => stage.CheckResult == "DIRECT");
        var uncheckedCount = selected.Count(stage => stage.CheckResult is null);
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

    private void AddVendorTab(VendorStatus vendor)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = vendor.Name, Margin = new(0, 0, 8, 0) });
        var status = new TextBlock();
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(VendorStatus.Status)));
        status.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(VendorStatus.StatusBrush)));
        header.Children.Add(status);
        var content = new DockPanel();
        var detail = new TextBlock { Margin = new(12), TextWrapping = TextWrapping.Wrap };
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(VendorStatus.Detail)));
        DockPanel.SetDock(detail, Dock.Top);
        content.Children.Add(detail);
        var log = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, Padding = new(14),
            Background = new SolidColorBrush(Color.FromRgb(12, 23, 18)),
            Foreground = new SolidColorBrush(Color.FromRgb(204, 228, 214)),
            FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        log.SetBinding(TextBox.TextProperty, new Binding(nameof(VendorStatus.Log)) { Mode = BindingMode.OneWay });
        log.TextChanged += (_, _) => log.ScrollToEnd();
        content.Children.Add(log);
        LogTabs.Items.Add(new TabItem { Header = header, Content = content, DataContext = vendor });
    }

    private void OpenChangelog(UpdateStage? stage)
    {
        if (_changelogWindow is not null)
        {
            if (stage is not null)
            {
                _changelogWindow.SelectStage(stage);
            }

            _changelogWindow.Activate();
            return;
        }

        _changelogWindow = new(Stages, stage) { Owner = this };
        _changelogWindow.Closed += (_, _) => _changelogWindow = null;
        _changelogWindow.Show();
    }

    private void RefreshUpdateButton()
    {
        var count = Stages.Count(stage => stage.CanSelectForUpdate && stage.IsSelectedForUpdate);
        UpdateButton.Content = $"Обновить выбранные ({count})";
        UpdateButton.IsEnabled = !_isRunning && count > 0;
    }

    private async Task RunUpdaterAsync(UpdateStage? requestedStage, string action = "Update", bool checkOnly = true, UpdateStage[]? selected = null)
    {
        if (_isRunning)
        {
            return;
        }

        if (!File.Exists(_scriptPath))
        {
            AppDialog.Show(this, $"Скрипт не найден:\n{_scriptPath}", "AI Updater");
            return;
        }

        _action = action;
        _checkOnly = action == "Update" && checkOnly;
        _runStages = selected ?? (requestedStage is null ? Stages.ToArray() : [requestedStage]);
        try
        {
            PrepareRun(requestedStage);
            File.WriteAllText(_logPath, $"AI update run: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);
            var vendors = _runStages.Select(stage => stage.Vendor).Distinct().ToArray();
            var results = await Task.WhenAll(vendors.Select(async vendor =>
            {
                if (selected is null)
                {
                    return await RunVendorAsync(vendor, requestedStage);
                }

                var result = 0;
                foreach (var stage in _runStages.Where(stage => stage.Vendor == vendor))
                {
                    if (_stopRequested)
                    {
                        break;
                    }

                    if (await RunVendorAsync(vendor, stage) != 0)
                    {
                        result = 1;
                    }
                }

                return result;
            }));

            FinishRun(results.All(code => code == 0) ? 0 : 1);
        }
        catch (Exception exception)
        {
            AppendLine(exception.ToString());
            _sawError = true;
            FinishRun(-1);
        }
    }

    private async Task<int> RunVendorAsync(string vendor, UpdateStage? requestedStage)
    {
        var run = new VendorRun(vendor);
        var vendorStatus = Vendors.First(item => item.Name == vendor);
        vendorStatus.SetStatus(OperationName, $"{OperationName}: запуск", (Brush)FindResource("AccentTextBrush"));
        var temporaryLog = Path.Combine(Path.GetTempPath(), $"AIUpdater-{Guid.NewGuid():N}.log");
        using var process = new Process();
        process.StartInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", _scriptPath,
                     "-Vendor", vendor, "-LogPath", temporaryLog,
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (_checkOnly)
        {
            process.StartInfo.ArgumentList.Add("-CheckOnly");
        }

        process.StartInfo.ArgumentList.Add("-Action");
        process.StartInfo.ArgumentList.Add(_action);
        if (requestedStage is not null)
        {
            process.StartInfo.ArgumentList.Add("-OnlyStage");
            process.StartInfo.ArgumentList.Add(requestedStage.Id);
        }

        var exitCode = -1;
        try
        {
            process.Start();
            _processes.Add(process);
            await Task.WhenAll(process.WaitForExitAsync(),
                PumpOutputAsync(process.StandardOutput, false, run),
                PumpOutputAsync(process.StandardError, true, run));

            exitCode = process.ExitCode;
        }
        catch (Exception exception)
        {
            run.SawError = true;
            AppendLine(exception.Message, vendor);
            if (_processes.Contains(process) && !process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync();
            }
        }
        finally
        {
            _processes.Remove(process);
            if (exitCode == 0 && !_stopRequested)
            {
                foreach (var stage in _runStages.Where(stage => stage.Vendor == vendor && (requestedStage is null || stage == requestedStage) && !run.ResultStages.Contains(stage.Id)))
                {
                    stage.SetFailed();
                    run.SawError = true;
                    AppendLine($"Скрипт не передал результат: {stage.Name}", vendor);
                }
            }

            foreach (var stage in Stages.Where(stage => stage.Vendor == vendor && (requestedStage is null || stage == requestedStage) && stage.OperationState is OperationState.Queued or OperationState.Checking or OperationState.Installing or OperationState.Updating or OperationState.Uninstalling))
            {
                if (exitCode == 0 && !run.SawError && !_stopRequested)
                {
                    stage.SetCompleted();
                }
                else
                {
                    stage.SetFailed(_stopRequested);
                }
            }

            var successful = exitCode == 0 && !run.SawError && !_stopRequested;
            vendorStatus.SetStatus(_stopRequested ? "Остановлено" : successful ? "Готово" : "Ошибка",
                _stopRequested ? "Остановка запрошена пользователем" : successful ? $"{OperationName}: завершено" : "Завершено с ошибками" + (exitCode == 0 ? "" : $" (код {exitCode})"),
                _stopRequested ? Brushes.LightSlateGray : successful ? (Brush)FindResource("AccentTextBrush") : Brushes.LightCoral);

            if (_checkOnly && successful)
            {
                var selected = Stages.Where(stage => stage.Vendor == vendor && (requestedStage is null || stage == requestedStage)).ToArray();
                var available = selected.Any(stage => stage.CheckResult == "AVAILABLE");
                var unknown = selected.Any(stage => stage.CheckResult == "UNKNOWN");
                var errors = selected.Any(stage => stage.CheckResult == "ERROR");
                vendorStatus.SetStatus(errors ? "Ошибка проверки" : available ? "Есть обновления" : unknown ? "Проверено частично" : "Проверено",
                    CheckSummary(selected), errors ? Brushes.LightCoral : available ? Brushes.Gold : (Brush)FindResource("AccentTextBrush"));
            }

            AppendLine(vendorStatus.Detail, vendor);
            _sawError |= run.SawError;
            try { File.Delete(temporaryLog); }
            catch (IOException) { }
        }

        return exitCode == 0 && run.SawError ? 1 : exitCode;
    }

    private void PrepareRun(UpdateStage? requestedStage)
    {
        _sawError = false;
        _isRunning = true;
        _stopRequested = false;
        _requestedStage = requestedStage;
        LogTextBox.Clear();
        foreach (var stage in Stages)
        {
            if (_runStages.Contains(stage))
            {
                stage.Queue();
            }
        }

        foreach (var vendor in Vendors.Where(item => _runStages.Any(stage => stage.Vendor == item.Name)))
        {
            vendor.Log = "";
            File.WriteAllText(vendor.LogPath, $"{vendor.Name}: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);
            vendor.SetStatus("Ожидание", "Подготовка к запуску", Brushes.LightSlateGray);
        }

        OverallStatusText.Text = _checkOnly
            ? requestedStage is null ? "Проверка версий без установки обновлений" : $"Только проверка: {requestedStage.Name}"
            : requestedStage is null
                ? "Проверка и установка всех обновлений"
                : $"Обновление: {requestedStage.Name}";

        if (_action != "Update")
        {
            OverallStatusText.Text = $"{OperationName}: {requestedStage?.Name}";
        }

        RunProgressBar.Visibility = Visibility.Visible;
        RunButton.IsEnabled = false;
        RefreshUpdateButton();
        StageItemsControl.IsEnabled = false;
        StopButton.Visibility = Visibility.Visible;
        StopButton.IsEnabled = true;
    }

    private async Task PumpOutputAsync(StreamReader reader, bool isError, VendorRun run)
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
                await Dispatcher.InvokeAsync(() => HandleOutputChunk(chunk, isError, run));
            }
        }
        catch (IOException)
        {
            // The stream closes immediately when the user stops the updater.
        }
        catch (ObjectDisposedException)
        {
            // The process can dispose redirected streams during shutdown.
        }

        await Dispatcher.InvokeAsync(() => FlushOutputBuffer(isError, run));
    }

    private void HandleOutputChunk(string chunk, bool isError, VendorRun run)
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
            HandleOutputLine(line, isError, run);
        }
    }

    private void FlushOutputBuffer(bool isError, VendorRun run)
    {
        var buffer = isError ? run.ErrorBuffer : run.OutputBuffer;
        if (buffer.Length == 0)
        {
            return;
        }

        var line = buffer.ToString();
        buffer.Clear();
        HandleOutputLine(line, isError, run);
    }

    private void HandleOutputLine(string line, bool isError, VendorRun run)
    {
        var trimmed = line.TrimEnd();
        if (trimmed.Length == 0)
        {
            return;
        }

        var progressMatch = Regex.Match(trimmed, @"(?<percent>\d{1,3})%");
        if (progressMatch.Success && (trimmed.Contains('█') || trimmed.Contains('▒')))
        {
            Vendors.First(item => item.Name == run.Vendor).Detail = $"{run.ActiveStage?.Name ?? "Обновление"}: {progressMatch.Groups["percent"].Value}%";
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
                var result = JsonSerializer.Deserialize<PackageResult>(payload.Groups["json"].Value);
                var stage = _runStages.FirstOrDefault(item => item.Vendor == run.Vendor && item.Id == result?.Stage);
                if (stage is null || result is null || result.Result is not ("AVAILABLE" or "CURRENT" or "UNKNOWN" or "NOT_INSTALLED" or "DIRECT" or "ERROR"))
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
                AppendLine($"Не удалось прочитать результат проверки: {exception.Message}", run.Vendor);
            }

            return;
        }

        AppendLine(trimmed, run.Vendor);
        UpdateStageFromLine(trimmed, isError, run);
    }

    private void UpdateStageFromLine(string line, bool isError, VendorRun run)
    {
        var result = Regex.Match(line, @"=== (?<result>COMPLETED|FAILED): (?<stage>.+) ===");
        if (result.Success)
        {
            var finishedStage = _runStages.FirstOrDefault(item => item.Vendor == run.Vendor && item.Id == result.Groups["stage"].Value);
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

        var stage = _runStages.FirstOrDefault(item => item.Vendor == run.Vendor && line.Contains($"=== {item.Id} ===", StringComparison.OrdinalIgnoreCase));
        if (stage is not null)
        {
            if (run.ActiveStage is { OperationState: not OperationState.Failed })
            {
                run.ActiveStage.SetCompleted();
            }

            run.ActiveStage = stage;
            stage.SetRunning(_action switch
            {
                "Install" => OperationState.Installing,
                "Uninstall" => OperationState.Uninstalling,
                _ => _checkOnly ? OperationState.Checking : OperationState.Updating,
            });

            Vendors.First(item => item.Name == run.Vendor).Detail = stage.Name;
            return;
        }

        if (line.Contains("Done with errors", StringComparison.OrdinalIgnoreCase))
        {
            run.SawError = true;
            return;
        }

        if (isError || line.Contains(" failed:", StringComparison.OrdinalIgnoreCase) || line.Contains("update failed for ", StringComparison.OrdinalIgnoreCase))
        {
            run.SawError = true;
            run.ActiveStage?.SetFailed();
        }
    }

    private void FinishRun(int exitCode)
    {
        var success = exitCode == 0 && !_sawError && !_stopRequested;
        OverallStatusText.Text = success
            ? _requestedStage is null ? "Все проверки завершены" : $"Проверка завершена: {_requestedStage.Name}"
            : _stopRequested
                ? "Обновление остановлено"
                : $"Завершено с ошибками (код {exitCode})";

        if (_checkOnly && success)
        {
            OverallStatusText.Text = CheckSummary(Stages.Where(stage => _requestedStage is null || stage == _requestedStage));
        }
        else if (success)
        {
            OverallStatusText.Text = $"{OperationName}: завершено" + (_requestedStage is null ? "" : $" – {_requestedStage.Name}");
        }

        RunProgressBar.Visibility = Visibility.Hidden;
        RunButton.IsEnabled = true;

        StageItemsControl.IsEnabled = true;
        StopButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Collapsed;
        _isRunning = false;
        foreach (var stage in _runStages.Where(stage => stage.OperationState is OperationState.Queued or OperationState.Checking or OperationState.Installing or OperationState.Updating or OperationState.Uninstalling))
        {
            stage.SetFailed(_stopRequested);
        }

        foreach (var vendor in Vendors)
        {
            RefreshVendorSummary(vendor);
        }

        RefreshUpdateButton();
        SaveResults();
    }

    private void RefreshVendorSummary(VendorStatus vendor)
    {
        var errors = vendor.Stages.Count(stage => stage.OperationState == OperationState.Failed || stage.PackageStatus == PackageStatus.CheckFailed);
        var stopped = vendor.Stages.Count(stage => stage.OperationState == OperationState.Stopped);
        var updates = vendor.Stages.Count(stage => stage.HasUpdate);
        vendor.SetStatus(errors > 0 ? $"Ошибок: {errors}" : stopped > 0 ? "Остановлено" : updates > 0 ? $"Обновлений: {updates}" : "",
            CheckSummary(vendor.Stages), errors > 0 ? Brushes.LightCoral : updates > 0 ? Brushes.Gold : Brushes.LightSlateGray);
    }

    private void LoadResults()
    {
        if (!File.Exists(_resultsPath))
        {
            return;
        }

        try
        {
            var results = JsonSerializer.Deserialize<PackageResult[]>(File.ReadAllText(_resultsPath, Encoding.UTF8));
            foreach (var result in results ?? [])
            {
                if (result is null || result.CheckedAt is null || result.Result is not ("AVAILABLE" or "CURRENT" or "UNKNOWN" or "NOT_INSTALLED" or "DIRECT" or "ERROR"))
                {
                    continue;
                }

                Stages.FirstOrDefault(stage => stage.Id == result.Stage)?.SetCheckResult(result.Result, result.Installed, result.Available, result.CheckedAt, result.Info);
            }

            foreach (var vendor in Vendors)
            {
                RefreshVendorSummary(vendor);
            }

            if (Stages.Any(stage => stage.CheckedAt is not null))
            {
                OverallStatusText.Text = "Показаны последние результаты. Нажмите «Проверить всё», чтобы обновить их.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppendLine($"Не удалось загрузить результаты: {exception.Message}");
        }
    }

    private void SaveResults()
    {
        try
        {
            var results = Stages.Where(stage => stage.CheckResult is not null)
                .Select(stage => new PackageResult(stage.Id, stage.CheckResult!, stage.InstalledVersion, stage.AvailableVersion, stage.CheckedAt, stage.Info))
                .ToArray();

            File.WriteAllText(_resultsPath + ".tmp", JsonSerializer.Serialize(results), Encoding.UTF8);
            File.Move(_resultsPath + ".tmp", _resultsPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppendLine($"Не удалось сохранить результаты: {exception.Message}");
        }
    }

    private void AppendLine(string line, string? vendorName = null)
    {
        if (vendorName is not null)
        {
            var vendor = Vendors.First(item => item.Name == vendorName);
            vendor.Log += line + Environment.NewLine;
            if (_isRunning)
            {
                try { File.AppendAllText(vendor.LogPath, line + Environment.NewLine, Encoding.UTF8); }
                catch (IOException) { }
            }

            line = $"[{vendorName}] {line}";
        }

        if (_isRunning)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8); }
            catch (IOException)
            {
                /* Live output remains available if the log is locked. */
            }
        }

        LogTextBox.AppendText(line + Environment.NewLine);
        LogTextBox.ScrollToEnd();
    }

    private void LoadLastLog()
    {
        if (!File.Exists(_logPath))
        {
            return;
        }

        try
        {
            LogTextBox.Text = "История прошлого запуска. Карточки показывают отдельно сохранённые результаты.\n" + File.ReadAllText(_logPath, Encoding.UTF8);
            LogTextBox.ScrollToEnd();
        }
        catch
        {
            // The updater may currently own the file; live output will still work.
        }
    }

    private sealed record PackageResult(string Stage, string Result, string? Installed, string? Available, DateTimeOffset? CheckedAt = null, string? Info = null);

    private sealed class VendorRun(string vendor)
    {
        public string Vendor { get; } = vendor;
        public UpdateStage? ActiveStage { get; set; }
        public bool SawError { get; set; }
        public HashSet<string> ResultStages { get; } = [];
        public StringBuilder OutputBuffer { get; } = new();
        public StringBuilder ErrorBuffer { get; } = new();
    }
}

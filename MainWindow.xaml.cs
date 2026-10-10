using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AIUpdater;

public partial class MainWindow
{
    private readonly string _scriptPath;
    private readonly string _logPath;
    private readonly List<Process> _processes = [];
    public ObservableCollection<VendorStatus> Vendors { get; } = [];
    private bool _isRunning;
    private bool _stopRequested;
    private bool _checkOnly;
    private sealed class VendorRun(string vendor)
    {
        public string Vendor { get; } = vendor;
        public UpdateStage? ActiveStage { get; set; }
        public bool SawError { get; set; }
        public StringBuilder OutputBuffer { get; } = new();
        public StringBuilder ErrorBuffer { get; } = new();
    }
    private UpdateStage? _requestedStage;
    private bool _sawError;

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
        new() { Id = "Antigravity CLI", Vendor = "Google", Name = "Antigravity CLI (agy)" }
    ];

    public MainWindow()
    {
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        InitializeComponent();
        DataContext = this;

        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUpdater", "Logs");
        Directory.CreateDirectory(logDirectory);
        _scriptPath = Path.Combine(AppContext.BaseDirectory, "Update-Codex.ps1");
        _logPath = Path.Combine(logDirectory, "Update-Codex-last.log");
        ScriptPathText.ToolTip = $"Скрипт: {_scriptPath}\nЖурналы: {logDirectory}";

        foreach (var stage in Stages)
        {
            stage.Reset();
        }

        foreach (var vendor in Stages.Select(stage => stage.Vendor).Distinct())
        {
            var status = new VendorStatus(vendor, Path.Combine(logDirectory, $"Update-Codex-{vendor}-last.log"))
            { Stages = Stages.Where(stage => stage.Vendor == vendor).ToArray() };
            Vendors.Add(status);
            AddVendorTab(status);
            if (File.Exists(status.LogPath))
            {
                try { status.Log = File.ReadAllText(status.LogPath, Encoding.UTF8); }
                catch (IOException) { }
            }
        }
        LoadLastLog();
    }

    private void AddVendorTab(VendorStatus vendor)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = vendor.Name, Margin = new Thickness(0, 0, 8, 0) });
        var status = new TextBlock();
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(VendorStatus.Status)));
        status.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(VendorStatus.StatusBrush)));
        header.Children.Add(status);
        var content = new DockPanel();
        var detail = new TextBlock { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(VendorStatus.Detail)));
        DockPanel.SetDock(detail, Dock.Top);
        content.Children.Add(detail);
        var log = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromRgb(12, 23, 18)),
            Foreground = new SolidColorBrush(Color.FromRgb(204, 228, 214)),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        log.SetBinding(TextBox.TextProperty, new Binding(nameof(VendorStatus.Log)) { Mode = BindingMode.OneWay });
        log.TextChanged += (_, _) => log.ScrollToEnd();
        content.Children.Add(log);
        LogTabs.Items.Add(new TabItem { Header = header, Content = content, DataContext = vendor });
    }

    private void CheckMode_Changed(object sender, RoutedEventArgs e)
    {
        if (RunButton is null || ModeDescriptionText is null) return;
        var checkOnly = CheckOnlyCheckBox.IsChecked == true;
        RunButton.Content = checkOnly ? "Проверить всё" : "Обновить всё";
        ModeDescriptionText.Text = checkOnly
            ? "Сравнить версии без установки обновлений"
            : "Проверить и установить обновления; приложения могут быть перезапущены";
    }

    private static string CheckSummary(IEnumerable<UpdateStage> stages)
    {
        var selected = stages.ToArray();
        var available = selected.Count(stage => stage.CheckResult == "AVAILABLE");
        var unknown = selected.Count(stage => stage.CheckResult == "UNKNOWN");
        var absent = selected.Count(stage => stage.CheckResult == "NOT_INSTALLED");
        return $"Обновлений: {available} · Не удалось сравнить: {unknown} · Не установлено: {absent}";
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUpdaterAsync(null);
    }

    private async void StageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateStage stage })
        {
            await RunUpdaterAsync(stage);
        }
    }

    private async Task RunUpdaterAsync(UpdateStage? requestedStage)
    {
        if (_isRunning) return;
        if (!File.Exists(_scriptPath))
        {
            MessageBox.Show(this, $"Скрипт не найден:\n{_scriptPath}", "AI Updater", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _checkOnly = CheckOnlyCheckBox.IsChecked == true;
        PrepareRun(requestedStage);
        try
        {
            File.WriteAllText(_logPath, $"AI update run: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);
            var vendors = requestedStage is null
                ? Stages.Select(stage => stage.Vendor).Distinct().ToArray()
                : [requestedStage.Vendor];
            var results = await Task.WhenAll(vendors.Select(vendor => RunVendorAsync(vendor, requestedStage)));
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
        vendorStatus.SetStatus(_checkOnly ? "Проверка" : "Обновление", "Запуск проверки обновлений", (Brush)FindResource("AccentTextBrush"));
        var temporaryLog = Path.Combine(Path.GetTempPath(), $"AIUpdater-{Guid.NewGuid():N}.log");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", _scriptPath,
                     "-Vendor", vendor, "-LogPath", temporaryLog })
            process.StartInfo.ArgumentList.Add(argument);
        if (_checkOnly) process.StartInfo.ArgumentList.Add("-CheckOnly");
        if (requestedStage is not null)
        {
            process.StartInfo.ArgumentList.Add("-OnlyStage");
            process.StartInfo.ArgumentList.Add(requestedStage.Id);
        }
        var exitCode = -1;
        try
        {
            File.WriteAllText(vendorStatus.LogPath, $"{vendor}: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}" + Environment.NewLine, Encoding.UTF8);
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
            foreach (var stage in Stages.Where(stage => stage.Vendor == vendor &&
                         (requestedStage is null || stage == requestedStage) &&
                         stage.Status is "Выполняется" or "Ожидание"))
            {
                if (exitCode == 0 && !run.SawError && !_stopRequested) stage.SetCompleted();
                else stage.SetFailed();
            }
            var successful = exitCode == 0 && !run.SawError && !_stopRequested;
            vendorStatus.SetStatus(_stopRequested ? "Остановлено" : successful ? "Готово" : "Ошибка",
                _stopRequested ? "Остановка запрошена пользователем" : successful ? "Все проверки завершены" : $"Завершено с ошибками (код {exitCode})",
                _stopRequested ? Brushes.LightSlateGray : successful ? (Brush)FindResource("AccentTextBrush") : Brushes.LightCoral);
            if (_checkOnly && successful)
            {
                var selected = Stages.Where(stage => stage.Vendor == vendor && (requestedStage is null || stage == requestedStage)).ToArray();
                var available = selected.Any(stage => stage.CheckResult == "AVAILABLE");
                var unknown = selected.Any(stage => stage.CheckResult == "UNKNOWN");
                vendorStatus.SetStatus(available ? "Есть обновления" : unknown ? "Проверено частично" : "Проверено",
                    CheckSummary(selected), available ? Brushes.Gold : (Brush)FindResource("AccentTextBrush"));
            }
            AppendLine(vendorStatus.Detail, vendor);
            _sawError |= run.SawError;
            try { File.Delete(temporaryLog); } catch (IOException) { }
        }
        return exitCode;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRunning) return;
        _stopRequested = true;
        foreach (var process in _processes.ToArray())
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (Exception exception) { AppendLine($"Не удалось остановить процесс: {exception.Message}"); }
        }
        AppendLine("Остановка запрошена пользователем.");
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = (LogTabs.SelectedItem as TabItem)?.DataContext is VendorStatus vendor
            ? vendor.LogPath : _logPath;
        if (!File.Exists(selectedPath))
        {
            MessageBox.Show(this, "Лог последнего запуска ещё не создан.", "AI Updater", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(selectedPath) { UseShellExecute = true });
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
            if (requestedStage is null || stage == requestedStage) stage.Reset();
        }
        foreach (var vendor in Vendors.Where(item => requestedStage is null || item.Name == requestedStage.Vendor))
        {
            vendor.Log = "";
            vendor.SetStatus("Ожидание", "Подготовка к запуску", Brushes.LightSlateGray);
        }

        OverallStatusText.Text = _checkOnly
            ? requestedStage is null ? "Проверка версий без установки обновлений" : $"Только проверка: {requestedStage.Name}"
            : requestedStage is null ? "Проверка и установка всех обновлений" : $"Обновление: {requestedStage.Name}";
        RunProgressBar.Visibility = Visibility.Visible;
        RunButton.IsEnabled = false;
        CheckOnlyCheckBox.IsEnabled = false;
        StageItemsControl.IsEnabled = false;
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

        AppendLine(trimmed, run.Vendor);
        UpdateStageFromLine(trimmed, isError, run);
    }

    private void UpdateStageFromLine(string line, bool isError, VendorRun run)
    {
        var check = Regex.Match(line, @"=== CHECK (?<result>AVAILABLE|CURRENT|UNKNOWN|NOT_INSTALLED): (?<stage>.+) ===");
        if (check.Success)
        {
            var checkedStage = Stages.FirstOrDefault(item => item.Vendor == run.Vendor && item.Id == check.Groups["stage"].Value);
            checkedStage?.SetCheckResult(check.Groups["result"].Value);
            return;
        }
        var result = Regex.Match(line, @"=== (?<result>COMPLETED|FAILED): (?<stage>.+) ===");
        if (result.Success)
        {
            var finishedStage = Stages.FirstOrDefault(item => item.Id == result.Groups["stage"].Value);
            if (result.Groups["result"].Value == "FAILED")
            {
                finishedStage?.SetFailed();
                run.SawError = true;
            }
            else if (finishedStage?.Status != "Ошибка" && finishedStage?.CheckResult is null) finishedStage?.SetCompleted();
            return;
        }
        var stage = Stages.FirstOrDefault(item => line.Contains($"=== {item.Id} ===", StringComparison.OrdinalIgnoreCase));
        if (stage is not null)
        {
            if (run.ActiveStage?.Status == "Выполняется") run.ActiveStage.SetCompleted();
            run.ActiveStage = stage;
            stage.SetRunning();
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
            : _stopRequested ? "Обновление остановлено" : $"Завершено с ошибками (код {exitCode})";
        if (_checkOnly && success)
            OverallStatusText.Text = CheckSummary(Stages.Where(stage => _requestedStage is null || stage == _requestedStage));
        RunProgressBar.Visibility = Visibility.Hidden;
        RunButton.IsEnabled = true;
        CheckOnlyCheckBox.IsEnabled = true;
        StageItemsControl.IsEnabled = true;
        StopButton.IsEnabled = false;
        _isRunning = false;
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
            catch (IOException) { /* Live output remains available if the log is locked. */ }
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
            LogTextBox.Text = File.ReadAllText(_logPath, Encoding.UTF8);
            LogTextBox.ScrollToEnd();
        }
        catch
        {
            // The updater may currently own the file; live output will still work.
        }
    }
}

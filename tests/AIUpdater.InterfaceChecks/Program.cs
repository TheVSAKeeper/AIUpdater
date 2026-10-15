using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using AIUpdater;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static object? Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Await(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static async Task AwaitIdle(MainWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((bool)typeof(MainWindow).GetField("_isRunning", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!)
            await Task.Delay(20, timeout.Token);
    }

    private static void Render(FrameworkElement view, string path, int width, int height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CheckStyles(string output)
    {
        foreach (var type in new[] { typeof(Button), typeof(CheckBox), typeof(TextBox), typeof(ToolTip), typeof(ContextMenu), typeof(MenuItem), typeof(Separator), typeof(TabControl), typeof(TabItem), typeof(ListBox), typeof(ListBoxItem), typeof(ScrollViewer), typeof(ScrollBar), typeof(ProgressBar) })
        {
            var style = Application.Current.TryFindResource(type) as Style;
            Check(style is not null && style.Setters.OfType<Setter>().Any(setter => setter.Property == Control.TemplateProperty), $"Missing application template: {type.Name}");
        }
        var samples = new StackPanel();
        var boxes = new[]
        {
            new CheckBox { Content = "Не выбрано", IsChecked = false },
            new CheckBox { Content = "Выбрано", IsChecked = true },
            new CheckBox { Content = "Частичный выбор", IsThreeState = true, IsChecked = null },
            new CheckBox { Content = "Недоступно", IsEnabled = false },
            new CheckBox { Content = "Выбрано, недоступно", IsChecked = true, IsEnabled = false }
        };
        foreach (var box in boxes) { box.Padding = new Thickness(8); samples.Children.Add(box); }
        var text = new TextBox { IsReadOnly = true, Text = "Тёмное текстовое поле с выделением и меню копирования", Padding = new Thickness(10), Margin = new Thickness(0, 12, 0, 0) };
        samples.Children.Add(text);
        var gallery = new Border { Background = (Brush)Application.Current.FindResource("WindowBrush"), Padding = new Thickness(20), Child = samples };
        Render(gallery, Path.Combine(output, "checkbox-states.png"), 560, 300);
        var emptyBorder = (Border)boxes[0].Template.FindName("CheckBorder", boxes[0]);
        var checkedBorder = (Border)boxes[1].Template.FindName("CheckBorder", boxes[1]);
        Check(((SolidColorBrush)emptyBorder.Background).Color == Color.FromRgb(11, 19, 14), "Unchecked checkbox has a light background");
        Check(checkedBorder.Background == Application.Current.FindResource("AccentBrush"), "Checked checkbox has no accent fill");
        Check(((UIElement)boxes[1].Template.FindName("CheckMark", boxes[1])).Visibility == Visibility.Visible, "Checked glyph is missing");
        Check(((UIElement)boxes[2].Template.FindName("MixedMark", boxes[2])).Visibility == Visibility.Visible, "Indeterminate glyph is missing");
        Check(boxes[3].Opacity < boxes[0].Opacity, "Disabled checkbox looks active");
        var toggle = (IToggleProvider)new CheckBoxAutomationPeer(boxes[0]).GetPattern(PatternInterface.Toggle)!;
        toggle.Toggle();
        Check(boxes[0].IsChecked == true, "Custom checkbox broke the standard toggle action");
        text.SelectAll();
        Check(text.ContextMenu is { Items.Count: 2 }, "Text menu fell back to system commands");
        var textMenu = text.ContextMenu!;
        textMenu.PlacementTarget = text;
        textMenu.ApplyTemplate();
        textMenu.Measure(new Size(260, 120));
        textMenu.Arrange(new Rect(0, 0, 260, 120));
        textMenu.UpdateLayout();
        Check(((MenuItem)textMenu.Items[0]).CommandTarget == text, "Copy command is not bound to the text field");
        Render(textMenu, Path.Combine(output, "text-menu.png"), 260, 90);
        var tooltip = new ToolTip { Content = "Причина последней проверки. Подсказка использует ту же тёмную палитру." };
        Render(tooltip, Path.Combine(output, "tooltip.png"), 350, 70);
        var changelog = new ChangelogWindow([]);
        var list = (ListBox)changelog.FindName("ComponentList");
        list.ItemsSource = new[] { new { Name = "ChatGPT / Codex" }, new { Name = "Claude Code" }, new { Name = "Antigravity CLI" } };
        list.SelectedIndex = 0;
        ((TextBox)changelog.FindName("ChangelogTextBox")).Text = "Пример содержимого окна «Что нового».\nПроверка тёмных рамок, списка, выделения и полосы прокрутки.";
        Render((FrameworkElement)changelog.Content, Path.Combine(output, "changelog.png"), 920, 600);
        Check(Descendants<ListBoxItem>(list).All(item => item.Template is not null), "Changelog rows lost their template");
        changelog.Close();
        var dialogType = typeof(App).Assembly.GetType("AIUpdater.AppDialog")!;
        var dialog = (Window)dialogType.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { null, "Удалить выбранный компонент?", "Удаление компонента", true })!;
        ((Grid)dialog.Content).Background = dialog.Background;
        Render((FrameworkElement)dialog.Content, Path.Combine(output, "confirmation.png"), 520, 170);
        var dialogButtons = Descendants<Button>((DependencyObject)dialog.Content).ToArray();
        Check(dialogButtons.Single(button => Equals(button.Content, "Отмена")) is { IsDefault: true, IsCancel: true }, "Delete confirmation lost its safe default");
        Check(!dialogButtons.Single(button => Equals(button.Content, "Удалить")).IsDefault, "Delete became the default action");
        dialog.Close();
    }

    private static void CheckIcon(string output)
    {
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/AIUpdater;component/Assets/AIUpdater.ico")).Stream;
        var decoder = BitmapDecoder.Create(resource, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        Check(decoder.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(sizes), "ICO is missing native Windows sizes");
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 20, 17)), null, new Rect(0, 0, 620, 300));
            context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 150, 620, 150));
            var offset = 12;
            foreach (var frame in decoder.Frames.Where(frame => frame.PixelWidth <= 128))
            {
                var pixels = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var buffer = new byte[pixels.PixelWidth * pixels.PixelHeight * 4];
                pixels.CopyPixels(buffer, pixels.PixelWidth * 4, 0);
                Check(buffer[3] == 0, "Icon corners lost transparency");
                Check(buffer.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 200), "ICO frame is empty");
                context.DrawImage(frame, new Rect(offset, 12, frame.PixelWidth, frame.PixelHeight));
                context.DrawImage(frame, new Rect(offset, 162, frame.PixelWidth, frame.PixelHeight));
                offset += frame.PixelWidth + 18;
            }
        }
        var bitmap = new RenderTargetBitmap(620, 300, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, "icon-sizes.png"));
        encoder.Save(stream);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var app = new App();
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new MainWindow();
        Set(window, "_resultsPath", Path.Combine(output, "results.json"));
        foreach (var item in window.Stages) item.InvalidateResult();
        var stage = window.Stages.Single(item => item.Id == "Claude Code");
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        stage.Queue();
        stage.SetRunning(OperationState.Checking);
        Check(stage.HasUpdate && stage.InstalledVersion == "2.1.299", "Queue/check lost the package result");
        stage.SetFailed();
        Check(stage.HasUpdate && stage.VersionDisplayText == "v2.1.299 → v2.2.1", "Failure lost known versions");
        stage.SetCompleted();
        Check(stage.Status == "Есть обновление", "Completion hid the package result");
        window.Stages[3].SetCheckResult("DIRECT", info: "Плагинов: 2");
        Check(window.Stages[3].CanUpdate, "Unsupported comparison blocked an explicit plugin update");
        Check(window.Stages[3].CanSelectForUpdate && !window.Stages[3].IsSelectedForUpdate, "Direct update was selected automatically");
        Check(window.Stages[3].Status == "Обновляется напрямую" && window.Stages[3].VersionDisplayText == "Плагинов: 2", "Direct update has no explanation");
        Check(stage.VersionBrush == Brushes.LightSlateGray && stage.VersionTargetBrush == Brushes.Gold, "Update versions have ambiguous colors");
        Call(window, "StageButton_Click", new Button { DataContext = stage }, new RoutedEventArgs());
        Check(!stage.IsSelectedForUpdate, "Card click did not deselect an available update");
        Call(window, "StageButton_Click", new Button { DataContext = stage }, new RoutedEventArgs());
        Check(stage.IsSelectedForUpdate, "Card click did not select an available update");
        Set(window, "_isRunning", true);
        Call(window, "StageButton_Click", new Button { DataContext = stage }, new RoutedEventArgs());
        Check(stage.IsSelectedForUpdate, "Busy card changed selection");
        Set(window, "_isRunning", false);
        var summaryMethod = typeof(MainWindow).GetMethod("CheckSummary", BindingFlags.NonPublic | BindingFlags.Static)!;
        var summary = (string)summaryMethod.Invoke(null, new object[] { new[] { stage } })!;
        Check(summary == "Доступно обновлений: 1", "Summary retained zero counters");
        var updateButton = (Button)window.FindName("UpdateButton");
        Check(updateButton.IsEnabled && updateButton.Content.ToString()!.EndsWith("(1)"), "Selection count did not refresh");
        Call(window, "StageButton_Click", new Button { DataContext = window.Stages[3] }, new RoutedEventArgs());
        Check(window.Stages[3].IsSelectedForUpdate && updateButton.Content.ToString()!.EndsWith("(2)"), "Direct card click did not join the bulk selection");
        Call(window, "StageButton_Click", new Button { DataContext = window.Stages[3] }, new RoutedEventArgs());
        stage.IsSelectedForUpdate = false;
        Check(!updateButton.IsEnabled, "Deselection still permits bulk updates");
        Set(window, "_runStages", new[] { stage });
        var runType = typeof(MainWindow).GetNestedType("VendorRun", BindingFlags.NonPublic)!;
        var run = Activator.CreateInstance(runType, "Anthropic")!;
        var line = "[12:34:56] UI_RESULT {\"Stage\":\"Claude Code\",\"Result\":\"CURRENT\",\"Installed\":\"2.2.1\",\"Available\":\"2.2.1\"}\r\n";
        Call(window, "HandleOutputChunk", line[..25], false, run);
        Call(window, "HandleOutputChunk", line[25..], false, run);
        Check(stage.Status == "Актуально" && stage.InstalledVersion == "2.2.1" && !stage.HasUpdate, "Chunked JSON result failed");
        var pluginStage = window.Stages[3];
        Set(window, "_runStages", new[] { pluginStage });
        var pluginRun = Activator.CreateInstance(runType, "OpenAI")!;
        Call(window, "HandleOutputLine", "[12:34:56] UI_RESULT {\"Stage\":\"Codex plugins\",\"Result\":\"DIRECT\",\"Info\":\"Плагинов: 2\"}", false, pluginRun);
        Check(pluginStage.CheckResult == "DIRECT" && pluginStage.Info == "Плагинов: 2", "Structured direct result was not accepted");
        var npmStage = window.Stages[2];
        Set(window, "_runStages", new[] { npmStage });
        Call(window, "HandleOutputLine", "[12:34:56] UI_RESULT {\"Stage\":\"Codex npm package\",\"Result\":\"ERROR\",\"Installed\":\"1.0.0\",\"Info\":\"Не удалось получить доступную версию\"}", false, pluginRun);
        Check(npmStage.Status == "Ошибка проверки" && npmStage.StatusBrush == Brushes.LightCoral && npmStage.InstalledVersion == "1.0.0", "Check failure was confused with absence");
        window.Stages[6].SetCheckResult("NOT_INSTALLED");
        Check(window.Stages[6].Status == "Не установлено" && !window.Stages[6].CanUpdate, "Absent package still permits Update");
        var errorSummary = (string)summaryMethod.Invoke(null, new object[] { new[] { npmStage, window.Stages[6] } })!;
        Check(errorSummary == "Ошибок проверки: 1 · Не установлено: 1", "Summary mixed absence and check failures");
        Set(window, "_runStages", new[] { stage });
        Check(!((TextBox)window.FindName("LogTextBox")).Text.Contains("UI_RESULT"), "Protocol leaked into the visible journal");
        Call(window, "HandleOutputLine", "[12:34:56] UI_RESULT {\"Stage\":\"Codex desktop\",\"Result\":\"CURRENT\"}", false, run);
        Check((bool)runType.GetProperty("SawError")!.GetValue(run)!, "Cross-vendor result was accepted");
        Call(window, "HandleOutputLine", "[12:34:56] UI_RESULT {bad json}", false, run);
        Check(stage.InstalledVersion == "2.2.1", "Malformed JSON destroyed the last valid result");
        Call(window, "SaveResults");
        stage.InvalidateResult();
        Call(window, "LoadResults");
        Check(stage.Status == "Актуально" && stage.CheckedAt is not null && stage.InstalledVersion == "2.2.1", "Snapshot round trip failed");
        Check(window.Stages[3].CheckResult == "DIRECT" && window.Stages[3].Info == "Плагинов: 2", "Direct-update details were not persisted");
        Check(!window.Stages[3].IsSelectedForUpdate, "Restored direct update was automatically selected");
        Check(npmStage.CheckResult == "ERROR" && npmStage.Info == "Не удалось получить доступную версию", "Check error was not persisted");
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        window.Stages[0].SetCheckResult("CURRENT", "26.1013.1");
        window.Stages[4].SetCheckResult("NOT_INSTALLED");
        window.Stages[9].SetCheckResult("DIRECT", "1.3.5");
        Check(window.Stages[9].VersionBrush == Brushes.LightSlateGray, "Uncompared version looks current");
        foreach (var vendor in window.Vendors) Call(window, "RefreshVendorSummary", vendor);
        var root = (FrameworkElement)window.Content;
        foreach (var (width, height) in new[] { (1072, 740), (932, 620) })
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            var tabs = (FrameworkElement)window.FindName("LogTabs");
            Check(tabs.TranslatePoint(new Point(0, tabs.ActualHeight), root).Y <= root.ActualHeight,
                $"Journal extends below the window: {width}x{height}, bottom={tabs.TranslatePoint(new Point(0, tabs.ActualHeight), root).Y}, available={root.ActualHeight}");
            Check(Descendants<Button>(root).Count(button => Equals(button.Content, "⋮")) == 10, "Missing visible card menus");
            Check(Descendants<Border>(root).Count(border => border.ContextMenu is not null) == 10, "Card context menus missing");
            var directChoices = Descendants<CheckBox>(root).Where(box => box.DataContext is UpdateStage { PackageStatus: PackageStatus.DirectUpdate }).ToArray();
            Check(directChoices.Length == 2 && directChoices.All(box => box.Visibility == Visibility.Visible && box.IsChecked == false), "Direct-update checkboxes are hidden or checked by default");
            var cards = Descendants<Border>(root).Where(border => border.ContextMenu is not null).ToArray();
            Check(cards.Length == 10 && cards.Max(card => card.ActualWidth) - cards.Min(card => card.ActualWidth) < 1, "Vendor card grid widths differ");
            foreach (var row in Descendants<Button>(root).Where(button => Equals(button.Tag, "Stage")).GroupBy(button => ((UpdateStage)button.DataContext).Vendor))
            {
                var positions = row.Select(button => Descendants<TextBlock>(button).First().TranslatePoint(new Point(0, 0), root).Y).ToArray();
                Check(positions.Max() - positions.Min() < 1, "Card titles are not aligned");
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"interface-{width}.png"));
            encoder.Save(file);
            var stopButton = (Button)window.FindName("StopButton");
            stopButton.Visibility = Visibility.Visible;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Check(tabs.TranslatePoint(new Point(0, tabs.ActualHeight), root).Y <= root.ActualHeight, "Busy layout extends below the window");
            stopButton.Visibility = Visibility.Collapsed;
        }
        Check(((Button)window.FindName("StopButton")).Visibility == Visibility.Collapsed, "Idle stop button is visible");
        var fixture = Path.Combine(output, "fixture.ps1");
        var calls = Path.Combine(output, "calls.txt");
        File.WriteAllText(calls, "");
        File.Delete(Path.Combine(output, "fail"));
        File.Delete(Path.Combine(output, "wait"));
        File.Delete(Path.Combine(output, "missing"));
        File.Delete(Path.Combine(output, "error-result"));
        File.WriteAllText(fixture, """
            param($Vendor, $LogPath, $Action, $OnlyStage, [switch]$CheckOnly)
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
            Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value "$Action|$OnlyStage|$CheckOnly"
            Write-Output "[12:34:56] === $OnlyStage ==="
            if (Test-Path (Join-Path $PSScriptRoot 'fail')) { exit 5 }
            if (Test-Path (Join-Path $PSScriptRoot 'missing')) { exit 0 }
            if (Test-Path (Join-Path $PSScriptRoot 'wait')) { Start-Sleep -Seconds 30 }
            $result = if (Test-Path (Join-Path $PSScriptRoot 'error-result')) { 'ERROR' } else { 'CURRENT' }
            $json = @{ Stage = $OnlyStage; Result = $result; Installed = '2.2.1'; Available = '2.2.1' } | ConvertTo-Json -Compress
            Write-Output "[12:34:56] UI_RESULT $json"
            Write-Output "[12:34:56] === COMPLETED: $OnlyStage ==="
            """);
        Set(window, "_scriptPath", fixture);
        Set(window, "_logPath", Path.Combine(output, "main.log"));
        foreach (var vendor in window.Vendors) Set(vendor, "<LogPath>k__BackingField", Path.Combine(output, vendor.Name + ".log"));
        var other = window.Stages.Single(item => item.Id == "Claude npm package");
        var untouched = window.Stages[0].InstalledVersion;
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        other.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        Await((Task)Call(window, "RunUpdaterAsync", null!, "Update", false, new[] { stage, other })!);
        Check(File.ReadAllLines(calls).SequenceEqual(new[] { "Update|Claude Code|False", "Update|Claude npm package|False" }), "Bulk update dispatched unselected stages or changed mode");
        Check(stage.Status == "Актуально" && other.Status == "Актуально", "Post-update results missing");
        Check(window.Stages[0].InstalledVersion == untouched, "Selected update reset another vendor");
        File.WriteAllText(calls, "");
        stage.SetCheckResult("AVAILABLE", "2.1.299", "2.2.1");
        var selectedPlugin = window.Stages[7];
        selectedPlugin.SetCheckResult("DIRECT", info: "Плагинов: 10");
        Call(window, "StageButton_Click", new Button { DataContext = selectedPlugin }, new RoutedEventArgs());
        Call(window, "UpdateButton_Click", updateButton, new RoutedEventArgs());
        Await(AwaitIdle(window));
        Check(File.ReadAllLines(calls).SequenceEqual(new[] { "Update|Claude Code|False", "Update|Claude plugins|False" }), "Bulk button omitted the selected direct update or included unchecked cards");
        File.WriteAllText(Path.Combine(output, "fail"), "");
        Await((Task)Call(window, "RunUpdaterAsync", stage, "Update", false, null!)!);
        Check(stage.OperationState == OperationState.Failed && other.Status == "Актуально", "Failed update affected another card");
        Check(stage.InstalledVersion == "2.2.1", "Failed update lost the last known version");
        File.Delete(Path.Combine(output, "fail"));
        File.WriteAllText(Path.Combine(output, "missing"), "");
        Await((Task)Call(window, "RunUpdaterAsync", stage, "Update", false, null!)!);
        Check(stage.OperationState == OperationState.Failed, "Missing result was reported successful");
        File.Delete(Path.Combine(output, "missing"));
        File.WriteAllText(Path.Combine(output, "wait"), "");
        var stopped = (Task)Call(window, "RunUpdaterAsync", null!, "Update", false, new[] { stage, other })!;
        Call(window, "StopButton_Click", window, new RoutedEventArgs());
        Await(stopped);
        Check(stage.OperationState == OperationState.Stopped && other.OperationState == OperationState.Stopped, "Stop did not finish running/queued cards");
        File.Delete(Path.Combine(output, "wait"));
        File.WriteAllText(Path.Combine(output, "error-result"), "");
        Await((Task)Call(window, "RunUpdaterAsync", stage, "Update", true, null!)!);
        Check(stage.Status == "Ошибка проверки" && ((TextBlock)window.FindName("OverallStatusText")).Text.Contains("Ошибок проверки: 1"), "Check completion overwrote its failure result");
        File.Delete(Path.Combine(output, "error-result"));
        CheckStyles(output);
        CheckIcon(output);
        window.Close();
        app.Shutdown();
        Console.WriteLine("PASS: independent states, selection, chunked JSON, vendor guard, malformed payload, persistence, two WPF widths, selected dispatch, post-update check, failure isolation, stop running/queued cards");
        Console.WriteLine("PASS: application control templates, checkbox states and accessible toggle, text menu, changelog layout, themed confirmation with safe default");
        Console.WriteLine("PASS: packaged ICO sizes, transparency and nonempty frames");
    }
}

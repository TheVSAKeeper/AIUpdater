using AIUpdater.Infrastructure;
using AIUpdater.Models;
using AIUpdater.Services;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class ChangelogWindow : Window
{
    private CancellationTokenSource? _loadCancellation;
    private ChangelogSource? _source;

    public ChangelogWindow(IEnumerable<UpdateStage> stages, UpdateStage? selected = null)
    {
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        InitializeComponent();
        var items = stages.ToArray();
        ComponentList.ItemsSource = items;
        Closed += (_, _) =>
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
        };

        ComponentList.SelectedItem = selected ?? items.FirstOrDefault();
    }

    public void SelectStage(UpdateStage stage)
    {
        ComponentList.SelectedItem = stage;
    }

    private async void ComponentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComponentList.SelectedItem is not UpdateStage stage)
        {
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = _loadCancellation = new();
        var source = _source = ChangelogService.ForStage(stage.Id);
        SourceNoteText.Text = source.Note;
        SourceButton.IsEnabled = source.PageUrl is not null;
        SourceButton.ToolTip = source.PageUrl;
        ChangelogTextBox.Clear();
        var canLoad = source.DownloadUrl is not null || source.Format == "agy";
        LoadStatusText.Text = canLoad ? "Загрузка изменений…" : "";
        try
        {
            var text = await ChangelogService.LoadAsync(source, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ChangelogTextBox.Text = text;
            ChangelogTextBox.ScrollToHome();
            LoadStatusText.Text = source.Format == "agy" ? "История из установленного agy"
                : canLoad ? "Изменения из официального источника" : "Отдельная история недоступна";
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                LoadStatusText.Text = "Источник не ответил вовремя. Можно открыть его в браузере.";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or JsonException)
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            LoadStatusText.Text = "Не удалось загрузить изменения. Можно открыть официальный источник.";
            ChangelogTextBox.Text = exception.Message;
        }
    }

    private void SourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_source?.PageUrl is not { } url)
        {
            return;
        }

        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception) { AppDialog.Show(this, exception.Message, "Не удалось открыть источник"); }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

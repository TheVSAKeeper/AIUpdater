using AIUpdater.Infrastructure;
using AIUpdater.Models;
using AIUpdater.Services;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace AIUpdater.Views;

public partial class ChangelogWindow : Window
{
    private const double DefaultFontSize = 14;
    private readonly UpdateStage[] _allStages;
    private readonly ICollectionView? _stagesView;
    private readonly HashSet<string> _collapsedGroupKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collapsedReleaseKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _originalOverrideReleaseKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _translationErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _translationTimer;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _translationCancellation;
    private ChangelogSource? _source;
    private ChangelogDocument? _currentDocument;
    private UpdateStage? _currentStage;
    private TextBlock? _activeInlineProgressText;
    private string? _translatingReleaseKey;
    private string? _translatingReleaseTitle;
    private int _translatingGroupCount;
    private int _translatingItemCount;
    private DateTimeOffset _translationStartedAt;
    private double _baseFontSize = DefaultFontSize;
    private bool _showTranslation = true;
    private bool _suppressTextSync;

    public ChangelogWindow(IEnumerable<UpdateStage> stages, UpdateStage? selected = null)
    {
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        InitializeComponent();
        _translationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _translationTimer.Tick += (_, _) => UpdateTranslationBannerLiveText();

        UpdateZoomLabel();
        UpdateLanguageButton();
        _allStages = stages.ToArray();
        if (_allStages.Length > 0)
        {
            var cvs = new CollectionViewSource { Source = _allStages };
            cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(UpdateStage.Vendor)));
            cvs.Filter += StagesView_Filter;
            _stagesView = cvs.View;
            ComponentList.ItemsSource = _stagesView;
        }
        else
        {
            ComponentList.DisplayMemberPath = "Name";
            ComponentList.ItemsSource = _allStages;
        }

        Closed += (_, _) =>
        {
            _translationTimer.Stop();
            CancelAndDispose(ref _translationCancellation);
            CancelAndDispose(ref _loadCancellation);
        };

        _currentStage = selected ?? _allStages.FirstOrDefault();
        ComponentList.SelectedItem = _currentStage;
    }

    public void SelectStage(UpdateStage stage)
    {
        ComponentList.SelectedItem = stage;
    }

    private static void CancelSafe(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void CancelAndDispose(ref CancellationTokenSource? slot)
    {
        var cts = Interlocked.Exchange(ref slot, null);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void StagesView_Filter(object sender, FilterEventArgs e)
    {
        if (e.Item is not UpdateStage stage)
        {
            e.Accepted = true;
            return;
        }

        if (InstalledOnlyCheckBox.IsChecked == true && !IsInstalledStage(stage))
        {
            e.Accepted = false;
            return;
        }

        var query = ComponentSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            e.Accepted = true;
            return;
        }

        e.Accepted = stage.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || stage.Vendor.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || stage.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || stage.VersionDisplayText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInstalledStage(UpdateStage stage)
    {
        if (stage.PackageStatus == PackageStatus.NotInstalled)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(stage.InstalledVersion)
               || stage.PackageStatus is PackageStatus.UpToDate or PackageStatus.UpdateAvailable or PackageStatus.DirectUpdate;
    }

    private void InstalledOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _stagesView?.Refresh();
        if (_stagesView is null)
        {
            return;
        }

        if (ComponentList.SelectedItem is null)
        {
            if (_currentStage is not null && _stagesView.Cast<object>().Contains(_currentStage))
            {
                ComponentList.SelectedItem = _currentStage;
            }
            else if (InstalledOnlyCheckBox.IsChecked == true)
            {
                var first = _stagesView.Cast<object>().FirstOrDefault();
                if (first is not null)
                {
                    ComponentList.SelectedItem = first;
                }
            }
        }
    }

    private void ComponentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ComponentSearchPlaceholder.Visibility = string.IsNullOrEmpty(ComponentSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _stagesView?.Refresh();
        if (ComponentList.SelectedItem is null && _currentStage is not null && _stagesView is not null)
        {
            if (_stagesView.Cast<object>().Contains(_currentStage))
            {
                ComponentList.SelectedItem = _currentStage;
            }
        }
    }

    private async void ComponentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComponentList.SelectedItem is not UpdateStage stage)
        {
            return;
        }

        if (ReferenceEquals(stage, _currentStage) && _currentDocument is not null)
        {
            return;
        }

        await LoadStageChangelogAsync(stage, false);
    }

    private async void RefreshChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStage is not null)
        {
            await LoadStageChangelogAsync(_currentStage, true);
        }
    }

    private void LanguageToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _showTranslation = !_showTranslation;
        _originalOverrideReleaseKeys.Clear();
        UpdateLanguageButton();
        if (_currentDocument is not null)
        {
            RenderDocument(_currentDocument, _currentStage);
        }
    }

    private void CancelTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        CancelSafe(_translationCancellation);
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        SetBaseFontSize(_baseFontSize - 1);
    }

    private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
    {
        SetBaseFontSize(DefaultFontSize);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        SetBaseFontSize(_baseFontSize + 1);
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        SetBaseFontSize(_baseFontSize + (e.Delta > 0 ? 1 : -1));
        e.Handled = true;
    }

    private void SetBaseFontSize(double size)
    {
        _baseFontSize = Math.Clamp(size, 11, 22);
        ChangelogTextBox.FontSize = _baseFontSize;
        UpdateZoomLabel();
        if (_currentDocument is not null)
        {
            RenderDocument(_currentDocument, _currentStage);
        }
    }

    private void UpdateZoomLabel()
    {
        var percent = (int)Math.Round(_baseFontSize / DefaultFontSize * 100);
        ZoomResetButton.Content = $"{percent}%";
    }

    private void UpdateLanguageButton()
    {
        var translatedCount = _currentDocument?.Releases.Count(r => r.HasTranslation) ?? 0;
        LanguageToggleButton.Content = _showTranslation
            ? translatedCount > 0 ? $"Язык: Русский (ИИ · {translatedCount})" : "Язык: Русский (ИИ)"
            : "Язык: Оригинал";
    }

    private async Task LoadStageChangelogAsync(UpdateStage stage, bool forceRefresh)
    {
        _currentStage = stage;
        CancelAndDispose(ref _translationCancellation);
        StopTranslationBanner();

        CancelAndDispose(ref _loadCancellation);
        var cancellation = _loadCancellation = new();
        var source = _source = ChangelogService.ForStage(stage.Id, stage.InstalledVersion ?? stage.AvailableVersion);
        SourceNoteText.Text = source.Note;
        SourceButton.IsEnabled = source.PageUrl is not null;
        SourceButton.ToolTip = source.PageUrl;

        var cached = ChangelogService.TryLoadCachedDocument(stage.Id);
        if (cached is not null)
        {
            _currentDocument = cached;
            InitializeDefaultCollapsedReleases(cached);
            RenderDocument(cached, stage);
            LoadStatusText.Text = "Показан сохранённый журнал · проверка новых выпусков…";
        }
        else
        {
            _currentDocument = null;
            ReleaseCardsPanel.Children.Clear();
            SetRawText("");
            LoadStatusText.Text = "Загрузка истории изменений…";
        }

        try
        {
            var (document, fromCacheOnly) = await ChangelogService.LoadDocumentAsync(stage, source, forceRefresh, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _currentDocument = document;
            InitializeDefaultCollapsedReleases(document);
            RenderDocument(document, stage);
            ReleasesScrollViewer.ScrollToHome();

            var latest = document.Releases.FirstOrDefault();
            var statusPrefix = fromCacheOnly
                ? $"Из кэша ({document.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}, выпусков: {document.Releases.Count})"
                : $"Загружено выпусков: {document.Releases.Count}";

            if (latest is not null && !latest.HasTranslation && !IsReleaseAlreadyRussian(latest))
            {
                LoadStatusText.Text = statusPrefix + " · Обработка свежего выпуска через Antigravity Flash…";
                await TranslateSpecificReleaseAsync(document, latest, stage, false, statusPrefix, cancellation.Token);
            }
            else if (latest is { HasTranslation: true })
            {
                LoadStatusText.Text = statusPrefix + " · Свежий выпуск обработан ИИ";
            }
            else
            {
                LoadStatusText.Text = statusPrefix;
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested && _currentDocument is null)
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

            if (_currentDocument is not null)
            {
                LoadStatusText.Text = "Показан сохранённый журнал (источник сейчас недоступен).";
                return;
            }

            LoadStatusText.Text = "Не удалось загрузить изменения. Можно открыть официальный источник.";
            ShowFallbackText(exception.Message);
        }
    }

    public async Task<bool> TranslateReleaseOnDemandAsync(ChangelogRelease release, bool forceRetranslate = false)
    {
        if (_currentDocument is null)
        {
            return false;
        }

        var statusPrefix = $"Выпусков: {_currentDocument.Releases.Count}";
        return await TranslateSpecificReleaseAsync(_currentDocument, release, _currentStage, forceRetranslate, statusPrefix, CancellationToken.None);
    }

    private async Task<bool> TranslateSpecificReleaseAsync(
        ChangelogDocument document,
        ChangelogRelease release,
        UpdateStage? stage,
        bool forceRetranslate,
        string statusPrefix,
        CancellationToken parentToken)
    {
        CancelAndDispose(ref _translationCancellation);
        CancellationTokenSource linked;
        try
        {
            linked = parentToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
                : new CancellationTokenSource();
        }
        catch (ObjectDisposedException)
        {
            linked = new CancellationTokenSource();
        }

        _translationCancellation = linked;
        var key = GetReleaseKey(release);
        _collapsedReleaseKeys.Remove(key);
        _translationErrors.Remove(key);
        _translatingReleaseKey = key;
        _translatingReleaseTitle = !string.IsNullOrWhiteSpace(release.Title) ? release.Title : $"v{release.Version}";
        _translatingGroupCount = release.Groups.Count;
        _translatingItemCount = release.Groups.Sum(g => g.Items.Count);
        _translationStartedAt = DateTimeOffset.UtcNow;
        StartTranslationBanner();
        RenderDocument(document, stage);

        try
        {
            var translated = await ChangelogService.TranslateReleaseAsync(document, release, linked.Token, forceRetranslate);
            if (linked.IsCancellationRequested)
            {
                return false;
            }

            StopTranslationBanner();
            if (translated)
            {
                _showTranslation = true;
                _originalOverrideReleaseKeys.Remove(key);
                RenderDocument(document, stage);
                LoadStatusText.Text = $"{statusPrefix} · Выпуск «{_translatingReleaseTitle}» обработан (Flash)";
                return true;
            }

            _translationErrors[key] = "Не удалось обработать релиз через Antigravity CLI (agy). Проверьте доступность agy и повторите попытку.";
            RenderDocument(document, stage);
            LoadStatusText.Text = $"{statusPrefix} · Не удалось обработать выпуск";
            return false;
        }
        catch (OperationCanceledException)
        {
            StopTranslationBanner();
            if (!parentToken.IsCancellationRequested)
            {
                _translationErrors[key] = "Обработка выпуска отменена.";
                RenderDocument(document, stage);
                LoadStatusText.Text = $"{statusPrefix} · Обработка отменена";
            }

            return false;
        }
        catch (Exception exception)
        {
            StopTranslationBanner();
            _translationErrors[key] = $"Ошибка при обработке выпуска: {exception.Message}";
            RenderDocument(document, stage);
            LoadStatusText.Text = $"{statusPrefix} · Ошибка обработки выпуска";
            return false;
        }
        finally
        {
            if (_translatingReleaseKey == key)
            {
                StopTranslationBanner();
            }

            if (ReferenceEquals(_translationCancellation, linked))
            {
                _translationCancellation = null;
            }

            try
            {
                linked.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void StartTranslationBanner()
    {
        TranslationBannerBorder.Visibility = Visibility.Collapsed;
        _translationTimer.Start();
    }

    private void StopTranslationBanner()
    {
        _translationTimer.Stop();
        _activeInlineProgressText = null;
        _translatingReleaseKey = null;
        TranslationBannerBorder.Visibility = Visibility.Collapsed;
    }

    private void UpdateTranslationBannerLiveText()
    {
        if (_translatingReleaseKey is null)
        {
            return;
        }

        var elapsed = Math.Max(0, (int)(DateTimeOffset.UtcNow - _translationStartedAt).TotalSeconds);
        if (_activeInlineProgressText is not null)
        {
            _activeInlineProgressText.Text = $"Обработка через Antigravity Flash ({elapsed} сек) · разделов: {_translatingGroupCount}, пунктов: {_translatingItemCount} · генерация резюме и перевода…";
        }
    }

    private void InitializeDefaultCollapsedReleases(ChangelogDocument document)
    {
        for (var index = 0; index < document.Releases.Count; index++)
        {
            var release = document.Releases[index];
            var key = GetReleaseKey(release);
            var totalItems = release.Groups.Sum(g => g.Items.Count);
            if (index >= 2 && totalItems > 4)
            {
                _collapsedReleaseKeys.Add(key);
            }
        }
    }

    public void RenderDocument(ChangelogDocument document, UpdateStage? stage = null)
    {
        _currentDocument = document;
        if (stage is not null)
        {
            _currentStage = stage;
            if (!ReferenceEquals(ComponentList.SelectedItem, stage) && (_stagesView is null || _stagesView.Cast<object>().Contains(stage)))
            {
                ComponentList.SelectedItem = stage;
            }

            var source = _source = ChangelogService.ForStage(stage.Id, stage.InstalledVersion ?? stage.AvailableVersion);
            SourceNoteText.Text = source.Note;
            SourceButton.IsEnabled = source.PageUrl is not null;
            SourceButton.ToolTip = source.PageUrl;
        }

        ChangelogService.EnsureSummaries(document.Releases);
        UpdateLanguageButton();
        _activeInlineProgressText = null;
        ReleaseCardsPanel.Children.Clear();

        for (var index = 0; index < document.Releases.Count; index++)
        {
            var release = document.Releases[index];
            ReleaseCardsPanel.Children.Add(CreateReleaseCard(release, index, stage));
        }

        ReleasesScrollViewer.Visibility = Visibility.Visible;
        ChangelogTextBox.Visibility = Visibility.Collapsed;
        SetRawText(ChangelogService.FormatAsPlainText(document.Releases, _showTranslation));
    }
}

using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace AIUpdater.ViewModels;

public sealed class ChangelogViewModel : ObservableObject, IDisposable
{
    public const double DefaultFontSize = 14;
    private readonly IChangelogService _changelogService;
    private readonly IDialogService? _dialogService;
    private readonly UpdateStage[] _allStages;
    private readonly CollectionViewSource? _stagesViewSource;
    private readonly ChangelogPresentationState _state;
    private bool _suppressTextSync;

    [ActivatorUtilitiesConstructor]
    public ChangelogViewModel(IChangelogService changelogService, IDialogService dialogService)
        : this(UpdateStage.CreateCatalog(), null, changelogService, dialogService)
    {
    }

    public ChangelogViewModel(
        IEnumerable<UpdateStage> stages,
        UpdateStage? selected = null,
        IChangelogService? changelogService = null,
        IDialogService? dialogService = null)
    {
        _changelogService = changelogService ?? new ChangelogService();
        _dialogService = dialogService;
        _allStages = stages.ToArray();
        _state = new ChangelogPresentationState(UpdateTranslationBannerLiveText);

        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
        RefreshCommand = new AsyncRelayCommand(RefreshCurrentStageAsync);
        ZoomInCommand = new RelayCommand(() => SetBaseFontSize(BaseFontSize + 1));
        ZoomOutCommand = new RelayCommand(() => SetBaseFontSize(BaseFontSize - 1));
        ZoomResetCommand = new RelayCommand(() => SetBaseFontSize(DefaultFontSize));
        OpenSourceCommand = new RelayCommand(OpenSource, () => CanOpenSource);
        UpdateLanguageButtonText();

        if (_allStages.Length > 0)
        {
            _stagesViewSource = new CollectionViewSource { Source = _allStages };
            _stagesViewSource.GroupDescriptions.Add(new PropertyGroupDescription(nameof(UpdateStage.Vendor)));
            _stagesViewSource.Filter += (_, e) => e.Accepted = e.Item is not UpdateStage s || ChangelogFormatter.MatchesStageFilter(s, InstalledOnly, SearchQuery);
            StagesView = _stagesViewSource.View;
        }

        SelectedStage = selected ?? _allStages.FirstOrDefault();
    }

    public event Action? ScrollToHomeRequested;
    public ICollectionView? StagesView { get; }
    public bool HasStages => _allStages.Length > 0;
    public ObservableCollection<ReleaseCardViewModel> Releases { get; } = [];
    public bool IsSearchPlaceholderVisible => string.IsNullOrEmpty(SearchQuery);
    public string ZoomLabel => $"{(int)Math.Round(BaseFontSize / DefaultFontSize * 100)}%";
    public bool CanOpenSource => SourcePageUrl is not null;
    public bool IsCardsVisible => !IsRawTextVisible;
    public IRelayCommand ToggleLanguageCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }
    public IRelayCommand ZoomResetCommand { get; }
    public IRelayCommand OpenSourceCommand { get; }

    public UpdateStage? SelectedStage
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value is not null && (!ReferenceEquals(value, _state.CurrentStage) || _state.CurrentDocument is null))
            {
                _ = LoadStageChangelogAsync(value, false);
            }
        }
    }

    public string SearchQuery
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsSearchPlaceholderVisible));
                RefreshStageFilter(false);
            }
        }
    } = "";

    public bool InstalledOnly
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RefreshStageFilter(value);
            }
        }
    }

    public bool ShowTranslation { get; private set => SetProperty(ref field, value); } = true;
    public string LanguageToggleText { get; private set => SetProperty(ref field, value); } = "Язык: Русский (ИИ)";
    public string SourceNote { get; private set => SetProperty(ref field, value); } = "";
    public string LoadStatusText { get; private set => SetProperty(ref field, value); } = "";

    public double BaseFontSize
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ZoomLabel));
            }
        }
    } = DefaultFontSize;

    public string? SourcePageUrl
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanOpenSource));
                OpenSourceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string RawText
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !_suppressTextSync)
            {
                HandleExternalRawTextChange(value);
            }
        }
    } = "";

    public bool IsRawTextVisible
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsCardsVisible));
            }
        }
    }

    public void SelectStage(UpdateStage stage) => SelectedStage = stage;

    public void SetBaseFontSize(double size)
    {
        BaseFontSize = Math.Clamp(size, 11, 22);
        if (_state.CurrentDocument is not null)
        {
            RenderDocument(_state.CurrentDocument, _state.CurrentStage);
        }
    }

    public Task LoadStageChangelogAsync(UpdateStage stage, bool forceRefresh)
    {
        UpdateSourceHeader(stage);
        return _state.LoadStageAsync(
            _changelogService,
            stage,
            forceRefresh,
            RenderDocument,
            () => { Releases.Clear(); SetRawTextInternal(""); },
            status => LoadStatusText = status,
            ShowFallbackText,
            () => ScrollToHomeRequested?.Invoke(),
            () => ShowTranslation = true);
    }

    public Task<bool> TranslateReleaseOnDemandAsync(ChangelogRelease release, bool forceRetranslate = false) =>
        _state.CurrentDocument is null
            ? Task.FromResult(false)
            : _state.TranslateReleaseAsync(
                _changelogService,
                _state.CurrentDocument,
                release,
                _state.CurrentStage,
                forceRetranslate,
                $"Выпусков: {_state.CurrentDocument.Releases.Count}",
                RenderDocument,
                status => LoadStatusText = status,
                () => ShowTranslation = true,
                _state.ActiveLoadToken);

    public void RenderDocument(ChangelogDocument document, UpdateStage? stage = null)
    {
        _state.CurrentDocument = document;
        if (stage is not null)
        {
            _state.CurrentStage = stage;
            if (!ReferenceEquals(SelectedStage, stage) && (StagesView is null || StagesView.Cast<object>().Contains(stage)))
            {
                SelectedStage = stage;
            }

            UpdateSourceHeader(stage);
        }

        ChangelogFormatter.EnsureSummaries(document.Releases);
        UpdateLanguageButtonText();
        RebuildReleaseCards(document.Releases, stage);
        SetRawTextInternal(ChangelogFormatter.FormatAsPlainText(document.Releases, ShowTranslation));
    }

    public void Dispose() => _state.Dispose();

    private void ToggleLanguage()
    {
        ShowTranslation = !ShowTranslation;
        _state.OriginalOverrideReleaseKeys.Clear();
        UpdateLanguageButtonText();
        if (_state.CurrentDocument is not null)
        {
            RenderDocument(_state.CurrentDocument, _state.CurrentStage);
        }
    }

    private Task RefreshCurrentStageAsync() =>
        _state.CurrentStage is not null ? LoadStageChangelogAsync(_state.CurrentStage, true) : Task.CompletedTask;

    private void OpenSource()
    {
        if (SourcePageUrl is { } url)
        {
            _dialogService?.OpenExternal(url, "Не удалось открыть источник");
        }
    }

    private void RefreshStageFilter(bool selectFirstWhenCurrentHidden)
    {
        if (StagesView is null)
        {
            return;
        }

        StagesView.Refresh();
        var visibleStages = StagesView.Cast<UpdateStage>().ToList();
        if (SelectedStage is not null && visibleStages.Contains(SelectedStage))
        {
            return;
        }

        if (_state.CurrentStage is not null && visibleStages.Contains(_state.CurrentStage))
        {
            SelectedStage = _state.CurrentStage;
        }
        else if (selectFirstWhenCurrentHidden && visibleStages.FirstOrDefault() is { } first)
        {
            SelectedStage = first;
        }
    }

    private void UpdateLanguageButtonText() =>
        LanguageToggleText = ChangelogFormatter.FormatLanguageToggleText(ShowTranslation, _state.CurrentDocument);

    private void UpdateSourceHeader(UpdateStage stage)
    {
        var source = stage.GetChangelogSource();
        SourceNote = source.Note;
        SourcePageUrl = source.PageUrl;
    }

    private void UpdateTranslationBannerLiveText()
    {
        if (_state.TranslatingReleaseKey is { } key
            && Releases.FirstOrDefault(item => string.Equals(item.ReleaseKey, key, StringComparison.OrdinalIgnoreCase)) is { } card)
        {
            card.TranslationProgressText = ChangelogFormatter.BuildTranslationProgressText(
                _state.TranslatingGroupCount,
                _state.TranslatingItemCount,
                _state.TranslationStartedAt);
        }
    }

    private void RebuildReleaseCards(IReadOnlyList<ChangelogRelease> releases, UpdateStage? stage)
    {
        Releases.Clear();
        for (var index = 0; index < releases.Count; index++)
        {
            var release = releases[index];
            var data = _state.PrepareCardData(release, index, stage, BaseFontSize, ShowTranslation);
            Releases.Add(new ReleaseCardViewModel(
                release,
                data,
                () => TranslateReleaseOnDemandAsync(release, true),
                _state.CancelTranslation,
                ToggleCardLanguage,
                (key, collapsed) => ChangelogPresentationState.SetCollapsedKey(_state.CollapsedReleaseKeys, key, collapsed),
                (key, collapsed) => ChangelogPresentationState.SetCollapsedKey(_state.CollapsedGroupKeys, key, collapsed)));
        }

        IsRawTextVisible = false;
    }

    private void ToggleCardLanguage(string releaseKey, bool currentlyShowingRu)
    {
        if (currentlyShowingRu)
        {
            _state.OriginalOverrideReleaseKeys.Add(releaseKey);
        }
        else
        {
            _state.OriginalOverrideReleaseKeys.Remove(releaseKey);
            ShowTranslation = true;
        }

        if (_state.CurrentDocument is not null)
        {
            RenderDocument(_state.CurrentDocument, _state.CurrentStage);
        }
    }

    private void ShowFallbackText(string text)
    {
        Releases.Clear();
        IsRawTextVisible = true;
        SetRawTextInternal(text);
    }

    private void SetRawTextInternal(string text)
    {
        _suppressTextSync = true;
        try
        {
            RawText = text;
        }
        finally
        {
            _suppressTextSync = false;
        }
    }

    private void HandleExternalRawTextChange(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var parsed = _changelogService.ParseReleases(text, "markdown");
        if (parsed.Count == 0)
        {
            IsRawTextVisible = true;
            return;
        }

        _suppressTextSync = true;
        try
        {
            _state.CurrentDocument = new ChangelogDocument { StageId = "preview", UpdatedAt = DateTimeOffset.UtcNow, Releases = parsed };
            RebuildReleaseCards(parsed, _state.CurrentStage);
        }
        finally
        {
            _suppressTextSync = false;
        }
    }
}

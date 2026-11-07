using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AIUpdater.ViewModels;

public sealed class ReleaseCardViewModel : ObservableObject
{
    private readonly Action<string, bool>? _onCollapseChanged;

    public ReleaseCardViewModel(
        ChangelogRelease release,
        ChangelogCardData data,
        Func<Task> onProcessAsync,
        Action onCancelTranslation,
        Action<string, bool> onToggleLanguage,
        Action<string, bool>? onReleaseCollapseChanged = null,
        Action<string, bool>? onGroupCollapseChanged = null)
    {
        Release = release;
        ReleaseKey = data.ReleaseKey;
        Title = data.Title;
        PublishedAt = data.PublishedAt;
        IsLatest = data.IsLatest;
        BaseFontSize = data.BaseFontSize;
        Summary = data.Summary;
        IsTranslating = data.IsTranslating;
        TranslationProgressText = data.TranslationProgressText;
        TranslationErrorText = data.TranslationErrorText;
        Badges = new ObservableCollection<ReleaseBadge>(data.Badges);
        Groups = new ObservableCollection<ReleaseGroupViewModel>(
            data.Groups.Select(item => new ReleaseGroupViewModel(
                item.GroupKey,
                item.Group,
                data.BaseFontSize,
                item.IsCollapsed,
                onGroupCollapseChanged)));
        TotalItems = Groups.Sum(g => g.Items.Count);
        _onCollapseChanged = onReleaseCollapseChanged;
        IsCollapsed = data.IsCollapsed;

        ProcessButtonText = data.HasError ? "Повторить обработку" : "Обработать этот релиз";
        CardLanguageButtonText = data.ShowRuForCard ? "Оригинал" : "Русский (ИИ)";

        ProcessCommand = new AsyncRelayCommand(onProcessAsync);
        CancelTranslationCommand = new RelayCommand(onCancelTranslation);
        ToggleCardLanguageCommand = new RelayCommand(() => onToggleLanguage(data.ReleaseKey, data.ShowRuForCard));
        ToggleCollapseCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
    }

    public ChangelogRelease Release { get; }

    public string ReleaseKey { get; }

    public string Title { get; }

    public string? PublishedAt { get; }

    public bool HasPublishedAt => !string.IsNullOrWhiteSpace(PublishedAt);

    public bool IsLatest { get; }

    public double BaseFontSize { get; }

    public double TitleFontSize => BaseFontSize + 2;

    public double MetaFontSize => Math.Max(11, BaseFontSize - 2);

    public double SummaryFontSize => Math.Max(12, BaseFontSize - 1);

    public ObservableCollection<ReleaseBadge> Badges { get; }

    public ObservableCollection<ReleaseGroupViewModel> Groups { get; }

    public int TotalItems { get; }

    public bool IsCollapsed
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ExpandButtonText));
                _onCollapseChanged?.Invoke(ReleaseKey, value);
            }
        }
    }

    public bool IsExpanded => !IsCollapsed;

    public string ExpandButtonText => IsCollapsed ? $"Развернуть ({TotalItems})" : "Свернуть";

    public string? Summary { get; }

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public bool IsTranslating
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasTranslationError));
                OnPropertyChanged(nameof(CanProcess));
                OnPropertyChanged(nameof(CanToggleCardLanguage));
                OnPropertyChanged(nameof(CanReprocess));
            }
        }
    }

    public string TranslationProgressText
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string? TranslationErrorText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasTranslationError));
            }
        }
    }

    public bool HasTranslationError => !IsTranslating && !string.IsNullOrWhiteSpace(TranslationErrorText);

    public bool CanProcess => !IsTranslating && !Release.HasTranslation;

    public string ProcessButtonText { get; }

    public bool CanToggleCardLanguage => !IsTranslating && Release.HasTranslation;

    public string CardLanguageButtonText { get; }

    public bool CanReprocess => !IsTranslating && Release.HasTranslation;

    public IAsyncRelayCommand ProcessCommand { get; }

    public IRelayCommand CancelTranslationCommand { get; }

    public IRelayCommand ToggleCardLanguageCommand { get; }

    public IRelayCommand ToggleCollapseCommand { get; }
}

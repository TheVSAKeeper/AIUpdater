using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIUpdater.ViewModels;

public sealed class ReleaseGroupViewModel : ObservableObject
{
    private readonly Action<string, bool>? _onCollapseChanged;

    public ReleaseGroupViewModel(
        string groupKey,
        ChangelogGroup group,
        double baseFontSize,
        bool isCollapsed,
        Action<string, bool>? onCollapseChanged = null)
    {
        GroupKey = groupKey;
        Category = group.Category;
        Title = group.Title;
        Items = group.Items;
        BaseFontSize = baseFontSize;
        HasAiSummary = !string.IsNullOrWhiteSpace(group.Summary);
        SummaryPreviewTooltip = HasAiSummary ? group.Summary! : ChangelogFormatter.BuildGroupSummary(group);
        _onCollapseChanged = onCollapseChanged;
        IsCollapsed = isCollapsed;
        ToggleCollapseCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
    }

    public string GroupKey { get; }

    public ChangeCategory Category { get; }

    public string Title { get; }

    public IReadOnlyList<string> Items { get; }

    public string HeaderText => $"{Title} ({Items.Count})";

    public double BaseFontSize { get; }

    public double TitleFontSize => Math.Max(12, BaseFontSize - 1);

    public double MetaFontSize => Math.Max(11, BaseFontSize - 2);

    public bool HasAiSummary { get; }

    public string SummaryPreviewTooltip { get; }

    public string SummaryPreviewText => $"– {SummaryPreviewTooltip}";

    public bool IsCollapsed
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ChevronText));
                OnPropertyChanged(nameof(IsSummaryPreviewVisible));
                _onCollapseChanged?.Invoke(GroupKey, value);
            }
        }
    }

    public bool IsExpanded => !IsCollapsed;

    public string ChevronText => IsCollapsed ? "▸" : "▾";

    public bool IsSummaryPreviewVisible => HasAiSummary || IsCollapsed;

    public IRelayCommand ToggleCollapseCommand { get; }
}

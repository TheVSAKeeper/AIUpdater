using AIUpdater.ViewModels;

namespace AIUpdater.Models;

public enum UpdateGroupMode
{
    None = 0,
    ByVendor = 1,
    ByState = 2,
    Ungrouped = 3,
}

public enum UpdateSortMode
{
    None = 0,
    Catalog = 1,
    UpdatesFirst = 2,
    ByName = 3,
}

public sealed record UpdateGroupOption(UpdateGroupMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed record UpdateSortOption(UpdateSortMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed record StageCardGroup(string Name, bool ShowHeader, IReadOnlyList<StageCardViewModel> Cards);

using AIUpdater.Models;
using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AIUpdater.ViewModels;

public enum DotNetPackageRowState
{
    None = 0,
    NotInstalled = 1,
    UpToDate = 2,
    UpdateAvailable = 3,
    EolInstalled = 4,
    ActiveOperation = 5,
}

public enum DotNetChannelBadgeKind
{
    None = 0,
    Lts = 1,
    Sts = 2,
    Preview = 3,
    Eol = 4,
}

public sealed class DotNetUninstallOptionViewModel
{
    public DotNetUninstallOptionViewModel(
        DotNetInstalledInstance instance,
        Func<DotNetInstalledInstance, Task> uninstallAsync,
        Func<bool> canExecute)
    {
        Instance = instance;
        Header = $"{instance.Version} · {instance.OwnerLabel}";
        ToolTip = instance.OwnerDescription;
        Command = new AsyncRelayCommand(
            () => uninstallAsync(instance),
            () => instance.CanUninstall && canExecute());
    }

    public DotNetInstalledInstance Instance { get; }

    public string Header { get; }

    public string ToolTip { get; }

    public bool IsEnabled => Instance.CanUninstall;

    public IAsyncRelayCommand Command { get; }

    public void RefreshCanExecute() => Command.NotifyCanExecuteChanged();
}

public sealed class DotNetPackageRowViewModel : ObservableObject
{
    private readonly Func<DotNetOperationRequest, Task> _runOperationAsync;
    private readonly Func<DotNetPackageKind, DotNetInstalledInstance, Task> _uninstallInstanceAsync;
    private readonly Action<string> _openReleaseNotes;
    private readonly Func<bool> _canInteract;
    private readonly Func<string?> _getOperationDetail;
    private DotNetActionKind _activeAction;

    public DotNetPackageRowViewModel(
        DotNetChannelInfo channel,
        DotNetPackageEntry entry,
        bool includePrerelease,
        Func<DotNetOperationRequest, Task> runOperationAsync,
        Func<DotNetPackageKind, DotNetInstalledInstance, Task> uninstallInstanceAsync,
        Action<string> openReleaseNotes,
        Func<bool> canInteract,
        Func<string?> getOperationDetail)
    {
        ChannelInfo = channel;
        Entry = entry;
        IncludePrerelease = includePrerelease;
        _runOperationAsync = runOperationAsync;
        _uninstallInstanceAsync = uninstallInstanceAsync;
        _openReleaseNotes = openReleaseNotes;
        _canInteract = canInteract;
        _getOperationDetail = getOperationDetail;

        UninstallOptions = entry.InstalledInstances
            .Select(instance => new DotNetUninstallOptionViewModel(
                instance,
                item => _uninstallInstanceAsync(entry.Kind, item),
                _canInteract))
            .ToArray();

        PrimaryActionCommand = new AsyncRelayCommand(ExecutePrimaryActionAsync, () => HasPrimaryAction && _canInteract());
        InstallLatestCommand = new AsyncRelayCommand(ExecuteInstallLatestAsync, () => CanInstallLatest && _canInteract());
        UpdateCommand = new AsyncRelayCommand(ExecuteUpdateAsync, () => CanUpdate && _canInteract());
        UninstallSingleCommand = new AsyncRelayCommand(ExecuteSingleUninstallAsync, () => CanUninstallSingle && _canInteract());
        OpenReleaseNotesCommand = new RelayCommand(() => _openReleaseNotes(channel.Channel));
    }

    public DotNetChannelInfo ChannelInfo { get; }

    public DotNetPackageEntry Entry { get; }

    public bool IncludePrerelease { get; }

    public string Title => DotNetPackageEntry.GetTitle(Entry.Kind);

    public string Subtitle => Entry.IsInstalled
        ? BuildOwnerSubtitle(Entry.InstalledInstances)
        : DotNetPackageEntry.GetSubtitle(Entry.Kind);

    public string InstalledText => Entry.IsInstalled
        ? string.Join("; ", Entry.InstalledInstances.Select(instance => instance.Version))
        : "–";

    public string AvailableText => string.IsNullOrWhiteSpace(Entry.AvailableVersion)
        ? "–"
        : Entry.AvailableVersion!;

    public bool HasUpdate => Entry.HasUpdate;

    public bool IsActive => _activeAction != DotNetActionKind.None;

    public DotNetPackageRowState RowState => IsActive
        ? DotNetPackageRowState.ActiveOperation
        : !Entry.IsInstalled
            ? DotNetPackageRowState.NotInstalled
            : Entry.HasUpdate
                ? DotNetPackageRowState.UpdateAvailable
                : ChannelInfo.IsEol
                    ? DotNetPackageRowState.EolInstalled
                    : DotNetPackageRowState.UpToDate;

    public string StatusText => _activeAction switch
    {
        DotNetActionKind.Install => "Установка…",
        DotNetActionKind.Update => "Обновление…",
        DotNetActionKind.Uninstall => "Удаление…",
        _ => RowState switch
        {
            DotNetPackageRowState.NotInstalled => "Не установлено",
            DotNetPackageRowState.UpdateAvailable => "Есть обновление",
            DotNetPackageRowState.EolInstalled => "Ветка EOL",
            DotNetPackageRowState.UpToDate => "Актуально",
            _ => "Не проверено",
        },
    };

    public string OperationDetail => IsActive ? (_getOperationDetail() ?? "") : "";

    public string RowToolTip
    {
        get
        {
            var lines = new List<string> { $"{Title} (.NET {Entry.Channel})" };
            if (Entry.IsInstalled)
            {
                foreach (var instance in Entry.InstalledInstances)
                {
                    lines.Add($"Установлено: {instance.Version} – {instance.OwnerLabel}");
                }
            }
            else
            {
                lines.Add("Компонент не установлен на этом компьютере.");
            }

            if (!string.IsNullOrWhiteSpace(Entry.LatestChannelVersion))
            {
                lines.Add($"Последний выпуск ветки: {Entry.LatestChannelVersion}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool CanUpdate => Entry.IsInstalled && Entry.HasUpdate;

    public bool CanInstallPrimary => !Entry.IsInstalled && !string.IsNullOrWhiteSpace(Entry.LatestChannelVersion);

    public bool CanInstallLatest => !string.IsNullOrWhiteSpace(Entry.LatestChannelVersion)
                                    && (!Entry.IsInstalled || Entry.CanInstallLatestBand);

    public bool HasPrimaryAction => CanUpdate || CanInstallPrimary;

    public bool IsPrimaryAccent => CanUpdate;

    public string PrimaryActionText => CanUpdate ? "Обновить" : "Установить";

    public string PrimaryActionToolTip => CanUpdate
        ? $"Обновить {Title} до актуального выпуска ветки {Entry.Channel}"
        : $"Установить {Title} {Entry.LatestChannelVersion}";

    public string InstallMenuHeader => string.IsNullOrWhiteSpace(Entry.LatestChannelVersion)
        ? "Установить"
        : Entry.IsInstalled && Entry.CanInstallLatestBand
            ? $"Установить SDK {Entry.LatestChannelVersion}"
            : $"Установить {Entry.LatestChannelVersion}";

    public IReadOnlyList<DotNetUninstallOptionViewModel> UninstallOptions { get; }

    public bool HasSingleInstance => Entry.InstalledInstances.Count == 1;

    public bool HasMultipleInstances => Entry.InstalledInstances.Count > 1;

    public bool CanUninstallSingle => HasSingleInstance && Entry.InstalledInstances[0].CanUninstall;

    public string SingleUninstallHeader => HasSingleInstance
        ? $"Удалить {Entry.InstalledInstances[0].Version}"
        : "Удалить";

    public string SingleUninstallToolTip => HasSingleInstance
        ? Entry.InstalledInstances[0].OwnerDescription
        : "";

    public bool CanInteract => _canInteract();

    public IAsyncRelayCommand PrimaryActionCommand { get; }

    public IAsyncRelayCommand InstallLatestCommand { get; }

    public IAsyncRelayCommand UpdateCommand { get; }

    public IAsyncRelayCommand UninstallSingleCommand { get; }

    public IRelayCommand OpenReleaseNotesCommand { get; }

    public void SetActiveAction(DotNetActionKind action)
    {
        if (_activeAction == action)
        {
            return;
        }

        _activeAction = action;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(RowState));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(OperationDetail));
    }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(OperationDetail));
        PrimaryActionCommand.NotifyCanExecuteChanged();
        InstallLatestCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        UninstallSingleCommand.NotifyCanExecuteChanged();
        foreach (var option in UninstallOptions)
        {
            option.RefreshCanExecute();
        }
    }

    private Task ExecutePrimaryActionAsync() =>
        CanUpdate ? ExecuteUpdateAsync() : ExecuteInstallLatestAsync();

    private Task ExecuteInstallLatestAsync()
    {
        if (string.IsNullOrWhiteSpace(Entry.LatestChannelVersion))
        {
            return Task.CompletedTask;
        }

        return _runOperationAsync(new DotNetOperationRequest(
            DotNetActionKind.Install,
            Entry.Kind,
            Entry.Channel,
            Entry.LatestChannelVersion,
            IncludePrerelease || ChannelInfo.IsPreview));
    }

    private Task ExecuteUpdateAsync()
    {
        string? version;
        if (Entry.Kind == DotNetPackageKind.Sdk)
        {
            var updatingBands = Entry.SdkBands.Where(band => band.HasUpdate).ToArray();
            version = updatingBands.Length == 1 ? updatingBands[0].Available : null;
        }
        else
        {
            version = Entry.AvailableVersion;
        }

        return _runOperationAsync(new DotNetOperationRequest(
            DotNetActionKind.Update,
            Entry.Kind,
            Entry.Channel,
            version,
            IncludePrerelease || ChannelInfo.IsPreview));
    }

    private Task ExecuteSingleUninstallAsync() =>
        HasSingleInstance
            ? _uninstallInstanceAsync(Entry.Kind, Entry.InstalledInstances[0])
            : Task.CompletedTask;

    private static string BuildOwnerSubtitle(IReadOnlyList<DotNetInstalledInstance> instances)
    {
        var owners = instances.Select(instance => instance.OwnerLabel).Distinct(StringComparer.Ordinal).ToArray();
        var joined = string.Join(", ", owners);
        return instances.Count == 1 ? joined : $"Версий: {instances.Count} · {joined}";
    }
}

public sealed class DotNetChannelViewModel
{
    public DotNetChannelViewModel(
        DotNetChannelInfo info,
        IReadOnlyList<DotNetPackageRowViewModel> packages,
        Action<string> openReleaseNotes)
    {
        Info = info;
        Packages = packages;
        OpenReleaseNotesCommand = new RelayCommand(() => openReleaseNotes(info.Channel));
    }

    public DotNetChannelInfo Info { get; }

    public string Title => $"{Info.DisplayTitle} ({Info.Channel})";

    public string BadgeText => Info.BadgeText;

    public DotNetChannelBadgeKind BadgeKind => Info.SupportPhase switch
    {
        DotNetSupportPhase.Preview or DotNetSupportPhase.GoLive => DotNetChannelBadgeKind.Preview,
        DotNetSupportPhase.Eol => DotNetChannelBadgeKind.Eol,
        _ => Info.ReleaseType switch
        {
            DotNetReleaseType.Lts => DotNetChannelBadgeKind.Lts,
            DotNetReleaseType.Sts => DotNetChannelBadgeKind.Sts,
            _ => DotNetChannelBadgeKind.Sts,
        },
    };

    public string SupportSummary => Info.SupportSummary;

    public IReadOnlyList<DotNetPackageRowViewModel> Packages { get; }

    public IRelayCommand OpenReleaseNotesCommand { get; }
}

public sealed class DotNetViewModel : ObservableObject, IDisposable, IPageHeader, IPageRefresh
{
    private readonly MainViewModel _updates;
    private readonly IDotNetPackageService _packageService;
    private readonly IUpdaterDialogs _dialogs;
    private DotNetCatalogSnapshot? _snapshot;
    private DotNetOperationRequest? _activeOperation;
    private CancellationTokenSource? _loadCts;
    private bool _hasLoaded;

    [ActivatorUtilitiesConstructor]
    public DotNetViewModel(MainViewModel updates, IDotNetPackageService packageService, IUpdaterDialogs dialogs)
    {
        _updates = updates;
        _packageService = packageService;
        _dialogs = dialogs;

        RefreshCommand = new AsyncRelayCommand(() => LoadCatalogAsync(forceRefresh: true), () => CanInteract);
        EnsureLoadedCommand = new AsyncRelayCommand(EnsureLoadedAsync);
        OpenLogsCommand = new RelayCommand(() => _dialogs.OpenChangelog(
            _updates.Stages.FirstOrDefault(stage => stage.Id == ".NET SDK 10")));

        _updates.PropertyChanged += OnUpdatesPropertyChanged;
    }

    public string PageTitle => ".NET";

    public string PageDescription => "Установка, обслуживание и удаление пакетов .NET SDK и сред выполнения";

    System.Windows.Input.ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public string RefreshTooltip => "Обновить каталог выпусков Microsoft и локальные версии";

    public ObservableCollection<DotNetChannelViewModel> Channels { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand EnsureLoadedCommand { get; }

    public IRelayCommand OpenLogsCommand { get; }

    public bool IsLoading
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                RefreshInteractiveState();
            }
        }
    }

    public bool ShowPrerelease
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && _hasLoaded)
            {
                _ = LoadCatalogAsync(forceRefresh: false);
            }
        }
    }

    public bool ShowEol
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RebuildChannels();
            }
        }
    }

    public bool InstalledOnly
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RebuildChannels();
            }
        }
    }

    public string SummaryText { get; private set => SetProperty(ref field, value); } = "Каталог выпусков .NET ещё не загружен";

    public string? WarningMessage { get; private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasWarning)); } }

    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);

    public bool IsBusy => IsLoading || _updates.IsRunning;

    public bool CanInteract => !IsBusy;

    public bool IsEmptyStateVisible => !IsLoading && Channels.Count == 0;

    public string EmptyStateText => InstalledOnly
        ? "Установленные пакеты .NET для выбранных фильтров не найдены."
        : "Нет доступных каналов .NET для выбранных фильтров.";

    public string BusyTitle => IsLoading
        ? "Загрузка каталога выпусков .NET…"
        : _activeOperation?.DisplayLabel ?? _updates.OperationTitle;

    public async Task EnsureLoadedAsync()
    {
        if (_hasLoaded || IsLoading)
        {
            return;
        }

        await LoadCatalogAsync(forceRefresh: false);
    }

    public async Task LoadCatalogAsync(bool forceRefresh)
    {
        var previous = Interlocked.Exchange(ref _loadCts, null);
        previous?.Cancel();
        previous?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        try
        {
            var snapshot = await _packageService.LoadCatalogAsync(forceRefresh, ShowPrerelease, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _snapshot = snapshot;
            _hasLoaded = true;
            WarningMessage = snapshot.WarningMessage;
            RebuildChannels();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            WarningMessage = $"Не удалось обновить каталог .NET: {ex.Message}";
            SummaryText = "Ошибка загрузки каталога .NET";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    public void ArrangeCatalog(DotNetCatalogSnapshot snapshot)
    {
        _snapshot = snapshot;
        _hasLoaded = true;
        WarningMessage = snapshot.WarningMessage;
        RebuildChannels();
    }

    public async Task ExecuteOperationAsync(DotNetOperationRequest request)
    {
        if (!CanInteract)
        {
            return;
        }

        _activeOperation = request;
        ApplyActiveRowMarker();
        RefreshInteractiveState();
        try
        {
            await _updates.RunDotNetPackageAsync(request);
        }
        finally
        {
            _activeOperation = null;
            ApplyActiveRowMarker();
            RefreshInteractiveState();
        }

        await LoadCatalogAsync(forceRefresh: false);
    }

    public async Task ConfirmAndUninstallAsync(DotNetPackageKind kind, DotNetInstalledInstance instance)
    {
        if (!CanInteract)
        {
            return;
        }

        if (!instance.CanUninstall)
        {
            _dialogs.ShowMessage(instance.OwnerDescription, "Удаление недоступно");
            return;
        }

        var channel = DotNetVersionComparer.TryGetChannel(instance.Version) ?? "10.0";
        var title = DotNetPackageEntry.GetTitle(kind);
        var confirmed = await _dialogs.ConfirmAsync(
            $"Удалить {title} {instance.Version}?\nУбедитесь, что эта версия не закреплена в global.json ваших проектов.",
            "Удаление пакета .NET",
            "Удалить");
        if (!confirmed)
        {
            return;
        }

        await ExecuteOperationAsync(new DotNetOperationRequest(
            DotNetActionKind.Uninstall,
            kind,
            channel,
            instance.Version,
            ShowPrerelease));
    }

    public void OpenChannelReleaseNotes(string channel) =>
        _dialogs.OpenExternal($"https://github.com/dotnet/core/tree/main/release-notes/{channel}", ".NET");

    public void Dispose()
    {
        _updates.PropertyChanged -= OnUpdatesPropertyChanged;
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private void RebuildChannels()
    {
        Channels.Clear();
        if (_snapshot is null)
        {
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(EmptyStateText));
            return;
        }

        foreach (var channel in _snapshot.Channels)
        {
            if (channel.IsEol && !ShowEol)
            {
                continue;
            }

            var packages = channel.Packages
                .Where(entry => !InstalledOnly || entry.IsInstalled)
                .Select(entry => new DotNetPackageRowViewModel(
                    channel,
                    entry,
                    ShowPrerelease,
                    ExecuteOperationAsync,
                    ConfirmAndUninstallAsync,
                    OpenChannelReleaseNotes,
                    () => CanInteract,
                    GetDevVendorDetail))
                .ToArray();

            if (packages.Length == 0)
            {
                continue;
            }

            Channels.Add(new DotNetChannelViewModel(channel, packages, OpenChannelReleaseNotes));
        }

        ApplyActiveRowMarker();
        UpdateSummaryText();
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private void UpdateSummaryText()
    {
        if (_snapshot is null)
        {
            SummaryText = "Каталог выпусков .NET ещё не загружен";
            return;
        }

        var visiblePackages = Channels.SelectMany(channel => channel.Packages).ToArray();
        var installedCount = visiblePackages.Count(row => row.Entry.IsInstalled);
        var updateCount = visiblePackages.Count(row => row.HasUpdate);
        var hiddenEolInstalled = !ShowEol
            ? _snapshot.Channels.Where(channel => channel.IsEol).Sum(channel => channel.Packages.Count(pkg => pkg.IsInstalled))
            : 0;

        var summary = $"Каналов: {Channels.Count} · Установлено: {installedCount} · Обновлений: {updateCount}";
        if (hiddenEolInstalled > 0)
        {
            summary += $" · В EOL: {hiddenEolInstalled}";
        }

        SummaryText = summary;
    }

    private void ApplyActiveRowMarker()
    {
        foreach (var row in Channels.SelectMany(channel => channel.Packages))
        {
            var isMatch = _activeOperation is not null
                          && string.Equals(row.Entry.Channel, _activeOperation.Channel, StringComparison.Ordinal)
                          && row.Entry.Kind == _activeOperation.Kind;
            row.SetActiveAction(isMatch ? _activeOperation!.Action : DotNetActionKind.None);
        }
    }

    private string? GetDevVendorDetail() =>
        _updates.Vendors.FirstOrDefault(vendor => vendor.Name == "Разработка")?.Detail;

    private void OnUpdatesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsRunning)
            or nameof(MainViewModel.ActiveOperationText)
            or nameof(MainViewModel.OperationTitle))
        {
            RefreshInteractiveState();
        }
    }

    private void RefreshInteractiveState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(BusyTitle));
        RefreshCommand.NotifyCanExecuteChanged();
        foreach (var row in Channels.SelectMany(channel => channel.Packages))
        {
            row.RefreshState();
        }
    }
}

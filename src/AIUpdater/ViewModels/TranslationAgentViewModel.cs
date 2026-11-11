using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Agents;

namespace AIUpdater.ViewModels;

public sealed record TranslationAgentOption(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class TranslationAgentViewModel : ObservableObject
{
    private readonly TranslationAgentSettings _settings;
    private Dictionary<string, TranslationAgentProfile> _profiles = new();
    private string _backendId = "";
    private int _profileGeneration;

    public TranslationAgentViewModel(TranslationAgentSettings settings)
    {
        _settings = settings;
        Backends = AgentBackendFactory.Ids.Select(id =>
        {
            var backend = AgentBackendFactory.Create(id);
            try { return new TranslationAgentOption(id, backend.DisplayName); }
            finally { (backend as IDisposable)?.Dispose(); }
        }).ToArray();
        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(Reset);
        DetectCommand = new AsyncRelayCommand(DetectAsync);
        Reset();
    }

    public IReadOnlyList<TranslationAgentOption> Backends { get; }
    public string BackendId
    {
        get => _backendId;
        set
        {
            if (value == _backendId || !Backends.Any(backend => backend.Id == value)) return;
            StoreProfile();
            SetProperty(ref _backendId, value);
            LoadProfile();
        }
    }
    public string Model { get; set => SetProperty(ref field, value ?? ""); } = "";
    public string Effort { get; set => SetProperty(ref field, value ?? ""); } = "";
    public string ExecutablePath
    {
        get;
        set
        {
            if (!SetProperty(ref field, value ?? "")) return;
            _profileGeneration++;
            Status = "";
            DetectedPath = "";
        }
    } = "";
    public bool Automatic { get; set => SetProperty(ref field, value); }
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    public string DetectedPath { get; private set => SetProperty(ref field, value); } = "";
    public IReadOnlyList<string> Models
    {
        get;
        private set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasModels));
        }
    } = [];
    public bool HasModels => Models.Count > 0;
    public IReadOnlyList<AgentEffortOption> Efforts { get; private set => SetProperty(ref field, value); } = [];
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand ResetCommand { get; }
    public IAsyncRelayCommand DetectCommand { get; }
    public event Action? Saved;

    public string SavedDescription
    {
        get
        {
            var selected = _settings.Current;
            var name = Backends.First(backend => backend.Id == selected.BackendId).Name;
            var model = string.IsNullOrWhiteSpace(selected.Profile.Model) ? "модель по умолчанию" : selected.Profile.Model;
            return $"{name} · {model}" + (selected.Automatic ? "" : " · автоперевод выключен");
        }
    }

    public void Reset()
    {
        DetectCommand.Cancel();
        _profiles = new(_settings.Current.Profiles);
        _backendId = _settings.Current.BackendId;
        OnPropertyChanged(nameof(BackendId));
        Automatic = _settings.Current.Automatic;
        LoadProfile();
    }

    private void StoreProfile() => _profiles[BackendId] = new()
    {
        Model = Model.Trim(), Effort = Effort.Trim(), ExecutablePath = ExecutablePath.Trim(),
    };

    private void LoadProfile()
    {
        _profileGeneration++;
        var profile = _profiles.GetValueOrDefault(BackendId) ?? new();
        var backend = AgentBackendFactory.Create(BackendId);
        try
        {
            Efforts = AgentModels.Efforts(backend.Kind, profile.Model);
            if (!Efforts.Any(option => option.Id == profile.Effort))
                Efforts = [.. Efforts, new(profile.Effort, profile.Effort)];
            Models = AgentModels.For(backend.Kind).Select(model => model.Id)
                .Concat(string.IsNullOrWhiteSpace(profile.Model) ? [] : new[] { profile.Model }).Distinct().ToArray();
        }
        finally { (backend as IDisposable)?.Dispose(); }
        Model = profile.Model;
        Effort = profile.Effort;
        ExecutablePath = profile.ExecutablePath;
        Status = "";
        DetectedPath = "";
    }

    private void Save()
    {
        try
        {
            StoreProfile();
            _settings.Save(new() { Automatic = Automatic, BackendId = BackendId, Profiles = new(_profiles) });
            OnPropertyChanged(nameof(SavedDescription));
            Status = "Настройки применены";
            Saved?.Invoke();
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { Status = exception.Message; }
    }

    private async Task DetectAsync(CancellationToken cancellationToken)
    {
        var generation = _profileGeneration;
        var path = ExecutablePath.Trim();
        if (path.Length > 0 && !System.IO.File.Exists(path))
        {
            Status = "Файл не найден. Проверьте путь или выберите программу через «Обзор…».";
            return;
        }
        var backend = AgentBackendFactory.Create(BackendId, new() { ExecutablePath = path });
        bool IsCurrent() => !cancellationToken.IsCancellationRequested && generation == _profileGeneration && path == ExecutablePath.Trim();
        Status = "Проверяем CLI…";
        try
        {
            var cli = await Task.Run(backend.Detect, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent()) return;
            if (path.Length > 0 && cli is not null && !string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(cli.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                Status = $"Указанный файл не распознан как CLI {backend.DisplayName}. Выберите программу этого агента.";
                return;
            }
            var models = cli is null ? [] : await Task.Run(backend.LoadModels, cancellationToken);
            if (!IsCurrent()) return;
            var model = Model;
            if (models.Count > 0)
                Models = models.Select(option => option.Id).Concat(string.IsNullOrWhiteSpace(model) ? [] : new[] { model }).Distinct().ToArray();
            Model = model;
            DetectedPath = cli?.ExecutablePath ?? "";
            Status = cli is null ? "CLI не найден. Установите агент или укажите путь к программе."
                : $"Найден {backend.DisplayName} {cli.Version}." + (models.Count == 0 ? " Каталог моделей недоступен, ID можно ввести вручную." : "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is System.IO.IOException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            if (IsCurrent()) Status = exception.Message;
        }
        finally { (backend as IDisposable)?.Dispose(); }
    }
}

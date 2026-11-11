using AIUpdater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Agents;

namespace AIUpdater.ViewModels;

public sealed class TranslationAgentViewModel : ObservableObject
{
    private readonly TranslationAgentSettings _settings;
    public TranslationAgentViewModel(TranslationAgentSettings settings)
    {
        _settings = settings;
        _backendId = settings.Current.BackendId;
        Automatic = settings.Current.Automatic;
        LoadProfile();
        SaveCommand = new RelayCommand(Save);
        DetectCommand = new AsyncRelayCommand(DetectAsync);
    }

    public IReadOnlyList<string> Backends => AgentBackendFactory.Ids;
    private string _backendId;
    public string BackendId
    {
        get => _backendId;
        set { if (SetProperty(ref _backendId, value)) LoadProfile(); }
    }
    public string Model { get; set; } = "";
    public string Effort { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public bool Automatic { get; set; }
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    public IReadOnlyList<string> Models { get; private set => SetProperty(ref field, value); } = [];
    public IReadOnlyList<string> Efforts { get; private set => SetProperty(ref field, value); } = ["", "low", "medium", "high"];
    public IRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand DetectCommand { get; }

    private void LoadProfile()
    {
        var profile = _settings.Current.Profiles.GetValueOrDefault(BackendId) ?? new();
        Model = profile.Model;
        Effort = profile.Effort;
        ExecutablePath = profile.ExecutablePath;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Effort));
        OnPropertyChanged(nameof(ExecutablePath));
        var backend = AgentBackendFactory.Create(BackendId);
        try { Efforts = ["", .. AgentModels.DefaultEfforts(backend.Kind)]; }
        finally { (backend as IDisposable)?.Dispose(); }
        Models = [];
    }

    private void Save()
    {
        try
        {
            var profiles = new Dictionary<string, TranslationAgentProfile>(_settings.Current.Profiles)
            {
                [BackendId] = new() { Model = Model.Trim(), Effort = Effort.Trim(), ExecutablePath = ExecutablePath.Trim() },
            };
            _settings.Save(new() { Automatic = Automatic, BackendId = BackendId, Profiles = profiles });
            Status = "Настройки сохранены";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { Status = exception.Message; }
    }

    private async Task DetectAsync()
    {
        var selectedBackend = BackendId;
        var backend = AgentBackendFactory.Create(BackendId, new() { ExecutablePath = ExecutablePath.Trim() });
        try
        {
            var cli = await Task.Run(backend.Detect);
            var models = cli is null ? [] : await Task.Run(backend.LoadModels);
            if (selectedBackend != BackendId) return;
            Models = models.Select(model => model.Id).ToArray();
            Status = cli is null ? "CLI не найден" : $"{backend.DisplayName} {cli.Version}: {cli.ExecutablePath}";
        }
        catch (Exception exception) when (exception is System.IO.IOException or System.ComponentModel.Win32Exception or ArgumentException) { Status = exception.Message; }
        finally { (backend as IDisposable)?.Dispose(); }
    }
}

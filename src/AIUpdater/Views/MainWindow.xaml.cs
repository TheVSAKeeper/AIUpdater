using AIUpdater.Infrastructure;
using AIUpdater.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AIUpdater.Views;

public partial class MainWindow
{
    public MainWindow()
        : this(App.Services.GetRequiredService<MainViewModel>())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        InitializeComponent();
        Title = $"AI Updater v{typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}";
        LogPanel.RegisterNames(this);
    }

    public MainViewModel ViewModel { get; }
}

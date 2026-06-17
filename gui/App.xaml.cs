using Microsoft.UI.Xaml;
using InariKontroller.Services;

namespace InariKontroller;

public partial class App : Application
{
    public static IpcClientService IpcClient { get; } = new();
    public static ProfileService   Profiles  { get; } = new();
    public static EngineRuntimeService EngineRuntime { get; } = new();
    public static DiagnosticUiStandalone DiagnosticUi { get; } = new();

    private Window? _window;

    public App() { InitializeComponent(); }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}

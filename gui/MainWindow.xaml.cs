using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using InariKontroller.Pages;
using InariKontroller.Services;
using System.Threading;

namespace InariKontroller;

public sealed partial class MainWindow : Window
{
    private readonly IpcClientService _ipc = App.IpcClient;
    private bool _isEngineConnected;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        SetupWindow();
        SetupIpc();
        ApplyLanguage();
        NavView.SelectedItem = NavDashboard;
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    private void SetupWindow()
    {
        if (AppWindow != null)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);
    }

    private void SetupIpc()
    {
        _ipc.ConnectionChanged += (_, connected) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _isEngineConnected = connected;
                StatusDot.Fill = connected
                    ? (SolidColorBrush)Application.Current.Resources["AccentGreenBrush"]
                    : (SolidColorBrush)Application.Current.Resources["AccentRedBrush"];
                StatusText.Text = App.DiagnosticUi.GetText(connected ? "EngineConnected" : "EngineDisconnected");
            });
        };

        _ = _ipc.StartAsync();
    }

    private void NavView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        switch (item.Tag?.ToString())
        {
            case "Dashboard": ContentFrame.Navigate(typeof(DashboardPage)); break;
            case "Circle": ContentFrame.Navigate(typeof(CircleSweepPage)); break;
            case "Profiles": ContentFrame.Navigate(typeof(ProfilesPage)); break;
            case "Runtime": ContentFrame.Navigate(typeof(RuntimePage)); break;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        App.IpcClient.SendCommand(new { type = "shutdown" });
        Thread.Sleep(300);
        App.IpcClient.Stop();
        App.EngineRuntime.StopEngine();
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        App.DiagnosticUi.ToggleLanguage();
        ApplyLanguage();

        if (ContentFrame.Content is DashboardPage dashboard)
        {
            dashboard.ApplyLanguage();
        }
        else if (ContentFrame.Content is CircleSweepPage circle)
        {
            circle.ApplyLanguage();
        }
        else if (ContentFrame.Content is ProfilesPage profiles)
        {
            profiles.ApplyLanguage();
        }
        else if (ContentFrame.Content is RuntimePage runtime)
        {
            runtime.ApplyLanguage();
        }
    }

    private void ApplyLanguage()
    {
        if (AppWindow != null)
        {
            AppWindow.Title = App.DiagnosticUi.IsJapanese ? "Inari-Kontroller - Questコントローラー補正" : "Inari-Kontroller - Quest Controller Correction";
        }
        NavDashboard.Content = App.DiagnosticUi.GetText("Dashboard");
        NavCircle.Content = App.DiagnosticUi.GetText("CircleSweep");
        NavProfiles.Content = App.DiagnosticUi.GetText("Profiles");
        NavRuntime.Content = App.DiagnosticUi.GetText("Runtime");
        LanguageButton.Content = App.DiagnosticUi.GetText("LanguageToggle");
        StatusText.Text = App.DiagnosticUi.GetText(_isEngineConnected ? "EngineConnected" : "EngineDisconnected");
    }
}


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VRStickScope.Services;

namespace VRStickScope.Pages;

public sealed partial class RuntimePage : Page
{
    private readonly EngineRuntimeService _runtime = App.EngineRuntime;

    public RuntimePage()
    {
        InitializeComponent();
        ApplyLanguage();
        _ = RefreshAutoStartAsync();
    }

    public void ApplyLanguage()
    {
        RuntimeTitle.Text = App.DiagnosticUi.GetText("RuntimeTitle");
        BtnStartOsc.Content = App.DiagnosticUi.GetText("StartVrChatOsc");
        BtnStopOsc.Content = App.DiagnosticUi.GetText("StopVrChatOsc");
        AutoStartSectionTitle.Text = App.DiagnosticUi.GetText("SteamVrAutoStart");
        AutoStartHelpText.Text = App.DiagnosticUi.GetText("SteamVrAutoStartHelp");
        BtnEnableAutoStart.Content = App.DiagnosticUi.GetText("EnableAutoStart");
        BtnDisableAutoStart.Content = App.DiagnosticUi.GetText("DisableAutoStart");
        VrChatOscSectionTitle.Text = App.DiagnosticUi.GetText("VrChatOscSection");
        VrChatOscHelpText.Text = App.DiagnosticUi.GetText("VrChatOscHelp");
    }

    private async System.Threading.Tasks.Task RefreshAutoStartAsync()
    {
        var state = await _runtime.GetAutoStartStateAsync();
        if (App.DiagnosticUi.IsJapanese)
        {
            AutoStartText.Text = state switch
            {
                SteamVrAutoStartState.Enabled => "SteamVR自動起動: 有効",
                SteamVrAutoStartState.Disabled => "SteamVR自動起動: 無効",
                _ => "SteamVR自動起動: 確認できません"
            };
        }
        else
        {
            AutoStartText.Text = state switch
            {
                SteamVrAutoStartState.Enabled => "SteamVR auto start: enabled",
                SteamVrAutoStartState.Disabled => "SteamVR auto start: disabled",
                _ => "SteamVR auto start: could not be checked"
            };
        }
    }

    private async void BtnStartOsc_Click(object sender, RoutedEventArgs e)
    {
        await StopEngineGracefullyAsync();
        _runtime.StartVrChatOsc();
    }

    private async void BtnStopOsc_Click(object sender, RoutedEventArgs e)
    {
        await StopEngineGracefullyAsync();
    }

    private async System.Threading.Tasks.Task StopEngineGracefullyAsync()
    {
        App.IpcClient.SendCommand(new { type = "shutdown" });
        await System.Threading.Tasks.Task.Delay(300);
        _runtime.StopEngine();
    }

    private async void BtnEnableAutoStart_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.InstallAutoStartAsync();
        await RefreshAutoStartAsync();
    }

    private async void BtnDisableAutoStart_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.UninstallAutoStartAsync();
        await RefreshAutoStartAsync();
    }
}

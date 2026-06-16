using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VRStickScope.Services;
using System;

namespace VRStickScope.Pages;

public sealed partial class RuntimePage : Page
{
    private readonly EngineRuntimeService _runtime = App.EngineRuntime;

    public RuntimePage()
    {
        InitializeComponent();
        ApplyLanguage();
        _ = RefreshAsync();
    }

    public void ApplyLanguage()
    {
        RuntimeTitle.Text = App.DiagnosticUi.GetText("RuntimeTitle");
        EngineSectionTitle.Text = App.DiagnosticUi.GetText("EngineSection");
        BtnStartDiagnostics.Content = App.DiagnosticUi.GetText("StartDiagnostics");
        BtnStartOsc.Content = App.DiagnosticUi.GetText("StartVrChatOsc");
        BtnStopEngine.Content = App.DiagnosticUi.GetText("StopEngine");
        BtnRefresh.Content = App.DiagnosticUi.GetText("Refresh");
        AutoStartSectionTitle.Text = App.DiagnosticUi.GetText("SteamVrAutoStart");
        BtnDisableAutoStart.Content = App.DiagnosticUi.GetText("DisableAutoStart");
        VrChatOscSectionTitle.Text = App.DiagnosticUi.GetText("VrChatOscSection");
        VrChatOscHelpText.Text = App.DiagnosticUi.GetText("VrChatOscHelp");
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        var path = _runtime.FindEnginePath();
        if (App.DiagnosticUi.IsJapanese)
        {
            EnginePathText.Text = path == null ? "エンジンパス: 見つかりません" : $"エンジンパス: {path}";
            EngineRunText.Text = _runtime.IsEngineRunning() ? "エンジン状態: 実行中" : "エンジン状態: 停止";
        }
        else
        {
            EnginePathText.Text = path == null ? "Engine path: not found" : $"Engine path: {path}";
            EngineRunText.Text = _runtime.IsEngineRunning() ? "Engine: running" : "Engine: stopped";
        }

        var state = await _runtime.GetAutoStartStateAsync();
        if (App.DiagnosticUi.IsJapanese)
        {
            AutoStartText.Text = state switch
            {
                SteamVrAutoStartState.Enabled => "旧バージョンのSteamVR自動起動が残っています",
                SteamVrAutoStartState.Disabled => "SteamVR自動起動: 未登録",
                _ => "SteamVR自動起動: 確認できません"
            };
        }
        else
        {
            AutoStartText.Text = state switch
            {
                SteamVrAutoStartState.Enabled => "SteamVR auto start from an older version is still registered",
                SteamVrAutoStartState.Disabled => "SteamVR auto start: not registered",
                _ => "SteamVR auto start: could not be checked"
            };
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => await RefreshAsync();

    private async void BtnStartDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        await StopEngineGracefullyAsync();
        _runtime.StartDiagnostics();
        await RefreshAsync();
    }

    private async void BtnStartOsc_Click(object sender, RoutedEventArgs e)
    {
        await StopEngineGracefullyAsync();
        _runtime.StartVrChatOsc();
        await RefreshAsync();
    }

    private async void BtnStopEngine_Click(object sender, RoutedEventArgs e)
    {
        await StopEngineGracefullyAsync();
        await RefreshAsync();
    }

    private async System.Threading.Tasks.Task StopEngineGracefullyAsync()
    {
        App.IpcClient.SendCommand(new { type = "shutdown" });
        await System.Threading.Tasks.Task.Delay(300);
        _runtime.StopEngine();
    }

    private async void BtnDisableAutoStart_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.UninstallAutoStartAsync();
        await RefreshAsync();
    }
}


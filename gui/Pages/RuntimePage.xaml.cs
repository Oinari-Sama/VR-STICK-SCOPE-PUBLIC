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
        BtnEnableAutoStart.Content = App.DiagnosticUi.GetText("EnableAutoStart");
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
                SteamVrAutoStartState.Enabled => "SteamVR起動時のOSC補正: 有効",
                SteamVrAutoStartState.Disabled => "SteamVR起動時のOSC補正: 無効",
                _ => "SteamVR起動時のOSC補正: 不明"
            };
        }
        else
        {
            AutoStartText.Text = state switch
            {
                SteamVrAutoStartState.Enabled => "OSC correction at SteamVR startup: enabled",
                SteamVrAutoStartState.Disabled => "OSC correction at SteamVR startup: disabled",
                _ => "OSC correction at SteamVR startup: unknown"
            };
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => await RefreshAsync();

    private async void BtnStartDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        _runtime.StartDiagnostics();
        await RefreshAsync();
    }

    private async void BtnStartOsc_Click(object sender, RoutedEventArgs e)
    {
        _runtime.StartVrChatOsc();
        await RefreshAsync();
    }

    private async void BtnStopEngine_Click(object sender, RoutedEventArgs e)
    {
        _runtime.StopEngine();
        await RefreshAsync();
    }

    private async void BtnEnableAutoStart_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.InstallAutoStartAsync();
        await RefreshAsync();
    }

    private async void BtnDisableAutoStart_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.UninstallAutoStartAsync();
        await RefreshAsync();
    }
}


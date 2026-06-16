using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VRStickScope.Models;
using VRStickScope.Services;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace VRStickScope.Pages;

public sealed partial class DashboardPage : Page
{
    private const int TrailLength = 20000;

    private readonly IpcClientService _ipc = App.IpcClient;
    private readonly List<(float rx, float ry)> _leftTrail = new();
    private readonly List<(float rx, float ry)> _rightTrail = new();
    private bool _engineStartedForDashboard;

    public DashboardPage()
    {
        InitializeComponent();
        ApplyLanguage();
        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;
    }

    public void ApplyLanguage()
    {
        DashboardTitle.Text = App.DiagnosticUi.GetText("Dashboard");
        BtnLeft.Content = App.DiagnosticUi.GetText("LeftStick");
        BtnRight.Content = App.DiagnosticUi.GetText("RightStick");
        LeftTitle.Text = App.DiagnosticUi.GetText("LeftStick");
        RightTitle.Text = App.DiagnosticUi.GetText("RightStick");
        ResetDiagnosticsButton.Content = App.DiagnosticUi.GetText("ResetDiagnostics");
        DiagnosticHelpText.Text = App.DiagnosticUi.GetText("DiagnosticHelp");
    }

    private void OnStateUpdated(object? sender, StateUpdatedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateUI(e.State));
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        _ipc.StateUpdated -= OnStateUpdated;
        _ipc.StateUpdated += OnStateUpdated;
        await StartDashboardEngineAsync();
    }

    private async void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _ipc.StateUpdated -= OnStateUpdated;
        await StopDashboardEngineAsync();
    }

    private async Task StartDashboardEngineAsync()
    {
        App.IpcClient.SendCommand(new { type = "shutdown" });
        await Task.Delay(300);
        App.EngineRuntime.StopEngine();
        await _ipc.WaitForDisconnectionAsync(TimeSpan.FromSeconds(2));

        _engineStartedForDashboard = App.EngineRuntime.StartDiagnostics();
        if (_engineStartedForDashboard)
        {
            await _ipc.WaitForConnectionAsync(TimeSpan.FromSeconds(5));
        }
    }

    private async Task StopDashboardEngineAsync()
    {
        if (!_engineStartedForDashboard) return;
        _engineStartedForDashboard = false;

        App.IpcClient.SendCommand(new { type = "shutdown" });
        await Task.Delay(300);
        App.EngineRuntime.StopEngine();
        await _ipc.WaitForDisconnectionAsync(TimeSpan.FromSeconds(2));
    }

    private void UpdateUI(EngineStateMessage s)
    {
        StickAxes left = s.Left;
        StickAxes right = s.Right;

        LeftRawX.Text = $"{RawLabel()} X: {left.Rx:+0.000;-0.000; 0.000}";
        LeftRawY.Text = $"{RawLabel()} Y: {left.Ry:+0.000;-0.000; 0.000}";
        LeftCorX.Text = $"{CorrectedLabel()} X: {left.Cx:+0.000;-0.000; 0.000}";
        LeftCorY.Text = $"{CorrectedLabel()} Y: {left.Cy:+0.000;-0.000; 0.000}";

        RightRawX.Text = $"{RawLabel()} X: {right.Rx:+0.000;-0.000; 0.000}";
        RightRawY.Text = $"{RawLabel()} Y: {right.Ry:+0.000;-0.000; 0.000}";
        RightCorX.Text = $"{CorrectedLabel()} X: {right.Cx:+0.000;-0.000; 0.000}";
        RightCorY.Text = $"{CorrectedLabel()} Y: {right.Cy:+0.000;-0.000; 0.000}";

        AddTrail(_leftTrail, (left.Rx, left.Ry));
        AddTrail(_rightTrail, (right.Rx, right.Ry));

        LeftPlot.UpdateStick(left.Rx, left.Ry, left.Cx, left.Cy, _leftTrail);
        RightPlot.UpdateStick(right.Rx, right.Ry, right.Cx, right.Cy, _rightTrail);
        PolarChart.UpdateData(_leftTrail, _rightTrail);
    }

    private static void AddTrail(List<(float, float)> trail, (float, float) pt)
    {
        trail.Add(pt);
        if (trail.Count > TrailLength) trail.RemoveAt(0);
    }

    private string RawLabel() => App.DiagnosticUi.IsJapanese ? "生入力" : "Raw";

    private string CorrectedLabel() => App.DiagnosticUi.IsJapanese ? "補正後" : "Corrected";

    private void BtnLeft_Click(object sender, RoutedEventArgs e)
    {
        BtnLeft.IsChecked = true;
        BtnRight.IsChecked = false;
    }

    private void BtnRight_Click(object sender, RoutedEventArgs e)
    {
        BtnLeft.IsChecked = false;
        BtnRight.IsChecked = true;
    }

    private void ResetDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        _leftTrail.Clear();
        _rightTrail.Clear();
        LeftPlot.UpdateStick(0f, 0f, 0f, 0f, _leftTrail);
        RightPlot.UpdateStick(0f, 0f, 0f, 0f, _rightTrail);
        PolarChart.UpdateData(_leftTrail, _rightTrail);
    }
}

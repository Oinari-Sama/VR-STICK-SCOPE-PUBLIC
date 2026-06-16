using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VRStickScope.Models;
using VRStickScope.Services;
using System;
using System.Diagnostics;
using System.Linq;

namespace VRStickScope.Pages;

public sealed partial class ProfilesPage : Page
{
    private readonly ProfileService _svc = App.Profiles;

    public ProfilesPage() { InitializeComponent(); ApplyLanguage(); LoadProfiles(); }

    public void ApplyLanguage()
    {
        ProfilesTitle.Text = App.DiagnosticUi.GetText("Profiles");
        BtnNew.Content = App.DiagnosticUi.GetText("New");
        BtnOpenFolder.Content = App.DiagnosticUi.GetText("OpenFolder");
        BtnRefresh.Content = App.DiagnosticUi.GetText("Refresh");
        LoadProfiles();
    }

    private void LoadProfiles() { ProfileList.ItemsSource = _svc.LoadAll(); }

    private async void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = App.DiagnosticUi.GetText("ProfileName") };
        var dialog = new ContentDialog
        {
            Title = App.DiagnosticUi.GetText("NewProfile"),
            Content = nameBox,
            PrimaryButtonText = App.DiagnosticUi.GetText("Create"),
            CloseButtonText = App.DiagnosticUi.GetText("Cancel"),
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _svc.Save(new CorrectionProfile
        {
            Name = string.IsNullOrWhiteSpace(nameBox.Text)
                ? $"Profile {DateTime.Now:yyyy-MM-dd HH:mm}" : nameBox.Text.Trim()
        });
        LoadProfiles();
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var id = btn.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        App.IpcClient.SendLoadProfile(id);
        var p = _svc.LoadAll().FirstOrDefault(x => x.Id == id);
        if (p != null) { App.IpcClient.SendLut("left", p.LeftLut); App.IpcClient.SendLut("right", p.RightLut); }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var id = btn.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        var dialog = new ContentDialog
        {
            Title = App.DiagnosticUi.GetText("Delete"),
            Content = App.DiagnosticUi.GetText("DeleteConfirm"),
            PrimaryButtonText = App.DiagnosticUi.GetText("Delete"),
            CloseButtonText = App.DiagnosticUi.GetText("Cancel"),
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _svc.Delete(id);
        LoadProfiles();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = _svc.ProfileDirectory, UseShellExecute = true });

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadProfiles();
    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
}


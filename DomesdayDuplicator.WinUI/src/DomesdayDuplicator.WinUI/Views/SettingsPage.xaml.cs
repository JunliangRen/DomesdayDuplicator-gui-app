// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DomesdayDuplicator.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DomesdayDuplicator.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void BrowseCaptureDir_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        // WinUI 3 desktop requires explicit HWND initialization
        var window = App.MainWindow
            ?? throw new InvalidOperationException("Main window not available");
        var hWnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, hWnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            ViewModel.Config.CaptureDirectory = folder.Path;
        }
    }
}

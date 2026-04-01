// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DomesdayDuplicator.WinUI.Views;
using WinRT.Interop;

namespace DomesdayDuplicator.WinUI;

/// <summary>
/// Main application window with NavigationView sidebar.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set minimum window size
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        appWindow.Title = "Domesday Duplicator";

        // Enable Mica backdrop for modern translucent look
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        // Extend content into title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Select Dashboard by default
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item) return;

        var tag = item.Tag?.ToString();
        var pageType = tag switch
        {
            "Dashboard" => typeof(DashboardPage),
            "Capture" => typeof(CapturePage),
            "DataConversion" => typeof(DataConversionPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        ContentFrame.Navigate(pageType);
    }
}

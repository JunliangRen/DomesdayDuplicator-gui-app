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
    private readonly Microsoft.UI.Windowing.AppWindow _appWindow;
    private Microsoft.UI.Xaml.Media.MicaBackdrop? _micaBackdrop;
    private bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnWindowClosed;

        // Set minimum window size
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        _appWindow.Title = "Domesday Duplicator";

        // Enable Mica backdrop for modern translucent look
        _micaBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        SystemBackdrop = _micaBackdrop;

        // Extend content into title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Select Dashboard by default
        if (NavView.MenuItems.Count > 0)
            NavView.SelectedItem = NavView.MenuItems[0];

        NavView.IsPaneOpen = false;
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

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        Closed -= OnWindowClosed;
        NavView.Loaded -= NavView_Loaded;
        NavView.SelectionChanged -= NavView_SelectionChanged;
        ExtendsContentIntoTitleBar = false;
        SetTitleBar(null);
        SystemBackdrop = null;
        _micaBackdrop = null;
        ContentFrame.Content = null;
    }
}

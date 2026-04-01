// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DomesdayDuplicator.WinUI.ViewModels;

namespace DomesdayDuplicator.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new();

    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Initialize(DispatcherQueue);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Uninitialize();
    }
}

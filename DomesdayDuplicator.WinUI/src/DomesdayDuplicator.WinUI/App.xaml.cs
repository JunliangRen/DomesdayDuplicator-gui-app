// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using Microsoft.UI.Xaml;
using DomesdayDuplicator.WinUI.Helpers;
using DomesdayDuplicator.WinUI.Services;

namespace DomesdayDuplicator.WinUI;

/// <summary>
/// Application entry point. Registers services, loads configuration,
/// and sets GC latency mode for real-time capture.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // ── GC tuning for real-time USB capture ─────────────────
        // SustainedLowLatency minimizes Gen2 GC pauses during capture.
        // Combined with NativeMemory buffers (which GC never touches),
        // this ensures the capture pipeline stays uninterrupted.
        System.Runtime.GCSettings.LatencyMode =
            System.Runtime.GCLatencyMode.SustainedLowLatency;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // ── Register services ───────────────────────────────────
        var configService = new ConfigurationService();
        configService.Load();

        ServiceLocator.Register(
            usbCapture: new UsbCaptureService(),
            dataConversion: new DataConversionService(),
            configuration: configService);

        // ── Create main window ──────────────────────────────────
        _window = new MainWindow();
        _window.Activate();
    }
}

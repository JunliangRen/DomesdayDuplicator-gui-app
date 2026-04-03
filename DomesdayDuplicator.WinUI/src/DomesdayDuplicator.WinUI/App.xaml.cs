// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using System.Diagnostics;
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

    /// <summary>
    /// The main application window. Used by pages to obtain a window handle
    /// for file/folder pickers that require HWND initialization in WinUI 3.
    /// </summary>
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        // ── GC tuning for real-time USB capture ─────────────────
        // SustainedLowLatency minimizes Gen2 GC pauses during capture.
        // Combined with NativeMemory buffers (which GC never touches),
        // this ensures the capture pipeline stays uninterrupted.
        System.Runtime.GCSettings.LatencyMode =
            System.Runtime.GCLatencyMode.SustainedLowLatency;

        // ── Global exception handling ───────────────────────────
        UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Prevent crash from unhandled exceptions (including native/SEH)
        // so the user gets a chance to see the error instead of a silent crash.
        e.Handled = true;
        Debug.WriteLine($"[Unhandled Exception] {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // ── Register services ───────────────────────────────
            var configService = new ConfigurationService();
            configService.Load();

            ServiceLocator.Register(
                usbCapture: new UsbCaptureService(),
                dataConversion: new DataConversionService(),
                configuration: configService);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App.OnLaunched] Service initialization failed: {ex}");
            // Continue with window creation even if services partially failed;
            // individual features will degrade gracefully.
        }

        // ── Create main window ──────────────────────────────────
        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
    }
}

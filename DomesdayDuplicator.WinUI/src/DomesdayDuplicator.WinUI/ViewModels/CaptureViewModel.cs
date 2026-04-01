// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DomesdayDuplicator.WinUI.Helpers;
using DomesdayDuplicator.WinUI.Models;

namespace DomesdayDuplicator.WinUI.ViewModels;

/// <summary>
/// ViewModel for the main capture page.
/// Controls capture start/stop, monitors real-time statistics,
/// and manages disc metadata for file naming.
/// </summary>
public sealed partial class CaptureViewModel : ObservableObject
{
    private readonly Services.IUsbCaptureService _usb;
    private readonly Services.IConfigurationService _config;

    // ── Capture state ────────────────────────────────────────────
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _isDeviceConnected;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private CaptureStatistics _statistics = new();

    // ── Capture options ──────────────────────────────────────────
    [ObservableProperty] private bool _isTestMode;
    [ObservableProperty] private bool _limitDuration;
    [ObservableProperty] private TimeSpan _durationLimit = TimeSpan.FromMinutes(60);

    // ── Disc metadata ────────────────────────────────────────────
    [ObservableProperty] private string _discTitle = string.Empty;
    [ObservableProperty] private bool _isCav = true;
    [ObservableProperty] private bool _isNtsc = true;
    [ObservableProperty] private int _sideNumber = 1;
    [ObservableProperty] private string _audioType = "Stereo";
    [ObservableProperty] private string _captureNotes = string.Empty;

    // ── Amplitude visualization ──────────────────────────────────
    [ObservableProperty] private double _rmsAmplitude;
    [ObservableProperty] private double[] _amplitudeWaveform = [];

    // ── Timing ───────────────────────────────────────────────────
    private Stopwatch? _captureTimer;
    private CancellationTokenSource? _durationCts;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _updateTimer;

    public CaptureViewModel()
    {
        _usb = ServiceLocator.UsbCapture;
        _config = ServiceLocator.Configuration;
    }

    public void Initialize(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _updateTimer = dispatcher.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromMilliseconds(100);
        _updateTimer.Tick += OnUpdateTimerTick;
        _updateTimer.Start();

        IsDeviceConnected = _usb.IsDeviceConnected;
        _usb.DeviceConnectionChanged += (_, c) => IsDeviceConnected = c;
    }

    public void Uninitialize()
    {
        _updateTimer?.Stop();
    }

    private void OnUpdateTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        IsDeviceConnected = _usb.IsDeviceConnected;

        if (!IsCapturing) return;

        _usb.ReadStatistics(Statistics);
        Statistics.ElapsedTime = _captureTimer?.Elapsed ?? TimeSpan.Zero;
        RmsAmplitude = Statistics.RmsAmplitude;
        OnPropertyChanged(nameof(Statistics));

        // Check for capture errors
        if (Statistics.LastResult != TransferResult.Running &&
            Statistics.LastResult != TransferResult.Success)
        {
            StatusMessage = $"Capture error: {Statistics.LastResult}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartCapture))]
    private async Task StartCaptureAsync()
    {
        if (IsCapturing) return;

        var config = _config.Configuration;
        string outputPath = GenerateOutputFilePath(config);

        Statistics.Reset();
        StatusMessage = "Starting capture...";

        bool started = _usb.StartCapture(
            outputPath,
            config.CaptureFormat,
            IsTestMode,
            config.UseSmallUsbTransfers,
            config.DiskBufferQueueSize);

        if (!started)
        {
            StatusMessage = "Failed to start capture. Check device connection.";
            return;
        }

        IsCapturing = true;
        _captureTimer = Stopwatch.StartNew();
        StatusMessage = $"Capturing to: {Path.GetFileName(outputPath)}";

        // Duration limit
        if (LimitDuration)
        {
            _durationCts = new CancellationTokenSource();
            _ = Task.Delay(DurationLimit, _durationCts.Token)
                .ContinueWith(_ => StopCaptureCommand.Execute(null),
                    _durationCts.Token,
                    TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.Default);
        }
    }

    private bool CanStartCapture() => IsDeviceConnected && !IsCapturing;

    [RelayCommand]
    private async Task StopCaptureAsync()
    {
        if (!IsCapturing) return;

        StatusMessage = "Stopping capture...";
        _durationCts?.Cancel();
        _captureTimer?.Stop();

        await _usb.StopCaptureAsync();

        IsCapturing = false;
        Statistics.IsCapturing = false;
        StatusMessage = $"Capture complete. {Statistics.FormattedFileSize} written in {Statistics.ElapsedTime:hh\\:mm\\:ss}";
    }

    private string GenerateOutputFilePath(CaptureConfiguration config)
    {
        string dir = config.CaptureDirectory;
        if (string.IsNullOrEmpty(dir))
            dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Directory.CreateDirectory(dir);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string title = string.IsNullOrWhiteSpace(DiscTitle) ? "capture" : DiscTitle;
        string side = $"side{SideNumber}";

        string extension = config.CaptureFormat == CaptureFormat.Signed16Bit ? ".raw" : ".dd";
        string filename = $"{title}_{side}_{timestamp}{extension}";

        // Sanitize filename
        foreach (char c in Path.GetInvalidFileNameChars())
            filename = filename.Replace(c, '_');

        return Path.Combine(dir, filename);
    }
}

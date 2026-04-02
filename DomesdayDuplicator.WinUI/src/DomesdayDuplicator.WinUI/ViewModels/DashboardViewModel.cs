// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DomesdayDuplicator.WinUI.Helpers;
using DomesdayDuplicator.WinUI.Models;

namespace DomesdayDuplicator.WinUI.ViewModels;

/// <summary>
/// Dashboard/home page ViewModel. Shows device status,
/// quick capture controls, and summary statistics.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly Services.IUsbCaptureService _usb;
    private readonly Services.IConfigurationService _config;

    // ── Device status ────────────────────────────────────────────
    [ObservableProperty] private bool _isDeviceConnected;
    [ObservableProperty] private string _deviceStatusText = "No device connected";
    [ObservableProperty] private string _deviceDescription = string.Empty;

    // ── Disk space ───────────────────────────────────────────────
    [ObservableProperty] private long _diskFreeBytes;
    [ObservableProperty] private string _diskFreeText = "—";
    [ObservableProperty] private string _diskCapturableTime = "—";

    // ── Quick capture ────────────────────────────────────────────
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private CaptureStatistics _statistics = new();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _statusTimer;

    public DashboardViewModel()
    {
        _usb = ServiceLocator.UsbCapture;
        _config = ServiceLocator.Configuration;
        _usb.DeviceConnectionChanged += OnDeviceConnectionChanged;
    }

    /// <summary>
    /// Called when the page is loaded. Start device polling.
    /// </summary>
    public void Initialize(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _statusTimer = dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(200);
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();

        RefreshDeviceStatus();
        UpdateDiskSpace();
    }

    public void Uninitialize()
    {
        _statusTimer?.Stop();
    }

    private void OnStatusTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (IsCapturing)
        {
            _usb.ReadStatistics(Statistics);
            OnPropertyChanged(nameof(Statistics));
        }

        RefreshDeviceStatus();
    }

    [RelayCommand]
    private void RefreshDeviceStatus()
    {
        IsDeviceConnected = _usb.IsDeviceConnected;
        IsCapturing = _usb.IsCapturing;

        if (IsDeviceConnected && _usb.ConnectedDevice != null)
        {
            DeviceStatusText = "Device connected";
            DeviceDescription = _usb.ConnectedDevice.Description;
        }
        else
        {
            DeviceStatusText = "No device connected";
            DeviceDescription = string.Empty;

            // Auto-scan for device
            TryScanForDevice();
        }
    }

    [RelayCommand]
    private void TryScanForDevice()
    {
        try
        {
            var config = _config.Configuration;
            var devices = _usb.EnumerateDevices();

            foreach (var path in devices)
            {
                var device = _usb.Connect(path, config.UsbVendorId, config.UsbProductId);
                if (device != null)
                {
                    IsDeviceConnected = true;
                    DeviceStatusText = "Device connected";
                    DeviceDescription = device.Description;
                    return;
                }
            }
        }
        catch
        {
            // Native interop failure — device scanning unavailable
            DeviceStatusText = "Device scan failed";
        }
    }

    [RelayCommand]
    private void UpdateDiskSpace()
    {
        try
        {
            var dir = _config.Configuration.CaptureDirectory;
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var driveInfo = new DriveInfo(Path.GetPathRoot(dir)!);
            DiskFreeBytes = driveInfo.AvailableFreeSpace;
            DiskFreeText = DiskFreeBytes switch
            {
                >= 1L << 30 => $"{DiskFreeBytes / (double)(1L << 30):F1} GB",
                >= 1L << 20 => $"{DiskFreeBytes / (double)(1L << 20):F1} MB",
                _ => $"{DiskFreeBytes / (double)(1L << 10):F1} KB"
            };

            // Calculate capturable time at 40MHz × 2 bytes per sample = 80 MB/s
            double seconds = DiskFreeBytes / (80.0 * 1024 * 1024);
            DiskCapturableTime = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
        }
        catch
        {
            DiskFreeText = "Unknown";
            DiskCapturableTime = "—";
        }
    }

    private void OnDeviceConnectionChanged(object? sender, bool connected)
    {
        IsDeviceConnected = connected;
        RefreshDeviceStatus();
    }
}

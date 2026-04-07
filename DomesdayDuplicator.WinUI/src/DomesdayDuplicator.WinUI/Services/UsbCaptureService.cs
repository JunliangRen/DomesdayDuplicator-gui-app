// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using DomesdayDuplicator.WinUI.Models;
using DomesdayDuplicator.WinUI.Native;

namespace DomesdayDuplicator.WinUI.Services;

/// <summary>
/// Service for managing USB device connection and capture sessions.
/// Wraps <see cref="NativeCaptureEngine"/> with a UI-friendly interface.
/// </summary>
public interface IUsbCaptureService : IDisposable
{
    /// <summary>Whether a device is currently connected.</summary>
    bool IsDeviceConnected { get; }

    /// <summary>Whether a capture session is in progress.</summary>
    bool IsCapturing { get; }

    /// <summary>Connected device information (null if none).</summary>
    DeviceInfo? ConnectedDevice { get; }

    /// <summary>Scan for connected Domesday Duplicator devices.</summary>
    List<string> EnumerateDevices();

    /// <summary>Connect to a specific device by path.</summary>
    DeviceInfo? Connect(string devicePath, ushort vid, ushort pid);

    /// <summary>Disconnect the current device.</summary>
    void Disconnect();

    /// <summary>Start a capture session.</summary>
    bool StartCapture(string outputFilePath, CaptureFormat format, bool testMode,
        bool useSmallTransfers, long diskBufferQueueSize);

    /// <summary>Stop the current capture session.</summary>
    Task StopCaptureAsync();

    /// <summary>Apply the FPGA test mode setting to the connected device.</summary>
    bool SetTestMode(bool testMode);

    /// <summary>Read the latest capture statistics.</summary>
    void ReadStatistics(CaptureStatistics stats);

    /// <summary>Raised when device connection state changes.</summary>
    event EventHandler<bool>? DeviceConnectionChanged;
}

/// <summary>
/// Default implementation using <see cref="NativeCaptureEngine"/>.
/// </summary>
public sealed class UsbCaptureService : IUsbCaptureService, IAsyncDisposable
{
    private readonly NativeCaptureEngine _engine = new();
    private DeviceInfo? _device;

    public bool IsDeviceConnected => _device != null;
    public bool IsCapturing => _engine.IsCapturing;
    public DeviceInfo? ConnectedDevice => _device;

    public event EventHandler<bool>? DeviceConnectionChanged;

    public List<string> EnumerateDevices() => NativeCaptureEngine.EnumerateDevices();

    public DeviceInfo? Connect(string devicePath, ushort vid, ushort pid)
    {
        var device = _engine.OpenDevice(devicePath, vid, pid);
        if (device == null)
        {
            return null;
        }

        var wasConnected = _device != null;
        _device = device;

        if (!wasConnected)
        {
            DeviceConnectionChanged?.Invoke(this, true);
        }

        return _device;
    }

    public void Disconnect()
    {
        if (IsCapturing)
        {
            StopCaptureAsync().GetAwaiter().GetResult();
        }

        _engine.CloseDevice();
        _device = null;
        DeviceConnectionChanged?.Invoke(this, false);
    }

    public bool StartCapture(string outputFilePath, CaptureFormat format, bool testMode,
        bool useSmallTransfers, long diskBufferQueueSize)
        => _engine.StartCapture(outputFilePath, format, testMode, useSmallTransfers, diskBufferQueueSize);

    public Task StopCaptureAsync() => _engine.StopCaptureAsync();

    public bool SetTestMode(bool testMode) => _engine.SendConfigurationCommand(testMode);

    public void ReadStatistics(CaptureStatistics stats) => _engine.ReadStatistics(stats);

    public void Dispose() => _engine.Dispose();

    public ValueTask DisposeAsync() => _engine.DisposeAsync();
}

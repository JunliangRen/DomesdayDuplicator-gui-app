// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using CommunityToolkit.Mvvm.ComponentModel;

namespace DomesdayDuplicator.WinUI.Models;

/// <summary>
/// Application-wide capture and device configuration.
/// Persisted to local settings.
/// </summary>
public sealed partial class CaptureConfiguration : ObservableObject
{
    // ── USB device identification ────────────────────────────────
    [ObservableProperty] private ushort _usbVendorId = 0x1D50;
    [ObservableProperty] private ushort _usbProductId = 0x603B;
    [ObservableProperty] private string _preferredDevicePath = string.Empty;

    // ── Capture settings ─────────────────────────────────────────
    [ObservableProperty] private string _captureDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    [ObservableProperty] private CaptureFormat _captureFormat = CaptureFormat.Signed16Bit;
    [ObservableProperty] private bool _isTestMode;

    // ── USB transfer tuning ──────────────────────────────────────
    /// <summary>Total disk buffer queue size in bytes (default 256 MB).</summary>
    [ObservableProperty] private long _diskBufferQueueSize = 256L * 1024 * 1024;

    /// <summary>Use 128 KB small transfers instead of large bulk reads.</summary>
    [ObservableProperty] private bool _useSmallUsbTransfers = true;

    /// <summary>Use WinUSB API (true) or LibUSB (false) on Windows.</summary>
    [ObservableProperty] private bool _useWinUsb = true;

    /// <summary>Use overlapped/async file I/O on Windows.</summary>
    [ObservableProperty] private bool _useAsyncFileIo = true;

    // ── UI preferences ───────────────────────────────────────────
    [ObservableProperty] private bool _showAmplitudeLabel = true;
    [ObservableProperty] private bool _showAmplitudeChart = true;
    [ObservableProperty] private bool _showAdvancedCaptureStats;
    [ObservableProperty] private bool _resetNotesOnSideChange = true;
    [ObservableProperty] private bool _resetMintMarksOnSideChange = true;

    // ── Disc metadata (for file naming) ──────────────────────────
    [ObservableProperty] private string _discTitle = string.Empty;
    [ObservableProperty] private bool _isCav = true;
    [ObservableProperty] private bool _isNtsc = true;
    [ObservableProperty] private int _sideNumber = 1;
    [ObservableProperty] private string _audioType = "Stereo";
    [ObservableProperty] private string _captureNotes = string.Empty;
    [ObservableProperty] private string _mintMarks = string.Empty;
}

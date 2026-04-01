// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using CommunityToolkit.Mvvm.ComponentModel;

namespace DomesdayDuplicator.WinUI.Models;

/// <summary>
/// Real-time statistics of an in-progress capture session.
/// Updated from the capture engine on a timer.
/// </summary>
public sealed partial class CaptureStatistics : ObservableObject
{
    // ── Transfer counters ────────────────────────────────────────
    [ObservableProperty] private long _totalTransfers;
    [ObservableProperty] private long _diskBuffersWritten;
    [ObservableProperty] private long _fileSizeBytes;
    [ObservableProperty] private long _processedSamples;

    // ── Signal statistics (all-time) ─────────────────────────────
    [ObservableProperty] private int _minSampleValue;
    [ObservableProperty] private int _maxSampleValue;
    [ObservableProperty] private long _clippedMinCount;
    [ObservableProperty] private long _clippedMaxCount;

    // ── Signal statistics (recent buffer) ────────────────────────
    [ObservableProperty] private int _recentMinSampleValue;
    [ObservableProperty] private int _recentMaxSampleValue;
    [ObservableProperty] private long _recentClippedMinCount;
    [ObservableProperty] private long _recentClippedMaxCount;

    // ── Amplitude ────────────────────────────────────────────────
    [ObservableProperty] private double _rmsAmplitude;
    [ObservableProperty] private double _meanAmplitude;
    [ObservableProperty] private double _captureRateKBps;

    // ── Timing ───────────────────────────────────────────────────
    [ObservableProperty] private TimeSpan _elapsedTime;
    [ObservableProperty] private TimeSpan _remainingDiskTime;

    // ── Status ───────────────────────────────────────────────────
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private TransferResult _lastResult = TransferResult.Success;
    [ObservableProperty] private bool _hasSequenceNumbers;

    /// <summary>
    /// Formatted file size string (e.g. "1.23 GB").
    /// </summary>
    public string FormattedFileSize => FileSizeBytes switch
    {
        >= 1L << 30 => $"{FileSizeBytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{FileSizeBytes / (double)(1L << 20):F2} MB",
        >= 1L << 10 => $"{FileSizeBytes / (double)(1L << 10):F2} KB",
        _ => $"{FileSizeBytes} B"
    };

    public void Reset()
    {
        TotalTransfers = 0;
        DiskBuffersWritten = 0;
        FileSizeBytes = 0;
        ProcessedSamples = 0;
        MinSampleValue = 0;
        MaxSampleValue = 0;
        ClippedMinCount = 0;
        ClippedMaxCount = 0;
        RecentMinSampleValue = 0;
        RecentMaxSampleValue = 0;
        RecentClippedMinCount = 0;
        RecentClippedMaxCount = 0;
        RmsAmplitude = 0;
        MeanAmplitude = 0;
        CaptureRateKBps = 0;
        ElapsedTime = TimeSpan.Zero;
        IsCapturing = false;
        LastResult = TransferResult.Success;
        HasSequenceNumbers = false;
    }
}

// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Services.IUsbCaptureService _usb;
    private readonly Services.IConfigurationService _config;
    private readonly Services.IDataConversionService _dataConversion;
    private readonly EventHandler<bool> _deviceConnectionChangedHandler;

    // ── Capture state ────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    [NotifyPropertyChangedFor(nameof(IsDeviceDisconnected))]
    private bool _isDeviceConnected;

    public bool IsDeviceDisconnected => !IsDeviceConnected;

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
    private bool _isScanningForDevice;
    private bool _isInitialized;
    private string? _currentOutputPath;
    private DateTimeOffset? _captureStartedUtc;

    public CaptureViewModel()
    {
        _usb = ServiceLocator.UsbCapture;
        _config = ServiceLocator.Configuration;
        _dataConversion = ServiceLocator.DataConversion;
        _deviceConnectionChangedHandler = OnDeviceConnectionChanged;

        var config = _config.Configuration;
        IsTestMode = config.IsTestMode;
        DiscTitle = config.DiscTitle;
        IsCav = config.IsCav;
        IsNtsc = config.IsNtsc;
        SideNumber = config.SideNumber;
        AudioType = config.AudioType;
        CaptureNotes = config.CaptureNotes;
    }

    public void Initialize(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        Uninitialize();
        _isInitialized = true;

        _updateTimer = dispatcher.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromMilliseconds(100);
        _updateTimer.Tick += OnUpdateTimerTick;
        _updateTimer.Start();

        IsDeviceConnected = _usb.IsDeviceConnected;
        _usb.DeviceConnectionChanged -= _deviceConnectionChangedHandler;
        _usb.DeviceConnectionChanged += _deviceConnectionChangedHandler;

        if (!IsDeviceConnected)
        {
            TryScanForDevice();
        }
    }

    public void Uninitialize()
    {
        _isInitialized = false;

        if (_updateTimer != null)
        {
            _updateTimer.Tick -= OnUpdateTimerTick;
            _updateTimer.Stop();
            _updateTimer = null;
        }

        _usb.DeviceConnectionChanged -= _deviceConnectionChangedHandler;
    }

    private void OnDeviceConnectionChanged(object? sender, bool connected)
    {
        IsDeviceConnected = connected;

        if (connected && !IsCapturing)
        {
            ApplyTestModeConfiguration();
        }

        if (IsCapturing)
        {
            return;
        }

        StatusMessage = connected ? "Ready" : "No device connected";
    }

    partial void OnIsTestModeChanged(bool value)
    {
        if (!_isInitialized || IsCapturing)
        {
            return;
        }

        ApplyTestModeConfiguration();
    }

    private void ApplyTestModeConfiguration()
    {
        if (!_usb.IsDeviceConnected || IsCapturing)
        {
            return;
        }

        bool applied = _usb.SetTestMode(IsTestMode);
        Debug.WriteLine($"[CaptureViewModel] ApplyTestModeConfiguration testMode={IsTestMode}, applied={applied}");
    }

    private void OnUpdateTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        IsDeviceConnected = _usb.IsDeviceConnected;

        if (!IsDeviceConnected)
        {
            TryScanForDevice();
        }

        if (!IsCapturing) return;

        _usb.ReadStatistics(Statistics);
        Statistics.ElapsedTime = _captureTimer?.Elapsed ?? TimeSpan.Zero;
        RmsAmplitude = Statistics.RmsAmplitude;
        OnPropertyChanged(nameof(Statistics));

        // Check for capture errors
        if (Statistics.LastResult != TransferResult.Running &&
            Statistics.LastResult != TransferResult.Success &&
            !(IsTestMode && Statistics.LastResult == TransferResult.VerificationError))
        {
            StatusMessage = $"Capture error: {Statistics.LastResult}";
        }
    }

    private void TryScanForDevice()
    {
        if (_isScanningForDevice || _usb.IsDeviceConnected)
        {
            return;
        }

        _isScanningForDevice = true;
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
                    StatusMessage = "Ready";
                    return;
                }
            }
        }
        catch
        {
            StatusMessage = "Device scan failed";
        }
        finally
        {
            _isScanningForDevice = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartCapture))]
    private async Task StartCaptureAsync()
    {
        if (IsCapturing) return;

        _durationCts?.Dispose();
        _durationCts = null;

        var config = _config.Configuration;
        config.IsTestMode = IsTestMode;
        config.DiscTitle = DiscTitle;
        config.IsCav = IsCav;
        config.IsNtsc = IsNtsc;
        config.SideNumber = SideNumber;
        config.AudioType = AudioType;
        config.CaptureNotes = CaptureNotes;
        _config.Save();

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
        _currentOutputPath = outputPath;
        _captureStartedUtc = DateTimeOffset.UtcNow;
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
        var durationCts = _durationCts;
        _durationCts = null;

        if (durationCts != null)
        {
            await durationCts.CancelAsync();
            durationCts.Dispose();
        }

        _captureTimer?.Stop();

        await _usb.StopCaptureAsync();

        _usb.ReadStatistics(Statistics);
        Statistics.ElapsedTime = _captureTimer?.Elapsed ?? TimeSpan.Zero;

        var outputPath = _currentOutputPath;
        var config = _config.Configuration;

        if (IsTestMode && !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
        {
            StatusMessage = "Verifying captured test data...";

            bool verified = await _dataConversion.VerifyTestDataAsync(
                outputPath,
                config.CaptureFormat is not CaptureFormat.Signed16Bit,
                progress: null,
                CancellationToken.None);

            Statistics.LastResult = verified ? TransferResult.Success : TransferResult.VerificationError;
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            WriteCaptureMetadataFile(outputPath, config, Statistics.ElapsedTime);
        }

        IsCapturing = false;
        Statistics.IsCapturing = false;
        _captureStartedUtc = null;
        _currentOutputPath = null;

        StatusMessage = Statistics.LastResult switch
        {
            TransferResult.Success when IsTestMode => $"Capture complete. Test data verified. {Statistics.FormattedFileSize} written in {Statistics.ElapsedTime:hh\\:mm\\:ss}",
            TransferResult.VerificationError => "Capture complete, but test data verification failed.",
            _ => $"Capture complete. {Statistics.FormattedFileSize} written in {Statistics.ElapsedTime:hh\\:mm\\:ss}"
        };
    }

    private string GenerateOutputFilePath(CaptureConfiguration config)
    {
        string dir = config.CaptureDirectory;
        if (string.IsNullOrEmpty(dir))
            dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Directory.CreateDirectory(dir);

        string filename = GenerateCaptureFileName(config);

        return Path.Combine(dir, filename);
    }

    private string GenerateCaptureFileName(CaptureConfiguration config)
    {
        var parts = new List<string>();

        if (IsTestMode)
        {
            parts.Add("TestData");
        }
        else
        {
            parts.Add(string.IsNullOrWhiteSpace(DiscTitle) ? "RF-Sample" : DiscTitle.Trim());
            parts.Add(IsCav ? "CAV" : "CLV");
            parts.Add(IsNtsc ? "NTSC" : "PAL");

            string audioSegment = SanitizeFileNameSegment(AudioType.Trim());
            if (!string.IsNullOrWhiteSpace(audioSegment))
            {
                parts.Add(audioSegment);
            }

            parts.Add($"side{SideNumber}");

            string notesSegment = SanitizeFileNameSegment(CaptureNotes.Trim());
            if (!string.IsNullOrWhiteSpace(notesSegment))
            {
                parts.Add(notesSegment);
            }

            string mintMarksSegment = SanitizeFileNameSegment(config.MintMarks.Trim());
            if (!string.IsNullOrWhiteSpace(mintMarksSegment))
            {
                parts.Add(mintMarksSegment);
            }
        }

        parts.Add(DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

        string fileName = string.Join("_", parts.Where(static p => !string.IsNullOrWhiteSpace(p)));
        return fileName + GetCaptureFileExtension(config.CaptureFormat);
    }

    private static string GetCaptureFileExtension(CaptureFormat format) => format switch
    {
        CaptureFormat.Unsigned10Bit => ".lds",
        CaptureFormat.Unsigned10BitDecimated => ".cds",
        _ => ".raw"
    };

    private static string SanitizeFileNameSegment(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value.Trim();
    }

    private void WriteCaptureMetadataFile(string outputPath, CaptureConfiguration config, TimeSpan elapsed)
    {
        try
        {
            var device = _usb.ConnectedDevice;
            var serialInfo = new Dictionary<string, object?>();
            if (device != null)
            {
                serialInfo["devicePath"] = device.DevicePath;
                serialInfo["deviceDescription"] = device.Description;
                serialInfo["deviceSpeed"] = device.DeviceSpeed;
                serialInfo["bulkInPipeId"] = device.BulkInPipeId;
                serialInfo["maxTransferSize"] = device.MaxTransferSize;
            }

            serialInfo["usbVendorId"] = $"0x{config.UsbVendorId:X4}";
            serialInfo["usbProductId"] = $"0x{config.UsbProductId:X4}";

            var namingInfo = new Dictionary<string, object?>
            {
                ["title"] = string.IsNullOrWhiteSpace(DiscTitle) ? null : DiscTitle,
                ["type"] = IsCav ? "CAV" : "CLV",
                ["format"] = IsNtsc ? "NTSC" : "PAL",
                ["audioType"] = string.IsNullOrWhiteSpace(AudioType) ? null : AudioType,
                ["side"] = SideNumber,
                ["notes"] = string.IsNullOrWhiteSpace(CaptureNotes) ? null : CaptureNotes,
                ["mintMarks"] = string.IsNullOrWhiteSpace(config.MintMarks) ? null : config.MintMarks
            };

            var captureInfo = new Dictionary<string, object?>
            {
                ["transferResult"] = Statistics.LastResult.ToString(),
                ["durationInMilliseconds"] = (long)elapsed.TotalMilliseconds,
                ["transferCount"] = Statistics.TotalTransfers,
                ["numberOfDiskBuffersWritten"] = Statistics.DiskBuffersWritten,
                ["fileSizeWrittenInBytes"] = Statistics.FileSizeBytes,
                ["sampleCount"] = Statistics.ProcessedSamples,
                ["minSampleValue"] = Statistics.MinSampleValue,
                ["maxSampleValue"] = Statistics.MaxSampleValue,
                ["clippedMinSampleCount"] = Statistics.ClippedMinCount,
                ["clippedMaxSampleCount"] = Statistics.ClippedMaxCount,
                ["sequenceMarkersPresent"] = Statistics.HasSequenceNumbers,
                ["captureFormat"] = config.CaptureFormat.ToString(),
                ["testMode"] = IsTestMode,
                ["creationTimestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["captureStartTimestamp"] = _captureStartedUtc?.ToString("O")
            };

            var infoFile = new Dictionary<string, object?>
            {
                ["serialInfo"] = serialInfo,
                ["namingInfo"] = namingInfo,
                ["captureInfo"] = captureInfo,
                ["timeSampledData"] = new Dictionary<string, object?>()
            };

            string metadataFilePath = Path.ChangeExtension(outputPath, ".json");
            string json = JsonSerializer.Serialize(infoFile, MetadataJsonOptions);
            File.WriteAllText(metadataFilePath, json);
        }
        catch
        {
            // Ignore metadata export errors to avoid masking capture completion.
        }
    }
}

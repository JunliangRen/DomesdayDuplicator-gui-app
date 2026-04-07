// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DomesdayDuplicator.WinUI.Helpers;

namespace DomesdayDuplicator.WinUI.ViewModels;

/// <summary>
/// ViewModel for the data conversion page (10-bit ↔ 16-bit).
/// </summary>
public sealed partial class DataConversionViewModel : ObservableObject
{
    private readonly Services.IDataConversionService _conversion;

    // ── File selection ───────────────────────────────────────────
    [ObservableProperty] private string _inputFilePath = string.Empty;
    [ObservableProperty] private string _outputFilePath = string.Empty;
    [ObservableProperty] private bool _inputIsTenBit = true;

    // ── File info ────────────────────────────────────────────────
    [ObservableProperty] private string _sampleCountText = "—";
    [ObservableProperty] private string _fileSizeText = "—";
    [ObservableProperty] private string _durationText = "—";
    [ObservableProperty] private string _formatText = "—";

    // ── Conversion options ───────────────────────────────────────
    [ObservableProperty] private bool _outputAsTenBit;
    [ObservableProperty] private bool _outputAsSixteenBit = true;
    [ObservableProperty] private TimeSpan _startTime;
    [ObservableProperty] private TimeSpan _endTime;

    // ── Progress ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isConverting;
    [ObservableProperty] private double _conversionProgress;
    [ObservableProperty] private string _conversionStatusText = string.Empty;

    // ── Verification ─────────────────────────────────────────────
    [ObservableProperty] private bool _isVerifying;
    [ObservableProperty] private double _verifyProgress;
    [ObservableProperty] private string _verifyStatusText = string.Empty;

    private CancellationTokenSource? _cts;

    public DataConversionViewModel()
    {
        _conversion = ServiceLocator.DataConversion;
    }

    /// <summary>
    /// Called when input file is selected (from file picker).
    /// </summary>
    public void LoadInputFile(string path, bool isTenBit)
    {
        InputFilePath = path;
        InputIsTenBit = isTenBit;

        try
        {
            long samples = _conversion.GetSampleCount(path, isTenBit);
            var duration = _conversion.GetDuration(path, isTenBit);
            var fileInfo = new FileInfo(path);

            SampleCountText = $"{samples:N0} samples";
            FileSizeText = fileInfo.Length switch
            {
                >= 1L << 30 => $"{fileInfo.Length / (double)(1L << 30):F2} GB",
                >= 1L << 20 => $"{fileInfo.Length / (double)(1L << 20):F2} MB",
                _ => $"{fileInfo.Length / (double)(1L << 10):F2} KB"
            };
            DurationText = duration.ToString(@"hh\:mm\:ss\.fff");
            FormatText = isTenBit ? "10-bit packed" : "16-bit signed";
            EndTime = duration;
        }
        catch
        {
            SampleCountText = "Error reading file";
        }
    }

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        if (string.IsNullOrEmpty(OutputFilePath)) return;

        IsConverting = true;
        ConversionProgress = 0;
        ConversionStatusText = "Converting...";
        _cts = new CancellationTokenSource();

        var progress = new Progress<double>(p =>
        {
            ConversionProgress = p;
            ConversionStatusText = $"Converting... {p * 100:F1}%";
        });

        try
        {
            await _conversion.ConvertAsync(
                InputFilePath, OutputFilePath,
                InputIsTenBit, OutputAsTenBit,
                StartTime > TimeSpan.Zero ? StartTime : null,
                EndTime > TimeSpan.Zero ? EndTime : null,
                progress, _cts.Token);

            ConversionStatusText = "Conversion complete!";
        }
        catch (OperationCanceledException)
        {
            ConversionStatusText = "Conversion cancelled.";
        }
        catch (Exception ex)
        {
            ConversionStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
        }
    }

    private bool CanConvert() => !string.IsNullOrEmpty(InputFilePath) && !IsConverting;

    [RelayCommand]
    private void CancelConversion() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyTestDataAsync()
    {
        IsVerifying = true;
        VerifyProgress = 0;
        VerifyStatusText = "Verifying test data...";
        _cts = new CancellationTokenSource();

        var progress = new Progress<double>(p =>
        {
            VerifyProgress = p;
            VerifyStatusText = $"Verifying... {p * 100:F1}%";
        });

        try
        {
            bool ok = await _conversion.VerifyTestDataAsync(
                InputFilePath, InputIsTenBit, progress, _cts.Token);

            VerifyStatusText = ok ? "✅ Test data verified successfully!" : "❌ Test data verification FAILED";
        }
        catch (OperationCanceledException)
        {
            VerifyStatusText = "Verification cancelled.";
        }
        catch (Exception ex)
        {
            VerifyStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsVerifying = false;
        }
    }

    private bool CanVerify() => !string.IsNullOrEmpty(InputFilePath) && !IsVerifying;
}

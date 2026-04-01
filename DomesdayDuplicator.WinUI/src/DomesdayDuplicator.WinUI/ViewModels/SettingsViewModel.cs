// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DomesdayDuplicator.WinUI.Helpers;
using DomesdayDuplicator.WinUI.Models;

namespace DomesdayDuplicator.WinUI.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Services.IConfigurationService _configService;

    public CaptureConfiguration Config => _configService.Configuration;

    // ── USB settings ─────────────────────────────────────────────
    [ObservableProperty] private string _vendorIdHex = string.Empty;
    [ObservableProperty] private string _productIdHex = string.Empty;

    // ── Buffer size options ──────────────────────────────────────
    public string[] BufferSizeOptions { get; } =
    [
        "64 MB", "128 MB", "256 MB", "512 MB", "1024 MB"
    ];

    [ObservableProperty] private int _selectedBufferSizeIndex = 2; // 256 MB default

    // ── Capture format options ───────────────────────────────────
    public string[] CaptureFormatOptions { get; } =
    [
        "16-bit Signed", "10-bit Packed", "10-bit Decimated (4:1)"
    ];

    [ObservableProperty] private int _selectedCaptureFormatIndex;

    // ── UI theme ─────────────────────────────────────────────────
    public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];
    [ObservableProperty] private int _selectedThemeIndex;

    // ── About info ───────────────────────────────────────────────
    public string AppVersion => "1.0.0";
    public string Copyright => "© Simon Inns 2018-2019 / Junliang Ren 2026";
    public string License => "GNU General Public License v3.0";

    public SettingsViewModel()
    {
        _configService = ServiceLocator.Configuration;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var c = Config;
        VendorIdHex = $"0x{c.UsbVendorId:X4}";
        ProductIdHex = $"0x{c.UsbProductId:X4}";

        SelectedBufferSizeIndex = c.DiskBufferQueueSize switch
        {
            64L * 1024 * 1024 => 0,
            128L * 1024 * 1024 => 1,
            256L * 1024 * 1024 => 2,
            512L * 1024 * 1024 => 3,
            1024L * 1024 * 1024 => 4,
            _ => 2
        };

        SelectedCaptureFormatIndex = (int)c.CaptureFormat;
    }

    [RelayCommand]
    private void Save()
    {
        var c = Config;

        // Parse hex VID/PID
        if (VendorIdHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(VendorIdHex[2..], System.Globalization.NumberStyles.HexNumber, null, out ushort vid))
            c.UsbVendorId = vid;

        if (ProductIdHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(ProductIdHex[2..], System.Globalization.NumberStyles.HexNumber, null, out ushort pid))
            c.UsbProductId = pid;

        c.DiskBufferQueueSize = SelectedBufferSizeIndex switch
        {
            0 => 64L * 1024 * 1024,
            1 => 128L * 1024 * 1024,
            2 => 256L * 1024 * 1024,
            3 => 512L * 1024 * 1024,
            4 => 1024L * 1024 * 1024,
            _ => 256L * 1024 * 1024
        };

        c.CaptureFormat = (CaptureFormat)SelectedCaptureFormatIndex;

        _configService.Save();
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _configService.RestoreDefaults();
        LoadFromConfig();
    }
}

// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using System.Text.Json;
using DomesdayDuplicator.WinUI.Models;

namespace DomesdayDuplicator.WinUI.Services;

/// <summary>
/// Service for loading and saving application configuration.
/// </summary>
public interface IConfigurationService
{
    CaptureConfiguration Configuration { get; }
    void Load();
    void Save();
    void RestoreDefaults();
}

public sealed class ConfigurationService : IConfigurationService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DomesdayDuplicator", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CaptureConfiguration Configuration { get; } = new();

    public void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json = File.ReadAllText(ConfigPath);
            var saved = JsonSerializer.Deserialize<SavedConfig>(json, JsonOptions);
            if (saved == null) return;

            Configuration.UsbVendorId = saved.UsbVendorId;
            Configuration.UsbProductId = saved.UsbProductId;
            Configuration.PreferredDevicePath = saved.PreferredDevicePath ?? string.Empty;
            Configuration.CaptureDirectory = saved.CaptureDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            Configuration.CaptureFormat = saved.CaptureFormat;
            Configuration.IsTestMode = saved.IsTestMode;
            Configuration.DiskBufferQueueSize = saved.DiskBufferQueueSize;
            Configuration.UseSmallUsbTransfers = saved.UseSmallUsbTransfers;
            Configuration.UseWinUsb = saved.UseWinUsb;
            Configuration.UseAsyncFileIo = saved.UseAsyncFileIo;
            Configuration.ShowAmplitudeLabel = saved.ShowAmplitudeLabel;
            Configuration.ShowAmplitudeChart = saved.ShowAmplitudeChart;
            Configuration.ShowAdvancedCaptureStats = saved.ShowAdvancedCaptureStats;
            Configuration.ResetNotesOnSideChange = saved.ResetNotesOnSideChange;
            Configuration.ResetMintMarksOnSideChange = saved.ResetMintMarksOnSideChange;
            Configuration.DiscTitle = saved.DiscTitle ?? string.Empty;
            Configuration.IsCav = saved.IsCav;
            Configuration.IsNtsc = saved.IsNtsc;
            Configuration.SideNumber = saved.SideNumber;
            Configuration.AudioType = saved.AudioType ?? string.Empty;
            Configuration.CaptureNotes = saved.CaptureNotes ?? string.Empty;
            Configuration.MintMarks = saved.MintMarks ?? string.Empty;
        }
        catch
        {
            // If config is corrupted, use defaults
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);

            var saved = new SavedConfig
            {
                UsbVendorId = Configuration.UsbVendorId,
                UsbProductId = Configuration.UsbProductId,
                PreferredDevicePath = Configuration.PreferredDevicePath,
                CaptureDirectory = Configuration.CaptureDirectory,
                CaptureFormat = Configuration.CaptureFormat,
                IsTestMode = Configuration.IsTestMode,
                DiskBufferQueueSize = Configuration.DiskBufferQueueSize,
                UseSmallUsbTransfers = Configuration.UseSmallUsbTransfers,
                UseWinUsb = Configuration.UseWinUsb,
                UseAsyncFileIo = Configuration.UseAsyncFileIo,
                ShowAmplitudeLabel = Configuration.ShowAmplitudeLabel,
                ShowAmplitudeChart = Configuration.ShowAmplitudeChart,
                ShowAdvancedCaptureStats = Configuration.ShowAdvancedCaptureStats,
                ResetNotesOnSideChange = Configuration.ResetNotesOnSideChange,
                ResetMintMarksOnSideChange = Configuration.ResetMintMarksOnSideChange,
                DiscTitle = Configuration.DiscTitle,
                IsCav = Configuration.IsCav,
                IsNtsc = Configuration.IsNtsc,
                SideNumber = Configuration.SideNumber,
                AudioType = Configuration.AudioType,
                CaptureNotes = Configuration.CaptureNotes,
                MintMarks = Configuration.MintMarks
            };

            var json = JsonSerializer.Serialize(saved, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public void RestoreDefaults()
    {
        var fresh = new CaptureConfiguration();
        Configuration.UsbVendorId = fresh.UsbVendorId;
        Configuration.UsbProductId = fresh.UsbProductId;
        Configuration.PreferredDevicePath = fresh.PreferredDevicePath;
        Configuration.CaptureDirectory = fresh.CaptureDirectory;
        Configuration.CaptureFormat = fresh.CaptureFormat;
        Configuration.IsTestMode = fresh.IsTestMode;
        Configuration.DiskBufferQueueSize = fresh.DiskBufferQueueSize;
        Configuration.UseSmallUsbTransfers = fresh.UseSmallUsbTransfers;
        Configuration.UseWinUsb = fresh.UseWinUsb;
        Configuration.UseAsyncFileIo = fresh.UseAsyncFileIo;
        Configuration.ShowAmplitudeLabel = fresh.ShowAmplitudeLabel;
        Configuration.ShowAmplitudeChart = fresh.ShowAmplitudeChart;
        Configuration.ShowAdvancedCaptureStats = fresh.ShowAdvancedCaptureStats;
        Configuration.ResetNotesOnSideChange = fresh.ResetNotesOnSideChange;
        Configuration.ResetMintMarksOnSideChange = fresh.ResetMintMarksOnSideChange;
        Configuration.DiscTitle = fresh.DiscTitle;
        Configuration.IsCav = fresh.IsCav;
        Configuration.IsNtsc = fresh.IsNtsc;
        Configuration.SideNumber = fresh.SideNumber;
        Configuration.AudioType = fresh.AudioType;
        Configuration.CaptureNotes = fresh.CaptureNotes;
        Configuration.MintMarks = fresh.MintMarks;
        Save();
    }

    private sealed class SavedConfig
    {
        public ushort UsbVendorId { get; set; }
        public ushort UsbProductId { get; set; }
        public string? PreferredDevicePath { get; set; }
        public string? CaptureDirectory { get; set; }
        public CaptureFormat CaptureFormat { get; set; }
        public bool IsTestMode { get; set; }
        public long DiskBufferQueueSize { get; set; }
        public bool UseSmallUsbTransfers { get; set; }
        public bool UseWinUsb { get; set; }
        public bool UseAsyncFileIo { get; set; }
        public bool ShowAmplitudeLabel { get; set; }
        public bool ShowAmplitudeChart { get; set; }
        public bool ShowAdvancedCaptureStats { get; set; }
        public bool ResetNotesOnSideChange { get; set; }
        public bool ResetMintMarksOnSideChange { get; set; }
        public string? DiscTitle { get; set; }
        public bool IsCav { get; set; } = true;
        public bool IsNtsc { get; set; } = true;
        public int SideNumber { get; set; } = 1;
        public string? AudioType { get; set; }
        public string? CaptureNotes { get; set; }
        public string? MintMarks { get; set; }
    }
}

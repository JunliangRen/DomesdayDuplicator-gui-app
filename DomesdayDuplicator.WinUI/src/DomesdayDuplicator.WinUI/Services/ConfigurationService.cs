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
            Configuration.DiskBufferQueueSize = saved.DiskBufferQueueSize;
            Configuration.UseSmallUsbTransfers = saved.UseSmallUsbTransfers;
            Configuration.UseWinUsb = saved.UseWinUsb;
            Configuration.UseAsyncFileIo = saved.UseAsyncFileIo;
            Configuration.ShowAmplitudeLabel = saved.ShowAmplitudeLabel;
            Configuration.ShowAmplitudeChart = saved.ShowAmplitudeChart;
            Configuration.ShowAdvancedCaptureStats = saved.ShowAdvancedCaptureStats;
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
                DiskBufferQueueSize = Configuration.DiskBufferQueueSize,
                UseSmallUsbTransfers = Configuration.UseSmallUsbTransfers,
                UseWinUsb = Configuration.UseWinUsb,
                UseAsyncFileIo = Configuration.UseAsyncFileIo,
                ShowAmplitudeLabel = Configuration.ShowAmplitudeLabel,
                ShowAmplitudeChart = Configuration.ShowAmplitudeChart,
                ShowAdvancedCaptureStats = Configuration.ShowAdvancedCaptureStats
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
        Configuration.DiskBufferQueueSize = fresh.DiskBufferQueueSize;
        Configuration.UseSmallUsbTransfers = fresh.UseSmallUsbTransfers;
        Configuration.UseWinUsb = fresh.UseWinUsb;
        Configuration.UseAsyncFileIo = fresh.UseAsyncFileIo;
        Configuration.ShowAmplitudeLabel = fresh.ShowAmplitudeLabel;
        Configuration.ShowAmplitudeChart = fresh.ShowAmplitudeChart;
        Configuration.ShowAdvancedCaptureStats = fresh.ShowAdvancedCaptureStats;
        Save();
    }

    private sealed class SavedConfig
    {
        public ushort UsbVendorId { get; set; }
        public ushort UsbProductId { get; set; }
        public string? PreferredDevicePath { get; set; }
        public string? CaptureDirectory { get; set; }
        public CaptureFormat CaptureFormat { get; set; }
        public long DiskBufferQueueSize { get; set; }
        public bool UseSmallUsbTransfers { get; set; }
        public bool UseWinUsb { get; set; }
        public bool UseAsyncFileIo { get; set; }
        public bool ShowAmplitudeLabel { get; set; }
        public bool ShowAmplitudeChart { get; set; }
        public bool ShowAdvancedCaptureStats { get; set; }
    }
}

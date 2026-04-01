// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using DomesdayDuplicator.WinUI.Services;

namespace DomesdayDuplicator.WinUI.Helpers;

/// <summary>
/// Simple service locator for dependency injection without a heavy DI container.
/// Initialized once in App.xaml.cs.
/// </summary>
public static class ServiceLocator
{
    private static IUsbCaptureService? _usbCapture;
    private static IDataConversionService? _dataConversion;
    private static IConfigurationService? _configuration;

    public static IUsbCaptureService UsbCapture =>
        _usbCapture ?? throw new InvalidOperationException("UsbCaptureService not registered");

    public static IDataConversionService DataConversion =>
        _dataConversion ?? throw new InvalidOperationException("DataConversionService not registered");

    public static IConfigurationService Configuration =>
        _configuration ?? throw new InvalidOperationException("ConfigurationService not registered");

    public static void Register(
        IUsbCaptureService usbCapture,
        IDataConversionService dataConversion,
        IConfigurationService configuration)
    {
        _usbCapture = usbCapture;
        _dataConversion = dataConversion;
        _configuration = configuration;
    }
}

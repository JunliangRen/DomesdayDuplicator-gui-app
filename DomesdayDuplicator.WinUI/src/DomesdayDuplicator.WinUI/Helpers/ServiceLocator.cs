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
    private static bool _isDisposed;

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
        _isDisposed = false;
        _usbCapture = usbCapture;
        _dataConversion = dataConversion;
        _configuration = configuration;
    }

    public static void DisposeRegisteredServices()
        => DisposeRegisteredServicesAsync().AsTask().GetAwaiter().GetResult();

    public static async ValueTask DisposeRegisteredServicesAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            _configuration?.Save();
        }
        catch
        {
        }

        try
        {
            if (_usbCapture is IAsyncDisposable asyncUsbCapture)
            {
                await asyncUsbCapture.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _usbCapture?.Dispose();
            }
        }
        catch
        {
        }

        try
        {
            if (_dataConversion is IAsyncDisposable asyncDataConversion)
            {
                await asyncDataConversion.DisposeAsync().ConfigureAwait(false);
            }
            else if (_dataConversion is IDisposable disposableDataConversion)
            {
                disposableDataConversion.Dispose();
            }
        }
        catch
        {
        }
        finally
        {
            _usbCapture = null;
            _dataConversion = null;
            _configuration = null;
        }
    }
}

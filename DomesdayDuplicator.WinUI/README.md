# Domesday Duplicator - WinUI 3 Edition

A modern Windows desktop application for the [Domesday Duplicator](https://github.com/simoninns/DomesdayDuplicator) LaserDisc RF capture device, built with WinUI 3 and .NET 10.

## 🎯 Key Features

- **Real-Time USB Capture** — High-speed RF data capture at 40 MHz sample rate
- **Zero-GC Hot Path** — All USB buffers use `NativeMemory` (unmanaged heap), ensuring the .NET garbage collector never pauses the capture pipeline
- **Three-Thread Architecture** — USB Transfer → Processing → Disk I/O pipeline (matching the original C++ design)
- **Modern WinUI 3 UI** — Mica backdrop, card-based layout, NavigationView, dark/light theme support
- **Data Conversion** — Convert between 10-bit packed and 16-bit signed formats
- **Test Data Verification** — Validate FPGA test pattern integrity

## 🏗️ Architecture

```
┌─────────────┐    Channel    ┌──────────────┐    Channel    ┌─────────────┐
│  USB Thread  │─────────────▶│  Processing   │─────────────▶│  Disk I/O   │
│  (WinUSB)   │  NativeBuffer │  (Validate +  │  NativeBuffer │  (WriteThru)│
│             │              │   Convert)    │              │             │
└─────────────┘              └──────────────┘              └─────────────┘
       │                            │                            │
       └──── All buffers from NativeBufferPool (unmanaged heap) ─┘
                    GC never touches these buffers
```

### Why NativeMemory?

The .NET GC can pause all managed threads for garbage collection. During a 40 MHz USB capture, even a few milliseconds of pause can cause the USB buffer to overflow and data loss. By using `System.Runtime.InteropServices.NativeMemory`:

1. **Buffer allocation** happens outside the managed heap
2. **Buffer pointers** are passed directly to WinUSB via P/Invoke
3. **Buffer lifecycle** is managed by `NativeBufferPool` (not GC)
4. **VirtualLock** pins buffers to physical RAM (no page faults)
5. **GCSettings.LatencyMode = SustainedLowLatency** minimizes Gen2 collections

## 📋 Requirements

- Windows 10 19041+ / Windows 11
- .NET 10 SDK
- Visual Studio 2022 17.14+ with:
  - ".NET desktop development" workload
  - "Windows App SDK" component
- Domesday Duplicator hardware (VID: 0x1D50, PID: 0x603B)
- WinUSB driver installed for the device

## 🔨 Build

```bash
cd DomesdayDuplicator.WinUI
dotnet restore src/DomesdayDuplicator.WinUI/DomesdayDuplicator.WinUI.csproj
dotnet build src/DomesdayDuplicator.WinUI/DomesdayDuplicator.WinUI.csproj -c Release
```

Or open `DomesdayDuplicator.WinUI.sln` in Visual Studio 2022.

## 📁 Project Structure

```
DomesdayDuplicator.WinUI/
├── src/DomesdayDuplicator.WinUI/
│   ├── Native/                    # Core performance layer
│   │   ├── WinUsbInterop.cs       #   WinUSB/CfgMgr32 P/Invoke declarations
│   │   ├── NativeBufferPool.cs    #   Unmanaged memory buffer pool
│   │   └── NativeCaptureEngine.cs #   Three-thread USB capture pipeline
│   ├── Models/                    # Data models
│   │   ├── Enums.cs               #   CaptureFormat, TransferResult, etc.
│   │   ├── CaptureConfiguration.cs#   Observable settings model
│   │   ├── CaptureStatistics.cs   #   Real-time capture metrics
│   │   └── DeviceInfo.cs          #   USB device information
│   ├── Services/                  # Business logic layer
│   │   ├── UsbCaptureService.cs   #   Device management + capture control
│   │   ├── DataConversionService.cs    # 10-bit ↔ 16-bit conversion
│   │   └── ConfigurationService.cs     # JSON settings persistence
│   ├── ViewModels/                # MVVM ViewModels
│   │   ├── DashboardViewModel.cs  #   Home page with device status
│   │   ├── CaptureViewModel.cs    #   Capture controls + live stats
│   │   ├── DataConversionViewModel.cs # File conversion UI logic
│   │   └── SettingsViewModel.cs   #   Application settings
│   ├── Views/                     # WinUI 3 XAML pages
│   │   ├── DashboardPage.xaml     #   Card-based dashboard
│   │   ├── CapturePage.xaml       #   Capture controls + metadata
│   │   ├── DataConversionPage.xaml#   Conversion + verification
│   │   └── SettingsPage.xaml      #   Configuration cards
│   ├── Styles/AppStyles.xaml      # Shared styles (CardStyle, etc.)
│   ├── Converters/ValueConverters.cs # XAML value converters
│   ├── Helpers/ServiceLocator.cs  # Simple DI container
│   ├── MainWindow.xaml            # NavigationView shell
│   └── App.xaml                   # Application entry + GC config
└── DomesdayDuplicator.WinUI.sln   # Solution file
```

## 📜 License

GNU General Public License v3.0 — see the original [DomesdayDuplicator](https://github.com/simoninns/DomesdayDuplicator) project.

## 🙏 Credits

- **Simon Inns** — Original DomesdayDuplicator hardware and software
- **Domesday86 Project** — LaserDisc preservation community

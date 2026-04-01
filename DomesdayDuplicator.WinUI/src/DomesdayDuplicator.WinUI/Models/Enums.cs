// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

namespace DomesdayDuplicator.WinUI.Models;

/// <summary>
/// Capture data format matching the FPGA output modes.
/// </summary>
public enum CaptureFormat
{
    /// <summary>10-bit unsigned packed to 16-bit signed (left-shifted by 6).</summary>
    Signed16Bit = 0,

    /// <summary>10-bit unsigned packed (4 samples → 5 bytes).</summary>
    Unsigned10Bit = 1,

    /// <summary>10-bit unsigned with 4:1 decimation.</summary>
    Unsigned10BitDecimated = 2
}

/// <summary>
/// Result of a USB capture transfer session.
/// </summary>
public enum TransferResult
{
    Running,
    Success,
    FileCreationError,
    BufferUnderflow,
    ConnectionFailure,
    UsbMemoryLimit,
    UsbTransferFailure,
    FileWriteError,
    SequenceMismatch,
    VerificationError,
    ProgramError,
    ForcedAbort
}

/// <summary>
/// Serial port baud rates for LaserDisc player communication.
/// </summary>
public enum SerialSpeed
{
    Bps1200 = 1200,
    Bps2400 = 2400,
    Bps4800 = 4800,
    Bps9600 = 9600,
    AutoDetect = 0
}

/// <summary>
/// LaserDisc player operational state.
/// </summary>
public enum PlayerState
{
    Disconnected,
    Stopped,
    Playing,
    Paused,
    Scanning,
    Searching,
    SpeedChanged,
    Unknown
}

/// <summary>
/// Disc type for automatic capture.
/// </summary>
public enum DiscType
{
    CAV,
    CLV
}

/// <summary>
/// Automatic capture mode.
/// </summary>
public enum AutoCaptureType
{
    WholeDisc,
    PartialDisc,
    LeadInToAddress
}

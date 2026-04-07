// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0
//
// WinUSB P/Invoke declarations for direct USB device communication.
// All buffers passed to WinUSB are allocated via NativeMemory to
// guarantee they are never moved or paused by the .NET GC.

using System.Runtime.InteropServices;

namespace DomesdayDuplicator.WinUI.Native;

/// <summary>
/// P/Invoke declarations for WinUSB and SetupAPI/CfgMgr32 on Windows.
/// </summary>
internal static partial class WinUsbInterop
{
    // ── WinUSB constants ────────────────────────────────────────────
    public const byte USB_ENDPOINT_DIRECTION_MASK = 0x80;
    public const int PIPE_TRANSFER_TIMEOUT = 0x03;
    public const int RAW_IO = 0x07;
    public const int MAXIMUM_TRANSFER_SIZE = 0x08;
    public const int DEVICE_SPEED = 0x01;

    public const int USB_DEVICE_DESCRIPTOR_TYPE = 0x01;
    public const int FILE_FLAG_OVERLAPPED = 0x40000000;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;

    public const int ERROR_IO_PENDING = 997;

    // ── GUID for WinUSB device interface ────────────────────────────
    // {DEE824EF-729B-4A0E-9C14-B7117D33A817} - standard WinUSB GUID
    public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE =
        new("DEE824EF-729B-4A0E-9C14-B7117D33A817");

    // ── Structures ──────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct USB_DEVICE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public ushort bcdUSB;
        public byte bDeviceClass;
        public byte bDeviceSubClass;
        public byte bDeviceProtocol;
        public byte bMaxPacketSize0;
        public ushort idVendor;
        public ushort idProduct;
        public ushort bcdDevice;
        public byte iManufacturer;
        public byte iProduct;
        public byte iSerialNumber;
        public byte bNumConfigurations;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WINUSB_SETUP_PACKET
    {
        public byte RequestType;
        public byte Request;
        public ushort Value;
        public ushort Index;
        public ushort Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeOverlapped
    {
        public nint Internal;
        public nint InternalHigh;
        public int OffsetLow;
        public int OffsetHigh;
        public nint EventHandle;
    }

    // ── WinUSB functions ────────────────────────────────────────────

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Initialize(
        nint DeviceHandle,
        out nint InterfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Free(nint InterfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_GetDescriptor(
        nint InterfaceHandle,
        byte DescriptorType,
        byte Index,
        ushort LanguageID,
        out USB_DEVICE_DESCRIPTOR Buffer,
        uint BufferLength,
        out uint LengthTransferred);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryInterfaceSettings(
        nint InterfaceHandle,
        byte AlternateInterfaceNumber,
        out USB_INTERFACE_DESCRIPTOR UsbAltInterfaceDescriptor);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryPipe(
        nint InterfaceHandle,
        byte AlternateInterfaceNumber,
        byte PipeIndex,
        out WINUSB_PIPE_INFORMATION PipeInformation);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_SetPipePolicy(
        nint InterfaceHandle,
        byte PipeID,
        uint PolicyType,
        uint ValueLength,
        ref int Value);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_GetPipePolicy(
        nint InterfaceHandle,
        byte PipeID,
        uint PolicyType,
        ref uint ValueLength,
        out uint Value);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ReadPipe(
        nint InterfaceHandle,
        byte PipeID,
        nint Buffer,
        uint BufferLength,
        out uint LengthTransferred,
        ref NativeOverlapped Overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ControlTransfer(
        nint InterfaceHandle,
        WINUSB_SETUP_PACKET SetupPacket,
        nint Buffer,
        uint BufferLength,
        out uint LengthTransferred,
        nint Overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryDeviceInformation(
        nint InterfaceHandle,
        uint InformationType,
        ref uint BufferLength,
        out uint Buffer);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_AbortPipe(
        nint InterfaceHandle,
        byte PipeID);

    // ── Kernel32 functions ──────────────────────────────────────────

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateEventW(
        nint lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        nint lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ResetEvent(nint hEvent);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForMultipleObjects(
        uint nCount,
        nint[] lpHandles,
        [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
        uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetOverlappedResult(
        nint hFile,
        ref NativeOverlapped lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualLock(nint lpAddress, nuint dwSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualUnlock(nint lpAddress, nuint dwSize);

    // ── CfgMgr32 for device enumeration ─────────────────────────────

    [LibraryImport("cfgmgr32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_List_SizeW(
        out uint pulLen,
        ref Guid InterfaceClassGuid,
        nint pDeviceID,
        uint ulFlags);

    [LibraryImport("cfgmgr32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_ListW(
        ref Guid InterfaceClassGuid,
        nint pDeviceID,
        [Out] char[] Buffer,
        uint BufferLen,
        uint ulFlags);

    public const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    public const uint CR_SUCCESS = 0;

    // ── Process priority ────────────────────────────────────────────

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPriorityClass(nint hProcess, uint dwPriorityClass);

    public const uint REALTIME_PRIORITY_CLASS = 0x00000100;
    public const uint HIGH_PRIORITY_CLASS = 0x00000080;
    public const uint NORMAL_PRIORITY_CLASS = 0x00000020;

    public static readonly nint INVALID_HANDLE_VALUE = new(-1);
}

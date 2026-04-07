// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0
//
// NativeCaptureEngine: The core high-performance USB capture pipeline.
// Runs entirely on native (unmanaged) memory to avoid GC pauses.
//
// Architecture (matches the original C++ three-thread design):
//   Thread 1 (USB):        Overlapped WinUSB reads → fill NativeBuffers
//   Thread 2 (Processing): Validate sequence numbers, convert format, compute stats
//   Thread 3 (Disk I/O):   Write processed buffers to file
//
// The hot path (Thread 1 → Channel → Thread 2 → Channel → Thread 3)
// allocates ZERO managed heap objects. All buffers come from NativeBufferPool.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DomesdayDuplicator.WinUI.Models;
using static DomesdayDuplicator.WinUI.Native.WinUsbInterop;

namespace DomesdayDuplicator.WinUI.Native;

/// <summary>
/// High-performance USB capture engine using NativeMemory and WinUSB.
/// </summary>
public sealed class NativeCaptureEngine : IDisposable, IAsyncDisposable
{
    // ── Constants matching original C++ firmware ─────────────────
    private const int SmallTransferSize = 128 * 1024;        // 128 KB
    private const int DefaultDiskBufferSize = 2 * 1024 * 1024; // 2 MB per disk buffer
    private const int SampleRate = 40_000_000;                // 40 MHz
    private const byte ConfigRequest = 0xB6;                  // Vendor-specific config command
    private const byte ConfigRequestType = 0x40;              // Vendor, host-to-device
    private const int SequenceCounterMask = 0xFC;             // Bits [7:2] of high byte
    private const int SequenceCounterShift = 2;
    private const int SequenceCounterMax = 63;                // 6-bit counter (0-63)
    private const int SampleDataMask = 0x03FF;                // 10-bit sample value

    // ── State ───────────────────────────────────────────────────
    private NativeBufferPool? _bufferPool;
    private string? _devicePath;
    private ushort _deviceVendorId;
    private ushort _deviceProductId;
    private nint _deviceHandle = INVALID_HANDLE_VALUE;
    private nint _winUsbHandle;
    private byte _bulkInPipeId;
    private uint _maxTransferSize;

    private CancellationTokenSource? _cts;
    private Task? _usbTask;
    private Task? _processingTask;
    private Task? _diskTask;

    private volatile bool _isCapturing;
    private volatile TransferResult _result = TransferResult.Success;

    // ── Statistics (updated atomically from processing thread) ───
    private long _totalTransfers;
    private long _diskBuffersWritten;
    private long _fileSizeBytes;
    private long _processedSamples;
    private int _minSample = int.MaxValue;
    private int _maxSample = int.MinValue;
    private long _clippedMinCount;
    private long _clippedMaxCount;
    private int _recentMinSample;
    private int _recentMaxSample;
    private long _recentClippedMin;
    private long _recentClippedMax;
    private double _rmsAmplitude;
    private bool _hasSequenceNumbers;

    // ── Channels (bounded, zero-alloc pass-through) ─────────────
    private Channel<NativeBuffer>? _usbToProcessing;
    private Channel<NativeBuffer>? _processingToDisk;

    // ── Public properties ───────────────────────────────────────
    public bool IsCapturing => _isCapturing;
    public TransferResult LastResult => _result;

    /// <summary>
    /// Populate a <see cref="CaptureStatistics"/> object with current values.
    /// Safe to call from the UI thread.
    /// </summary>
    public void ReadStatistics(CaptureStatistics stats)
    {
        stats.TotalTransfers = Interlocked.Read(ref _totalTransfers);
        stats.DiskBuffersWritten = Interlocked.Read(ref _diskBuffersWritten);
        stats.FileSizeBytes = Interlocked.Read(ref _fileSizeBytes);
        stats.ProcessedSamples = Interlocked.Read(ref _processedSamples);
        stats.MinSampleValue = Volatile.Read(ref _minSample) == int.MaxValue ? 0 : _minSample;
        stats.MaxSampleValue = Volatile.Read(ref _maxSample) == int.MinValue ? 0 : _maxSample;
        stats.ClippedMinCount = Interlocked.Read(ref _clippedMinCount);
        stats.ClippedMaxCount = Interlocked.Read(ref _clippedMaxCount);
        stats.RecentMinSampleValue = Volatile.Read(ref _recentMinSample);
        stats.RecentMaxSampleValue = Volatile.Read(ref _recentMaxSample);
        stats.RecentClippedMinCount = Interlocked.Read(ref _recentClippedMin);
        stats.RecentClippedMaxCount = Interlocked.Read(ref _recentClippedMax);
        stats.RmsAmplitude = Volatile.Read(ref _rmsAmplitude);
        stats.IsCapturing = _isCapturing;
        stats.LastResult = _result;
        stats.HasSequenceNumbers = _hasSequenceNumbers;
    }

    /// <summary>
    /// Enumerate connected Domesday Duplicator devices via CfgMgr32.
    /// Returns an empty list if the native call fails (e.g. DLL not found).
    /// </summary>
    public static List<string> EnumerateDevices()
    {
        var devices = new List<string>();

        try
        {
            var guid = GUID_DEVINTERFACE_USB_DEVICE;

            uint result = CM_Get_Device_Interface_List_SizeW(out uint len, ref guid, nint.Zero,
                CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (result != CR_SUCCESS || len == 0) return devices;

            var buffer = new char[len];
            result = CM_Get_Device_Interface_ListW(ref guid, nint.Zero, buffer, len,
                CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (result != CR_SUCCESS) return devices;

            // Parse multi-string: null-terminated strings with double-null terminator
            int start = 0;
            for (int i = 0; i < buffer.Length - 1; i++)
            {
                if (buffer[i] == '\0')
                {
                    if (i > start)
                    {
                        devices.Add(new string(buffer, start, i - start));
                    }
                    start = i + 1;
                    if (buffer[i + 1] == '\0') break;
                }
            }
        }
        catch (DllNotFoundException ex) { Debug.WriteLine($"[EnumerateDevices] DLL not found: {ex.Message}"); }
        catch (EntryPointNotFoundException ex) { Debug.WriteLine($"[EnumerateDevices] Entry point not found: {ex.Message}"); }
        catch (System.Runtime.InteropServices.SEHException ex) { Debug.WriteLine($"[EnumerateDevices] SEH exception: {ex.Message}"); }

        return devices;
    }

    private bool TrySendConfigurationCommandOnCurrentHandle(WINUSB_SETUP_PACKET setup)
    {
        if (_winUsbHandle == nint.Zero)
        {
            return false;
        }

        return WinUsb_ControlTransfer(_winUsbHandle, setup, nint.Zero, 0, out _, nint.Zero);
    }

    /// <summary>
    /// Open a WinUSB device and validate it matches the expected VID/PID.
    /// Returns null if the device cannot be opened or any native call fails.
    /// </summary>
    public DeviceInfo? OpenDevice(string devicePath, ushort expectedVid, ushort expectedPid)
    {
        try
        {
            return OpenDeviceCore(devicePath, expectedVid, expectedPid);
        }
        catch (DllNotFoundException ex) { Debug.WriteLine($"[OpenDevice] DLL not found: {ex.Message}"); return null; }
        catch (EntryPointNotFoundException ex) { Debug.WriteLine($"[OpenDevice] Entry point not found: {ex.Message}"); return null; }
        catch (System.Runtime.InteropServices.SEHException ex) { Debug.WriteLine($"[OpenDevice] SEH exception: {ex.Message}"); return null; }
        catch (AccessViolationException ex) { Debug.WriteLine($"[OpenDevice] Access violation: {ex.Message}"); return null; }
    }

    private DeviceInfo? OpenDeviceCore(string devicePath, ushort expectedVid, ushort expectedPid)
    {
        CloseDevice();

        _deviceHandle = CreateFileW(
            devicePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nint.Zero,
            OPEN_EXISTING,
            (uint)FILE_FLAG_OVERLAPPED,
            nint.Zero);

        if (_deviceHandle == INVALID_HANDLE_VALUE) return null;

        if (!WinUsb_Initialize(_deviceHandle, out _winUsbHandle))
        {
            CloseHandle(_deviceHandle);
            _deviceHandle = INVALID_HANDLE_VALUE;
            return null;
        }

        // Read device descriptor for VID/PID validation
        if (!WinUsb_GetDescriptor(_winUsbHandle, USB_DEVICE_DESCRIPTOR_TYPE, 0, 0,
            out var desc, (uint)Marshal.SizeOf<USB_DEVICE_DESCRIPTOR>(), out _))
        {
            CloseDevice();
            return null;
        }

        if (desc.idVendor != expectedVid || desc.idProduct != expectedPid)
        {
            CloseDevice();
            return null;
        }

        // Find bulk-in endpoint
        if (!WinUsb_QueryInterfaceSettings(_winUsbHandle, 0, out var ifDesc))
        {
            CloseDevice();
            return null;
        }

        byte pipeId = 0;
        for (byte i = 0; i < ifDesc.bNumEndpoints; i++)
        {
            if (!WinUsb_QueryPipe(_winUsbHandle, 0, i, out var pipeInfo)) continue;
            if ((pipeInfo.PipeId & USB_ENDPOINT_DIRECTION_MASK) != 0 && pipeInfo.PipeType == 2) // Bulk IN
            {
                pipeId = pipeInfo.PipeId;
                break;
            }
        }

        if (pipeId == 0)
        {
            CloseDevice();
            return null;
        }

        _bulkInPipeId = pipeId;

        // Query max transfer size
        uint len = 4;
        WinUsb_GetPipePolicy(_winUsbHandle, _bulkInPipeId, MAXIMUM_TRANSFER_SIZE, ref len, out _maxTransferSize);
        if (_maxTransferSize == 0) _maxTransferSize = 2 * 1024 * 1024;

        // Enable RAW_IO for real-time performance
        int rawIoValue = 1;
        WinUsb_SetPipePolicy(_winUsbHandle, _bulkInPipeId, RAW_IO, 4, ref rawIoValue);

        // Query device speed
        uint speedLen = 4;
        WinUsb_QueryDeviceInformation(_winUsbHandle, DEVICE_SPEED, ref speedLen, out uint speed);

        _devicePath = devicePath;
        _deviceVendorId = desc.idVendor;
        _deviceProductId = desc.idProduct;

        return new DeviceInfo(
            devicePath, desc.idVendor, desc.idProduct,
            $"Domesday Duplicator ({desc.idVendor:X4}:{desc.idProduct:X4})",
            speed, pipeId, _maxTransferSize);
    }

    /// <summary>
    /// Send a configuration command to the FPGA (test mode toggle).
    /// </summary>
    public bool SendConfigurationCommand(bool testMode)
    {
        var setup = new WINUSB_SETUP_PACKET
        {
            RequestType = ConfigRequestType,
            Request = ConfigRequest,
            Value = (ushort)(testMode ? 1 : 0),
            Index = 0,
            Length = 0
        };

        bool temporaryHandleSucceeded = TrySendConfigurationCommandOnTemporaryHandle(_devicePath, setup);
        bool currentHandleSucceeded = false;

        if (!temporaryHandleSucceeded && _winUsbHandle != nint.Zero)
        {
            currentHandleSucceeded = WinUsb_ControlTransfer(_winUsbHandle, setup, nint.Zero, 0, out _, nint.Zero);
        }

        Debug.WriteLine($"[SendConfigurationCommand] testMode={testMode}, temporaryHandleSucceeded={temporaryHandleSucceeded}, currentHandleSucceeded={currentHandleSucceeded}, devicePath={_devicePath}");
        return temporaryHandleSucceeded || currentHandleSucceeded;
    }

    private bool TrySendConfigurationCommandOnTemporaryHandle(string? devicePath, WINUSB_SETUP_PACKET setup)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return false;

        nint deviceHandle = INVALID_HANDLE_VALUE;
        nint winUsbHandle = nint.Zero;

        try
        {
            deviceHandle = CreateFileW(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                nint.Zero,
                OPEN_EXISTING,
                (uint)FILE_FLAG_OVERLAPPED,
                nint.Zero);

            if (deviceHandle == INVALID_HANDLE_VALUE)
            {
                return false;
            }

            if (!WinUsb_Initialize(deviceHandle, out winUsbHandle))
            {
                return false;
            }

            return WinUsb_ControlTransfer(winUsbHandle, setup, nint.Zero, 0, out _, nint.Zero);
        }
        finally
        {
            if (winUsbHandle != nint.Zero)
            {
                WinUsb_Free(winUsbHandle);
            }

            if (deviceHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(deviceHandle);
            }
        }
    }

    /// <summary>
    /// Start a capture session. Allocates native buffers and launches the
    /// three-thread pipeline.
    /// </summary>
    public bool StartCapture(
        string outputFilePath,
        CaptureFormat format,
        bool testMode,
        bool useSmallTransfers,
        long diskBufferQueueSize)
    {
        if (_isCapturing || _winUsbHandle == nint.Zero) return false;

        string? devicePath = _devicePath;
        ushort deviceVendorId = _deviceVendorId;
        ushort deviceProductId = _deviceProductId;

        CloseDevice();

        var setup = new WINUSB_SETUP_PACKET
        {
            RequestType = ConfigRequestType,
            Request = ConfigRequest,
            Value = (ushort)(testMode ? 1 : 0),
            Index = 0,
            Length = 0
        };

        bool temporaryHandleSucceeded = TrySendConfigurationCommandOnTemporaryHandle(devicePath, setup);
        Debug.WriteLine($"[StartCapture] Pre-connect configuration testMode={testMode}, temporaryHandleSucceeded={temporaryHandleSucceeded}, devicePath={devicePath}");
        if (!temporaryHandleSucceeded)
        {
            Debug.WriteLine($"[StartCapture] Failed to apply test mode setting: {testMode}");
        }

        if (string.IsNullOrWhiteSpace(devicePath) || OpenDeviceCore(devicePath, deviceVendorId, deviceProductId) == null)
        {
            _result = TransferResult.ConnectionFailure;
            return false;
        }

        bool currentHandleSucceeded = TrySendConfigurationCommandOnCurrentHandle(setup);
        Debug.WriteLine($"[StartCapture] Post-connect configuration testMode={testMode}, currentHandleSucceeded={currentHandleSucceeded}, devicePath={_devicePath}");
        if (!currentHandleSucceeded)
        {
            Debug.WriteLine($"[StartCapture] Failed to re-apply test mode on capture handle: {testMode}");
        }

        Thread.Sleep(50);

        // Reset statistics
        _totalTransfers = 0;
        _diskBuffersWritten = 0;
        _fileSizeBytes = 0;
        _processedSamples = 0;
        _minSample = int.MaxValue;
        _maxSample = int.MinValue;
        _clippedMinCount = 0;
        _clippedMaxCount = 0;
        _result = TransferResult.Running;
        _hasSequenceNumbers = false;

        // Determine transfer size
        bool useLargeTransfersForThisCapture = testMode || !useSmallTransfers;
        int transferSize = useLargeTransfersForThisCapture
            ? (int)Math.Min(_maxTransferSize, DefaultDiskBufferSize)
            : SmallTransferSize;

        Debug.WriteLine($"[StartCapture] transferSize={transferSize}, requestedSmallTransfers={useSmallTransfers}, effectiveLargeTransfers={useLargeTransfersForThisCapture}");

        // Allocate native buffer pool
        _bufferPool?.Dispose();
        _bufferPool = new NativeBufferPool(diskBufferQueueSize, transferSize, lockToRam: true);

        // Create bounded channels for the pipeline
        var channelOpts = new BoundedChannelOptions(_bufferPool.TotalCount)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        };
        _usbToProcessing = Channel.CreateBounded<NativeBuffer>(channelOpts);
        _processingToDisk = Channel.CreateBounded<NativeBuffer>(channelOpts);

        // Boost process priority
        SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);

        _cts = new CancellationTokenSource();
        _isCapturing = true;

        var token = _cts.Token;
        var winUsbHandle = _winUsbHandle;
        var pipeId = _bulkInPipeId;

        // Launch three pipeline threads
        _usbTask = Task.Factory.StartNew(
            () => UsbTransferLoop(winUsbHandle, pipeId, transferSize, token),
            token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        _processingTask = Task.Factory.StartNew(
            () => ProcessingLoop(format, testMode, token),
            token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        _diskTask = Task.Factory.StartNew(
            () => DiskWriteLoop(outputFilePath, token),
            token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        return true;
    }

    /// <summary>
    /// Stop the current capture session.
    /// </summary>
    public async Task StopCaptureAsync()
    {
        if (!_isCapturing) return;

        _cts?.Cancel();

        // Abort pending USB transfers
        if (_winUsbHandle != nint.Zero && _bulkInPipeId != 0)
        {
            WinUsb_AbortPipe(_winUsbHandle, _bulkInPipeId);
        }

        // Wait for all pipeline threads to finish
        try
        {
            if (_usbTask != null) await _usbTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        try
        {
            if (_processingTask != null) await _processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        try
        {
            if (_diskTask != null) await _diskTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        // Restore normal priority
        SetPriorityClass(GetCurrentProcess(), NORMAL_PRIORITY_CLASS);

        _isCapturing = false;
        if (_result == TransferResult.Running) _result = TransferResult.Success;
    }

    private sealed class UsbTransferSlot
    {
        public nint EventHandle;
        public NativeBuffer? Buffer;
        public WinUsbInterop.NativeOverlapped Overlapped;
        public bool Submitted;
        public bool Completed;
        public uint CompletedBytes;
        public long SequenceId;
    }

    private bool SubmitUsbRead(UsbTransferSlot slot, nint winUsbHandle, byte pipeId, long sequenceId)
    {
        if (slot.Buffer == null)
        {
            return false;
        }

        ResetEvent(slot.EventHandle);
        slot.Overlapped = new WinUsbInterop.NativeOverlapped { EventHandle = slot.EventHandle };
        slot.SequenceId = sequenceId;
        slot.Completed = false;
        slot.CompletedBytes = 0;

        bool success = WinUsb_ReadPipe(
            winUsbHandle,
            pipeId,
            slot.Buffer.Pointer,
            (uint)slot.Buffer.Size,
            out _,
            ref slot.Overlapped);

        if (success)
        {
            slot.Submitted = true;
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == ERROR_IO_PENDING)
        {
            slot.Submitted = true;
            return true;
        }

        slot.Submitted = false;
        return false;
    }

    // ═════════════════════════════════════════════════════════════
    // Thread 1: USB Transfer Loop
    // Reads raw data from the device into NativeBuffers.
    // ZERO managed allocations in the hot path.
    // ═════════════════════════════════════════════════════════════
    private void UsbTransferLoop(nint winUsbHandle, byte pipeId, int transferSize, CancellationToken ct)
    {
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        const int SimultaneousTransfers = 4;
        const int InitialTransfersToSkip = 4;
        var slots = new UsbTransferSlot[SimultaneousTransfers];

        try
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var buffer = _bufferPool!.Rent();
                if (buffer == null)
                {
                    _result = TransferResult.BufferUnderflow;
                    return;
                }

                var hEvent = CreateEventW(nint.Zero, true, false, nint.Zero);
                if (hEvent == nint.Zero)
                {
                    _bufferPool.Return(buffer);
                    _result = TransferResult.UsbTransferFailure;
                    return;
                }

                var slot = new UsbTransferSlot
                {
                    EventHandle = hEvent,
                    Buffer = buffer
                };

                if (!SubmitUsbRead(slot, winUsbHandle, pipeId, i))
                {
                    _bufferPool.Return(buffer);
                    CloseHandle(hEvent);
                    _result = TransferResult.ConnectionFailure;
                    return;
                }

                slots[i] = slot;
            }

            long nextCompletionSequence = 0;
            long nextSubmissionSequence = slots.Length;
            int skippedInitialTransfers = 0;

            while (!ct.IsCancellationRequested)
            {
                var activeSlots = new List<int>(slots.Length);
                var handles = new List<nint>(slots.Length);

                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].Submitted)
                    {
                        activeSlots.Add(i);
                        handles.Add(slots[i].EventHandle);
                    }
                }

                if (handles.Count == 0)
                {
                    break;
                }

                uint waitResult = WaitForMultipleObjects((uint)handles.Count, handles.ToArray(), false, 5000);
                if (waitResult >= handles.Count)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    _result = TransferResult.UsbTransferFailure;
                    break;
                }

                int slotIndex = activeSlots[(int)waitResult];
                var slot = slots[slotIndex];

                if (!GetOverlappedResult(_deviceHandle, ref slot.Overlapped, out uint bytesRead, false))
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    _result = TransferResult.UsbTransferFailure;
                    break;
                }

                slot.Submitted = false;
                slot.Completed = true;
                slot.CompletedBytes = bytesRead;
                slots[slotIndex] = slot;

                while (true)
                {
                    UsbTransferSlot? readySlot = null;
                    int readyIndex = -1;

                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i].Completed && slots[i].SequenceId == nextCompletionSequence)
                        {
                            readySlot = slots[i];
                            readyIndex = i;
                            break;
                        }
                    }

                    if (readySlot == null)
                    {
                        break;
                    }

                    Interlocked.Increment(ref _totalTransfers);

                    var completedBuffer = readySlot.Buffer;
                    readySlot.Buffer = null;
                    readySlot.Completed = false;

                    if (completedBuffer != null)
                    {
                        int validBytes = (int)(readySlot.CompletedBytes & ~1U);
                        if (validBytes > 0)
                        {
                            completedBuffer.Length = validBytes;

                            if (skippedInitialTransfers < InitialTransfersToSkip)
                            {
                                skippedInitialTransfers++;
                                Debug.WriteLine($"[UsbTransferLoop] Skipping warm-up transfer {skippedInitialTransfers}/{InitialTransfersToSkip}, bytes={validBytes}, sequenceId={readySlot.SequenceId}");
                                _bufferPool!.Return(completedBuffer);
                            }
                            else if (!_usbToProcessing!.Writer.TryWrite(completedBuffer))
                            {
                                _usbToProcessing.Writer.WriteAsync(completedBuffer, ct).AsTask().Wait(ct);
                            }
                        }
                        else
                        {
                            _bufferPool!.Return(completedBuffer);
                        }
                    }

                    var nextBuffer = _bufferPool!.Rent();
                    if (nextBuffer == null)
                    {
                        _result = TransferResult.BufferUnderflow;
                        break;
                    }

                    readySlot.Buffer = nextBuffer;
                    if (!SubmitUsbRead(readySlot, winUsbHandle, pipeId, nextSubmissionSequence++))
                    {
                        _bufferPool.Return(nextBuffer);
                        readySlot.Buffer = null;
                        _result = TransferResult.ConnectionFailure;
                        break;
                    }

                    slots[readyIndex] = readySlot;
                    nextCompletionSequence++;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _result = TransferResult.UsbTransferFailure;
        }
        finally
        {
            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    continue;
                }

                if (slot.Buffer != null)
                {
                    try
                    {
                        _bufferPool?.Return(slot.Buffer);
                    }
                    catch
                    {
                    }
                }

                if (slot.EventHandle != nint.Zero)
                {
                    CloseHandle(slot.EventHandle);
                }
            }
        }

        _usbToProcessing?.Writer.TryComplete();
    }

    // ═════════════════════════════════════════════════════════════
    // Thread 2: Processing Loop
    // Validates sequence numbers, converts data, computes statistics.
    // Operates directly on NativeBuffer via Span<byte> — no copies.
    // ═════════════════════════════════════════════════════════════
    private void ProcessingLoop(CaptureFormat format, bool testMode, CancellationToken ct)
    {
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

        int expectedSequence = -1;
        int? expectedNextTestDataValue = null;
        int? testDataMax = null;
        bool loggedInitialTestModeSamples = false;
        bool loggedTestModeMismatch = false;
        long testModeSampleIndex = 0;

        try
        {
            while (_usbToProcessing!.Reader.WaitToReadAsync(ct).AsTask().Result)
            {
                while (_usbToProcessing.Reader.TryRead(out var buffer))
                {
                    ct.ThrowIfCancellationRequested();

                    var span = buffer.AsSpan(buffer.Length);
                    int sampleCount = span.Length / 2;
                    // Per-buffer statistics
                    int localMin = int.MaxValue;
                    int localMax = int.MinValue;
                    long localClipMin = 0;
                    long localClipMax = 0;
                    double sumSquares = 0;
                    List<int>? initialSamples = testMode && !loggedInitialTestModeSamples ? [] : null;

                    for (int i = 0; i < span.Length - 1; i += 2)
                    {
                        int lo = span[i];
                        int hi = span[i + 1];
                        int raw = lo | (hi << 8);

                        // Extract 6-bit sequence number from bits [15:10]
                        int seq = (hi >> 2) & 0x3F;

                        // Strip sequence number, keep 10-bit sample
                        int sample10 = raw & SampleDataMask;

                        if (initialSamples != null && initialSamples.Count < 16)
                        {
                            initialSamples.Add(sample10);
                        }

                        if (i == 0 && expectedSequence < 0)
                        {
                            expectedSequence = seq;
                        }

                        // Validate sequence (checked every Nth sample in original code)
                        if (i % (65536 * 2) == 0)
                        {
                            if (expectedSequence >= 0)
                            {
                                int nextExpectedSequence = (expectedSequence + 1) & SequenceCounterMax;

                                if (_hasSequenceNumbers)
                                {
                                    if (seq != nextExpectedSequence)
                                    {
                                        _result = TransferResult.SequenceMismatch;
                                    }
                                }
                                else if (seq == nextExpectedSequence)
                                {
                                    _hasSequenceNumbers = true;
                                }
                            }

                            expectedSequence = seq;
                        }

                        // Test mode verification
                        if (testMode)
                        {
                            int expectedValue = expectedNextTestDataValue ?? sample10;

                            if (!testDataMax.HasValue &&
                                expectedValue != sample10 &&
                                sample10 == 0 &&
                                (expectedValue == 1021 || expectedValue == 1024))
                            {
                                testDataMax = expectedValue;
                                expectedNextTestDataValue = 1;
                                continue;
                            }

                            if (expectedValue != sample10)
                            {
                                if (!loggedTestModeMismatch)
                                {
                                    Debug.WriteLine($"[ProcessingLoop] Test-mode mismatch at sample {testModeSampleIndex}: expected {expectedValue}, actual {sample10}, testDataMax={(testDataMax.HasValue ? testDataMax.Value : -1)}");
                                    loggedTestModeMismatch = true;
                                }

                                _result = TransferResult.VerificationError;
                            }

                            expectedValue++;
                            if (testDataMax.HasValue && expectedValue == testDataMax.Value)
                            {
                                expectedValue = 0;
                            }

                            expectedNextTestDataValue = expectedValue;
                            testModeSampleIndex++;
                        }

                        // Statistics
                        if (sample10 < localMin) localMin = sample10;
                        if (sample10 > localMax) localMax = sample10;
                        if (sample10 <= 1) localClipMin++;
                        if (sample10 >= 1022) localClipMax++;

                        double centered = sample10 - 512.0;
                        sumSquares += centered * centered;

                        // Convert in-place based on format
                        switch (format)
                        {
                            case CaptureFormat.Signed16Bit:
                                short signed16 = (short)((sample10 - 0x200) << 6);
                                span[i] = (byte)(signed16 & 0xFF);
                                span[i + 1] = (byte)((signed16 >> 8) & 0xFF);
                                break;

                            case CaptureFormat.Unsigned10Bit:
                            span[i] = (byte)(sample10 & 0xFF);
                            span[i + 1] = (byte)((sample10 >> 8) & 0xFF);
                            break;

                        case CaptureFormat.Unsigned10BitDecimated:
                            span[i] = (byte)(sample10 & 0xFF);
                            span[i + 1] = (byte)((sample10 >> 8) & 0xFF);
                                break;
                        }
                    }

                    int outputLength = format switch
                    {
                        CaptureFormat.Unsigned10Bit => PackUnsigned10BitInPlace(span),
                        CaptureFormat.Unsigned10BitDecimated => PackUnsigned10Bit4To1DecimationInPlace(span),
                        _ => span.Length
                    };
                    buffer.Length = outputLength;

                    if (initialSamples != null)
                    {
                        Debug.WriteLine($"[ProcessingLoop] Initial test-mode samples: {string.Join(',', initialSamples)}");
                        loggedInitialTestModeSamples = true;
                    }

                    // Update global statistics atomically
                    UpdateMinAtomic(ref _minSample, localMin);
                    UpdateMaxAtomic(ref _maxSample, localMax);
                    Interlocked.Add(ref _clippedMinCount, localClipMin);
                    Interlocked.Add(ref _clippedMaxCount, localClipMax);
                    Volatile.Write(ref _recentMinSample, localMin);
                    Volatile.Write(ref _recentMaxSample, localMax);
                    Interlocked.Exchange(ref _recentClippedMin, localClipMin);
                    Interlocked.Exchange(ref _recentClippedMax, localClipMax);
                    Interlocked.Add(ref _processedSamples, sampleCount);

                    double rms = Math.Sqrt(sumSquares / (512.0 * 512.0 * sampleCount / 2.0));
                    Volatile.Write(ref _rmsAmplitude, rms);

                    // Pass to disk writer
                    if (!_processingToDisk!.Writer.TryWrite(buffer))
                    {
                        _processingToDisk.Writer.WriteAsync(buffer, ct).AsTask().Wait(ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { }

        _processingToDisk?.Writer.TryComplete();
    }

    // ═════════════════════════════════════════════════════════════
    // Thread 3: Disk Write Loop
    // Writes processed buffers to the output file.
    // ═════════════════════════════════════════════════════════════
    private void DiskWriteLoop(string outputFilePath, CancellationToken ct)
    {
        Thread.CurrentThread.Priority = ThreadPriority.Normal;
        FileStream? fs = null;

        try
        {
            fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 4 * 1024 * 1024,
                FileOptions.SequentialScan | FileOptions.WriteThrough);

            while (_processingToDisk!.Reader.WaitToReadAsync(ct).AsTask().Result)
            {
                while (_processingToDisk.Reader.TryRead(out var buffer))
                {
                    ct.ThrowIfCancellationRequested();

                    var data = buffer.AsReadOnlySpan(buffer.Length);
                    fs.Write(data);

                    Interlocked.Add(ref _fileSizeBytes, data.Length);
                    Interlocked.Increment(ref _diskBuffersWritten);

                    // Return buffer to pool for reuse
                    _bufferPool!.Return(buffer);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { }
        catch (IOException)
        {
            _result = TransferResult.FileWriteError;
        }
        finally
        {
            fs?.Flush();
            fs?.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static void UpdateMinAtomic(ref int target, int value)
    {
        int current;
        do { current = Volatile.Read(ref target); }
        while (value < current && Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static void UpdateMaxAtomic(ref int target, int value)
    {
        int current;
        do { current = Volatile.Read(ref target); }
        while (value > current && Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static int PackUnsigned10BitInPlace(Span<byte> span)
    {
        int groupCount = span.Length / 8;
        var packed = new byte[groupCount * 5];
        int writeIndex = 0;

        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            int readIndex = groupIndex * 8;
            ushort word0 = (ushort)(span[readIndex] | (span[readIndex + 1] << 8));
            ushort word1 = (ushort)(span[readIndex + 2] | (span[readIndex + 3] << 8));
            ushort word2 = (ushort)(span[readIndex + 4] | (span[readIndex + 5] << 8));
            ushort word3 = (ushort)(span[readIndex + 6] | (span[readIndex + 7] << 8));

            packed[writeIndex++] = (byte)((word0 & 0x03FC) >> 2);
            packed[writeIndex++] = (byte)(((word0 & 0x0003) << 6) | ((word1 & 0x03F0) >> 4));
            packed[writeIndex++] = (byte)(((word1 & 0x000F) << 4) | ((word2 & 0x03C0) >> 6));
            packed[writeIndex++] = (byte)(((word2 & 0x003F) << 2) | ((word3 & 0x0300) >> 8));
            packed[writeIndex++] = (byte)(word3 & 0x00FF);
        }

        packed.CopyTo(span);
        return packed.Length;
    }

    private static int PackUnsigned10Bit4To1DecimationInPlace(Span<byte> span)
    {
        int groupCount = span.Length / 32;
        var packed = new byte[groupCount * 5];
        int writeIndex = 0;

        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            int readIndex = groupIndex * 32;
            ushort word0 = (ushort)(span[readIndex] | (span[readIndex + 1] << 8));
            ushort word1 = (ushort)(span[readIndex + 6] | (span[readIndex + 7] << 8));
            ushort word2 = (ushort)(span[readIndex + 12] | (span[readIndex + 13] << 8));
            ushort word3 = (ushort)(span[readIndex + 18] | (span[readIndex + 19] << 8));

            packed[writeIndex++] = (byte)((word0 & 0x03FC) >> 2);
            packed[writeIndex++] = (byte)(((word0 & 0x0003) << 6) | ((word1 & 0x03F0) >> 4));
            packed[writeIndex++] = (byte)(((word1 & 0x000F) << 4) | ((word2 & 0x03C0) >> 6));
            packed[writeIndex++] = (byte)(((word2 & 0x003F) << 2) | ((word3 & 0x0300) >> 8));
            packed[writeIndex++] = (byte)(word3 & 0x00FF);
        }

        packed.CopyTo(span);
        return packed.Length;
    }

    public void CloseDevice()
    {
        try
        {
            if (_winUsbHandle != nint.Zero)
            {
                WinUsb_Free(_winUsbHandle);
                _winUsbHandle = nint.Zero;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CloseDevice] WinUsb_Free failed: {ex.Message}");
            _winUsbHandle = nint.Zero;
        }

        try
        {
            if (_deviceHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_deviceHandle);
                _deviceHandle = INVALID_HANDLE_VALUE;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CloseDevice] CloseHandle failed: {ex.Message}");
            _deviceHandle = INVALID_HANDLE_VALUE;
        }

        _devicePath = null;
        _deviceVendorId = 0;
        _deviceProductId = 0;
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_isCapturing)
        {
            _cts?.Cancel();
            try
            {
                await StopCaptureAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }

        _cts?.Dispose();
        _cts = null;
        _bufferPool?.Dispose();
        _bufferPool = null;
        CloseDevice();
    }
}

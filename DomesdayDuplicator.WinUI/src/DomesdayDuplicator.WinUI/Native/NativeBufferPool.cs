// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0
//
// NativeBufferPool: A pool of unmanaged memory buffers for USB capture.
// These buffers live outside the .NET managed heap, so the GC never
// touches, moves, or pauses them. They are pinned to physical RAM
// via VirtualLock to prevent page faults during real-time capture.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DomesdayDuplicator.WinUI.Native;

/// <summary>
/// A single unmanaged memory buffer with its lifecycle metadata.
/// </summary>
public sealed class NativeBuffer : IDisposable
{
    private nint _pointer;
    private readonly nuint _size;
    private bool _disposed;
    private bool _locked;

    /// <summary>Pointer to the unmanaged buffer. Never moved by GC.</summary>
    public nint Pointer => _disposed ? throw new ObjectDisposedException(nameof(NativeBuffer)) : _pointer;

    /// <summary>Size of the buffer in bytes.</summary>
    public int Size => (int)_size;

    public NativeBuffer(int sizeInBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);
        _size = (nuint)sizeInBytes;
        unsafe
        {
            _pointer = (nint)NativeMemory.AllocZeroed(_size);
        }
    }

    /// <summary>
    /// Lock the buffer pages into physical RAM to prevent page faults.
    /// Critical for real-time USB capture — a page fault during DMA
    /// would cause data loss.
    /// </summary>
    public bool LockToPhysicalMemory()
    {
        if (_locked || _disposed) return _locked;
        _locked = WinUsbInterop.VirtualLock(_pointer, _size);
        return _locked;
    }

    /// <summary>
    /// Get a Span&lt;byte&gt; view of the native buffer (zero-copy).
    /// </summary>
    public unsafe Span<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Span<byte>((void*)_pointer, (int)_size);
    }

    /// <summary>
    /// Get a ReadOnlySpan&lt;byte&gt; view of the native buffer (zero-copy).
    /// </summary>
    public unsafe ReadOnlySpan<byte> AsReadOnlySpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ReadOnlySpan<byte>((void*)_pointer, (int)_size);
    }

    /// <summary>
    /// Zero out the buffer contents.
    /// </summary>
    public unsafe void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMemory.Clear((void*)_pointer, _size);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_locked)
        {
            WinUsbInterop.VirtualUnlock(_pointer, _size);
            _locked = false;
        }

        unsafe
        {
            NativeMemory.Free((void*)_pointer);
        }
        _pointer = nint.Zero;
    }
}

/// <summary>
/// Thread-safe pool of NativeBuffer instances for USB capture.
/// Pre-allocates all buffers at startup and uses a lock-free
/// concurrent queue to distribute them to producer/consumer threads.
///
/// Architecture:
///   USB Thread → takes buffer from pool → fills via WinUsb_ReadPipe
///   → enqueues to processing queue → Processing Thread dequeues
///   → validates/converts data → writes to file → returns buffer to pool
/// </summary>
public sealed class NativeBufferPool : IDisposable
{
    private readonly ConcurrentQueue<NativeBuffer> _freeBuffers = new();
    private readonly List<NativeBuffer> _allBuffers = [];
    private readonly int _bufferSize;
    private bool _disposed;

    /// <summary>Total number of buffers in the pool.</summary>
    public int TotalCount => _allBuffers.Count;

    /// <summary>Number of buffers currently available.</summary>
    public int AvailableCount => _freeBuffers.Count;

    /// <summary>Size of each buffer in bytes.</summary>
    public int BufferSize => _bufferSize;

    /// <summary>
    /// Create a buffer pool with the specified total memory and per-buffer size.
    /// All memory is allocated from the unmanaged heap via NativeMemory.
    /// </summary>
    /// <param name="totalSizeBytes">Total memory budget (e.g. 256 MB).</param>
    /// <param name="bufferSizeBytes">Size of each individual buffer.</param>
    /// <param name="lockToRam">Lock buffers to physical RAM (requires elevated privileges).</param>
    public NativeBufferPool(long totalSizeBytes, int bufferSizeBytes, bool lockToRam = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSizeBytes);

        _bufferSize = bufferSizeBytes;
        int count = (int)(totalSizeBytes / bufferSizeBytes);
        if (count == 0) count = 1;

        for (int i = 0; i < count; i++)
        {
            var buffer = new NativeBuffer(bufferSizeBytes);
            if (lockToRam) buffer.LockToPhysicalMemory();
            _allBuffers.Add(buffer);
            _freeBuffers.Enqueue(buffer);
        }
    }

    /// <summary>
    /// Try to rent a buffer from the pool (non-blocking).
    /// Returns null if no buffers are available (buffer underflow).
    /// </summary>
    public NativeBuffer? Rent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _freeBuffers.TryDequeue(out var buffer) ? buffer : null;
    }

    /// <summary>
    /// Return a buffer to the pool after use.
    /// </summary>
    public void Return(NativeBuffer buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _freeBuffers.Enqueue(buffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var buffer in _allBuffers)
        {
            buffer.Dispose();
        }
        _allBuffers.Clear();
    }
}

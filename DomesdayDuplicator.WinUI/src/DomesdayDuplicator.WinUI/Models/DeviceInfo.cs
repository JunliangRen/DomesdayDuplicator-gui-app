// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

namespace DomesdayDuplicator.WinUI.Models;

/// <summary>
/// Information about a connected USB device.
/// </summary>
public sealed record DeviceInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    string Description,
    uint DeviceSpeed,
    int BulkInPipeId,
    uint MaxTransferSize);

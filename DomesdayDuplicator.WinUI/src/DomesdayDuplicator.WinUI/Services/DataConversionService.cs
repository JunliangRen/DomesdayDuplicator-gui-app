// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0
//
// Data conversion between 10-bit packed and 16-bit signed formats.
// Matches the original C++ dddconv/dddutil algorithms.

namespace DomesdayDuplicator.WinUI.Services;

/// <summary>
/// Service for converting RF sample data between 10-bit and 16-bit formats.
/// </summary>
public interface IDataConversionService
{
    /// <summary>
    /// Convert a file between formats with progress reporting.
    /// </summary>
    Task ConvertAsync(
        string inputPath,
        string outputPath,
        bool inputIsTenBit,
        bool outputIsTenBit,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Verify test data integrity.
    /// </summary>
    Task<bool> VerifyTestDataAsync(
        string inputPath,
        bool isTenBit,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>Get the number of samples in a file.</summary>
    long GetSampleCount(string filePath, bool isTenBit);

    /// <summary>Get the duration at 40 MHz sample rate.</summary>
    TimeSpan GetDuration(string filePath, bool isTenBit);
}

public sealed class DataConversionService : IDataConversionService
{
    private const int SampleRate = 40_000_000;
    private const int BufferSizeBytes = 20 * 1024 * 1024; // 20 MB chunks

    public long GetSampleCount(string filePath, bool isTenBit)
    {
        long fileSize = new FileInfo(filePath).Length;
        return isTenBit ? (fileSize / 5) * 4 : fileSize / 2;
    }

    public TimeSpan GetDuration(string filePath, bool isTenBit)
    {
        long samples = GetSampleCount(filePath, isTenBit);
        return TimeSpan.FromSeconds((double)samples / SampleRate);
    }

    public async Task ConvertAsync(
        string inputPath, string outputPath,
        bool inputIsTenBit, bool outputIsTenBit,
        TimeSpan? startTime, TimeSpan? endTime,
        IProgress<double>? progress, CancellationToken ct)
    {
        long totalSamples = GetSampleCount(inputPath, inputIsTenBit);
        long startSample = startTime.HasValue ? (long)(startTime.Value.TotalSeconds * SampleRate) : 0;
        long endSample = endTime.HasValue ? (long)(endTime.Value.TotalSeconds * SampleRate) : totalSamples;
        endSample = Math.Min(endSample, totalSamples);

        await Task.Run(() =>
        {
            using var input = File.OpenRead(inputPath);
            using var output = File.Create(outputPath);

            // Seek to start position
            long startByte = inputIsTenBit ? (startSample / 4) * 5 : startSample * 2;
            input.Seek(startByte, SeekOrigin.Begin);

            long samplesToProcess = endSample - startSample;
            long samplesProcessed = 0;
            var readBuffer = new byte[BufferSizeBytes];

            while (samplesProcessed < samplesToProcess)
            {
                ct.ThrowIfCancellationRequested();

                int bytesRead = input.Read(readBuffer, 0, readBuffer.Length);
                if (bytesRead == 0) break;

                // Unpack input to 16-bit samples
                ushort[] samples;
                if (inputIsTenBit)
                {
                    samples = UnpackTenBit(readBuffer.AsSpan(0, bytesRead));
                }
                else
                {
                    int sampleCount = bytesRead / 2;
                    samples = new ushort[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        samples[i] = (ushort)(readBuffer[i * 2] | (readBuffer[i * 2 + 1] << 8));
                    }
                }

                // Pack output
                byte[] outputBytes;
                if (outputIsTenBit)
                {
                    outputBytes = PackTenBit(samples);
                }
                else
                {
                    outputBytes = new byte[samples.Length * 2];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        // Convert 10-bit to 16-bit signed: (value - 512) * 64
                        short signed16 = (short)((samples[i] - 512) * 64);
                        outputBytes[i * 2] = (byte)(signed16 & 0xFF);
                        outputBytes[i * 2 + 1] = (byte)((signed16 >> 8) & 0xFF);
                    }
                }

                output.Write(outputBytes);
                samplesProcessed += samples.Length;
                progress?.Report((double)samplesProcessed / samplesToProcess);
            }
        }, ct);
    }

    public async Task<bool> VerifyTestDataAsync(
        string inputPath, bool isTenBit,
        IProgress<double>? progress, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var input = File.OpenRead(inputPath);
            long totalSamples = GetSampleCount(inputPath, isTenBit);
            long samplesProcessed = 0;
            var readBuffer = new byte[BufferSizeBytes];

            int expectedValue = -1;
            int testDataMax = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                int bytesRead = input.Read(readBuffer, 0, readBuffer.Length);
                if (bytesRead == 0) break;

                ushort[] samples;
                if (isTenBit)
                {
                    samples = UnpackTenBit(readBuffer.AsSpan(0, bytesRead));
                }
                else
                {
                    int sampleCount = bytesRead / 2;
                    samples = new ushort[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        // Convert 16-bit signed back to 10-bit: (value / 64) + 512
                        short signed16 = (short)(readBuffer[i * 2] | (readBuffer[i * 2 + 1] << 8));
                        samples[i] = (ushort)(signed16 / 64 + 512);
                    }
                }

                foreach (var sample in samples)
                {
                    if (expectedValue < 0)
                    {
                        expectedValue = sample;
                        continue;
                    }

                    if (sample != expectedValue)
                    {
                        if (testDataMax == 0 && sample == 0)
                        {
                            testDataMax = expectedValue; // Auto-detect wrap point
                        }
                        else
                        {
                            return false; // Test failed
                        }
                    }

                    expectedValue++;
                    if (testDataMax > 0 && expectedValue > testDataMax)
                        expectedValue = 0;
                }

                samplesProcessed += samples.Length;
                progress?.Report((double)samplesProcessed / totalSamples);
            }

            return true;
        }, ct);
    }

    /// <summary>
    /// Unpack 10-bit packed data (5 bytes → 4 samples).
    /// </summary>
    private static ushort[] UnpackTenBit(ReadOnlySpan<byte> data)
    {
        int groups = data.Length / 5;
        var samples = new ushort[groups * 4];

        for (int g = 0; g < groups; g++)
        {
            int bi = g * 5;
            int si = g * 4;

            samples[si] = (ushort)((data[bi] << 2) | ((data[bi + 1] & 0xC0) >> 6));
            samples[si + 1] = (ushort)(((data[bi + 1] & 0x3F) << 4) | ((data[bi + 2] & 0xF0) >> 4));
            samples[si + 2] = (ushort)(((data[bi + 2] & 0x0F) << 6) | ((data[bi + 3] & 0xFC) >> 2));
            samples[si + 3] = (ushort)(((data[bi + 3] & 0x03) << 8) | data[bi + 4]);
        }

        return samples;
    }

    /// <summary>
    /// Pack 10-bit data (4 samples → 5 bytes).
    /// </summary>
    private static byte[] PackTenBit(ushort[] samples)
    {
        int groups = samples.Length / 4;
        var data = new byte[groups * 5];

        for (int g = 0; g < groups; g++)
        {
            int si = g * 4;
            int bi = g * 5;

            ushort s0 = samples[si];
            ushort s1 = samples[si + 1];
            ushort s2 = samples[si + 2];
            ushort s3 = samples[si + 3];

            data[bi] = (byte)(s0 >> 2);
            data[bi + 1] = (byte)(((s0 & 0x03) << 6) | (s1 >> 4));
            data[bi + 2] = (byte)(((s1 & 0x0F) << 4) | (s2 >> 6));
            data[bi + 3] = (byte)(((s2 & 0x3F) << 2) | (s3 >> 8));
            data[bi + 4] = (byte)(s3 & 0xFF);
        }

        return data;
    }
}

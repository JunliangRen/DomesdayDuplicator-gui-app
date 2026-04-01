// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using DomesdayDuplicator.WinUI.Models;

namespace DomesdayDuplicator.WinUI.Services;

/// <summary>
/// Service for communicating with a LaserDisc player via serial port.
/// </summary>
public interface IPlayerCommunicationService : IDisposable
{
    bool IsConnected { get; }
    PlayerState CurrentState { get; }
    string? CurrentTimeCode { get; }
    int? CurrentFrameNumber { get; }

    Task<bool> ConnectAsync(string portName, SerialSpeed speed);
    void Disconnect();

    // Playback commands
    Task SendPlayAsync();
    Task SendPauseAsync();
    Task SendStopAsync();
    Task SendStepForwardAsync();
    Task SendStepReverseAsync();
    Task SendScanForwardAsync();
    Task SendScanReverseAsync();
    Task SendSearchAsync(int frameOrTime);
    Task SendSpeedChangeAsync(int speed);
    Task SendMultiSpeedForwardAsync();
    Task SendMultiSpeedReverseAsync();
    Task SendRejectAsync();
    Task SendRepeatAsync();
    Task SendDisplayAsync();
    Task SendAudioAsync();
    Task SendClearAsync();
    Task SendDigitAsync(int digit);
    Task<string?> SendRawCommandAsync(string command);

    event EventHandler<PlayerState>? StateChanged;
    event EventHandler<string?>? TimeCodeChanged;
}

/// <summary>
/// Serial port implementation of player communication.
/// Uses System.IO.Ports.SerialPort.
/// </summary>
public sealed class PlayerCommunicationService : IPlayerCommunicationService
{
    private System.IO.Ports.SerialPort? _serialPort;
    private volatile PlayerState _state = PlayerState.Disconnected;
    private string? _timeCode;
    private int? _frameNumber;
    private Timer? _pollTimer;

    public bool IsConnected => _serialPort?.IsOpen == true;
    public PlayerState CurrentState => _state;
    public string? CurrentTimeCode => _timeCode;
    public int? CurrentFrameNumber => _frameNumber;

    public event EventHandler<PlayerState>? StateChanged;
    public event EventHandler<string?>? TimeCodeChanged;

    public Task<bool> ConnectAsync(string portName, SerialSpeed speed)
    {
        try
        {
            _serialPort = new System.IO.Ports.SerialPort(portName)
            {
                BaudRate = speed == SerialSpeed.AutoDetect ? 9600 : (int)speed,
                DataBits = 8,
                Parity = System.IO.Ports.Parity.None,
                StopBits = System.IO.Ports.StopBits.One,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            _serialPort.Open();
            _state = PlayerState.Stopped;
            StateChanged?.Invoke(this, _state);

            // Start polling for player status
            _pollTimer = new Timer(PollPlayerStatus, null, 500, 500);

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public void Disconnect()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _serialPort?.Close();
        _serialPort?.Dispose();
        _serialPort = null;
        _state = PlayerState.Disconnected;
        StateChanged?.Invoke(this, _state);
    }

    private async void PollPlayerStatus(object? state)
    {
        if (!IsConnected) return;
        try
        {
            var response = await SendRawCommandAsync("?F");
            if (response != null)
            {
                _timeCode = response;
                TimeCodeChanged?.Invoke(this, _timeCode);
            }
        }
        catch { /* Ignore poll errors */ }
    }

    private Task<string?> SendCommandAsync(string command)
    {
        return Task.Run(() =>
        {
            if (_serialPort == null || !_serialPort.IsOpen) return null;
            try
            {
                _serialPort.DiscardInBuffer();
                _serialPort.Write(command + "\r");
                Thread.Sleep(50);
                return _serialPort.ReadExisting();
            }
            catch { return null; }
        });
    }

    public Task SendPlayAsync() => SendCommandAsync("PL");
    public Task SendPauseAsync() => SendCommandAsync("PA");
    public Task SendStopAsync() => SendCommandAsync("RJ");
    public Task SendStepForwardAsync() => SendCommandAsync("SF");
    public Task SendStepReverseAsync() => SendCommandAsync("SR");
    public Task SendScanForwardAsync() => SendCommandAsync("NF");
    public Task SendScanReverseAsync() => SendCommandAsync("NR");
    public Task SendSearchAsync(int frameOrTime) => SendCommandAsync($"{frameOrTime}SE");
    public Task SendSpeedChangeAsync(int speed) => SendCommandAsync($"{speed}SP");
    public Task SendMultiSpeedForwardAsync() => SendCommandAsync("MF");
    public Task SendMultiSpeedReverseAsync() => SendCommandAsync("MR");
    public Task SendRejectAsync() => SendCommandAsync("RJ");
    public Task SendRepeatAsync() => SendCommandAsync("RP");
    public Task SendDisplayAsync() => SendCommandAsync("DS");
    public Task SendAudioAsync() => SendCommandAsync("AD");
    public Task SendClearAsync() => SendCommandAsync("CL");
    public Task SendDigitAsync(int digit) => SendCommandAsync(digit.ToString());
    public Task<string?> SendRawCommandAsync(string command) => SendCommandAsync(command);

    public void Dispose()
    {
        Disconnect();
    }
}

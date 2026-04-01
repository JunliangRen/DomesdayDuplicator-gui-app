// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DomesdayDuplicator.WinUI.Helpers;
using DomesdayDuplicator.WinUI.Models;

namespace DomesdayDuplicator.WinUI.ViewModels;

/// <summary>
/// ViewModel for the LaserDisc player remote control page.
/// </summary>
public sealed partial class PlayerControlViewModel : ObservableObject
{
    private readonly Services.IPlayerCommunicationService _player;
    private readonly Services.IConfigurationService _config;

    // ── Connection state ─────────────────────────────────────────
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private PlayerState _playerState = PlayerState.Disconnected;
    [ObservableProperty] private string _statusText = "Disconnected";

    // ── Display ──────────────────────────────────────────────────
    [ObservableProperty] private string _displayText = string.Empty;
    [ObservableProperty] private string _timeCodeText = "—";
    [ObservableProperty] private string _entryText = string.Empty;

    // ── Manual serial ────────────────────────────────────────────
    [ObservableProperty] private string _manualCommand = string.Empty;
    [ObservableProperty] private string _manualResponse = string.Empty;

    // ── Available ports ──────────────────────────────────────────
    [ObservableProperty] private string[] _availablePorts = [];
    [ObservableProperty] private string _selectedPort = string.Empty;

    public PlayerControlViewModel()
    {
        _player = ServiceLocator.PlayerCommunication;
        _config = ServiceLocator.Configuration;
    }

    public void Initialize()
    {
        RefreshPorts();
        _player.StateChanged += (_, s) =>
        {
            PlayerState = s;
            IsConnected = s != PlayerState.Disconnected;
            StatusText = s.ToString();
        };
        _player.TimeCodeChanged += (_, tc) => TimeCodeText = tc ?? "—";
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        AvailablePorts = System.IO.Ports.SerialPort.GetPortNames();
        if (AvailablePorts.Length > 0 && string.IsNullOrEmpty(SelectedPort))
            SelectedPort = AvailablePorts[0];
    }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            _player.Disconnect();
        }
        else
        {
            if (string.IsNullOrEmpty(SelectedPort)) return;
            var speed = _config.Configuration.SerialSpeed;
            bool ok = await _player.ConnectAsync(SelectedPort, speed);
            if (!ok) StatusText = "Connection failed";
        }
    }

    // ── Playback commands ────────────────────────────────────────
    [RelayCommand] private async Task PlayAsync() => await _player.SendPlayAsync();
    [RelayCommand] private async Task PauseAsync() => await _player.SendPauseAsync();
    [RelayCommand] private async Task StopAsync() => await _player.SendStopAsync();
    [RelayCommand] private async Task StepForwardAsync() => await _player.SendStepForwardAsync();
    [RelayCommand] private async Task StepReverseAsync() => await _player.SendStepReverseAsync();
    [RelayCommand] private async Task ScanForwardAsync() => await _player.SendScanForwardAsync();
    [RelayCommand] private async Task ScanReverseAsync() => await _player.SendScanReverseAsync();
    [RelayCommand] private async Task RejectAsync() => await _player.SendRejectAsync();
    [RelayCommand] private async Task RepeatAsync() => await _player.SendRepeatAsync();
    [RelayCommand] private async Task DisplayAsync() => await _player.SendDisplayAsync();
    [RelayCommand] private async Task AudioAsync() => await _player.SendAudioAsync();
    [RelayCommand] private async Task MultiSpeedForwardAsync() => await _player.SendMultiSpeedForwardAsync();
    [RelayCommand] private async Task MultiSpeedReverseAsync() => await _player.SendMultiSpeedReverseAsync();
    [RelayCommand] private async Task SpeedUpAsync() => await _player.SendSpeedChangeAsync(1);
    [RelayCommand] private async Task SpeedDownAsync() => await _player.SendSpeedChangeAsync(-1);

    [RelayCommand]
    private async Task DigitAsync(string digit)
    {
        if (int.TryParse(digit, out int d))
        {
            EntryText += digit;
            await _player.SendDigitAsync(d);
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        EntryText = string.Empty;
        await _player.SendClearAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (int.TryParse(EntryText, out int frame))
        {
            await _player.SendSearchAsync(frame);
            EntryText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SendManualCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualCommand)) return;
        ManualResponse = await _player.SendRawCommandAsync(ManualCommand) ?? "(no response)";
    }
}

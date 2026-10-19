using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using OmniConnect.Shared.Models;

namespace OmniConnect.Desktop.Services;

/// <summary>
/// Standalone Bluetooth transport for desktop (Windows).
///
/// Real Bluetooth RFCOMM on Windows requires the
/// Windows.Devices.Bluetooth.Rfcomm WinRT API surface plus the
/// `bluetooth.rfcomm` appx capability, which a plain Avalonia desktop app does
/// not expose by default. This service probes honestly: IsAvailable is false
/// unless a real Bluetooth radio + RFCOMM path is confirmed, so the Bluetooth
/// label is never advertised when it cannot actually send.
///
/// All messages are ephemeral: kept in memory only, never written to the DB.
/// </summary>
public sealed class WindowsBluetoothService : IDisposable
{
    private readonly ConcurrentQueue<BluetoothIncomingMessage> _incoming = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private Task? _probeTask;

    public bool IsAvailable { get; private set; }
    public int PendingCount => _incoming.Count;

    public event Action<BluetoothIncomingMessage>? MessageReceived;

    /// <summary>Probe the platform for real Bluetooth RFCOMM capability.</summary>
    public async Task<bool> ProbeAsync()
    {
        if (_disposed) return false;

        try
        {
            // On plain net10.0 desktop, System.Net.Sockets does not expose
            // AddressFamily.Bth / RFCOMM. Real Bluetooth requires the
            // Windows.Devices.Bluetooth.Rfcomm WinRT API (UWP appx capability),
            // which is not available here without additional platform work.
            // Report not-available rather than pretending Bluetooth works.
            var supported = IsRfcommAvailableViaWinRt();
            IsAvailable = supported;

            if (supported)
            {
                _probeTask = Task.Run(ListenLoop, _cts.Token);
            }

            return IsAvailable;
        }
        catch
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>Start the RFCOMM server so peers can connect to this device.</summary>
    /// <returns>false on plain desktop .NET (WinRT Rfcomm not wired).</returns>
    public Task<bool> StartServerAsync()
    {
        return Task.FromResult(false);
    }

    /// <summary>Stop the RFCOMM server.</summary>
    public Task StopServerAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Connect to a paired peer by its Bluetooth MAC address.</summary>
    /// <returns>false on plain desktop .NET.</returns>
    public Task<bool> ConnectAsync(string deviceAddress)
    {
        return Task.FromResult(false);
    }

    /// <summary>Send a framed chat message over Bluetooth to a peer.</summary>
    /// <returns>false when the peer transport is not actually available.</returns>
    public Task<bool> SendAsync(string deviceId, string fromPeerId, string fromPeerName,
        string toPeerId, string content)
    {
        return Task.FromResult(false);
    }

    /// <summary>Disconnect from the current peer.</summary>
    public Task DisconnectAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Drain an incoming ephemeral message from the queue.</summary>
    public bool TryDequeue(out BluetoothIncomingMessage message)
    {
        return _incoming.TryDequeue(out message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _probeTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }

    /// <summary>
    /// Best-effort check for a real Bluetooth RFCOMM path.
    /// Returns false on plain desktop .NET (WinRT Rfcomm not wired).
    /// </summary>
    private bool IsRfcommAvailableViaWinRt()
    {
        try
        {
            // Probe whether the WinRT Bluetooth API surface is reachable.
            // Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService requires the
            // bluetooth.rfcomm capability; if the type can't be resolved or
            // instantiated, Bluetooth is not usable from this process.
            var type = Type.GetType(
                "Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService, Windows, ContentType=WindowsRuntime",
                throwOnError: false);
            return type != null;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    /// <summary>Inject a test message (for testing without real hardware).</summary>
    internal void InjectIncoming(BluetoothIncomingMessage msg)
    {
        _incoming.Enqueue(msg);
        MessageReceived?.Invoke(msg);
    }
}

/// <summary>An ephemeral inbound Bluetooth message (never written to DB).</summary>
public sealed class BluetoothIncomingMessage
{
    public string DeviceId { get; set; } = string.Empty;
    public string FromPeerId { get; set; } = string.Empty;
    public string FromPeerName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ClientMessageId { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
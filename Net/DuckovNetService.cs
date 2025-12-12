// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;
using DuckovNet;
using DuckovTogether.Core;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Net;

public class DuckovNetService : INetEventListener
{
    private readonly ServerConfig _config;
    private QuicTransport _transport;
    private NetDataWriter _writer = new();
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly object _lock = new();
    
    public bool IsRunning => _transport?.IsRunning ?? false;
    public int PlayerCount => _players.Count;
    
    public event Action<int, PlayerState> OnPlayerConnected;
    public event Action<int, DisconnectReason> OnPlayerDisconnected;
    public event Action<int, NetDataReader, byte> OnDataReceived;
    
    private byte[] _cachedLogoData;
    private bool _logoLoaded;
    
    public DuckovNetService(ServerConfig config)
    {
        _config = config;
    }
    
    public async Task<bool> StartAsync()
    {
        _transport = new QuicTransport();
        
        _transport.OnPeerConnectedQuic += OnQuicPeerConnected;
        _transport.OnPeerDisconnectedQuic += OnQuicPeerDisconnected;
        _transport.OnDataReceivedQuic += OnQuicDataReceived;
        
        var started = await _transport.StartServerAsync(_config.Port);
        if (started)
        {
            Log.Info($"Server started on port {_config.Port}");
            Log.Info($"Protocol: {QuicTransport.PROTOCOL_VERSION}");
            Log.Info($"Max players: {_config.MaxPlayers}");
            Log.Info("Press Ctrl+C to stop");
            Console.WriteLine();
            PrintNetworkInfo();
        }
        else
        {
            Log.Error($"Failed to start on port {_config.Port}");
        }
        return started;
    }
    
    private void PrintNetworkInfo()
    {
        Log.Info("===========================================");
        Log.Info("  Connection Information (QUIC/TLS 1.3)");
        Log.Info("===========================================");
        Log.Info($"  Local:    127.0.0.1:{_config.Port}");
        
        foreach (var ip in GetLanAddresses())
        {
            Log.Info($"  LAN:      {ip}:{_config.Port}");
        }
        
        Task.Run(async () =>
        {
            var publicIP = await GetPublicIPAsync();
            if (!string.IsNullOrEmpty(publicIP))
            {
                Log.Info($"  Public:   {publicIP}:{_config.Port}");
            }
            Log.Info("===========================================");
            Console.WriteLine();
        });
    }
    
    private List<string> GetLanAddresses()
    {
        var addresses = new List<string>();
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                {
                    addresses.Add(ip.ToString());
                }
            }
        }
        catch { }
        return addresses;
    }
    
    private async Task<string> GetPublicIPAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            return (await client.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                return (await client.GetStringAsync("https://icanhazip.com")).Trim();
            }
            catch { return null; }
        }
    }
    
    public void Stop()
    {
        _transport?.Stop();
        lock (_lock)
        {
            _players.Clear();
            _peers.Clear();
        }
        Log.Info("Server stopped");
    }
    
    public void SendToAll(NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        var data = writer.CopyData();
        var mode = ConvertDeliveryMethod(method);
        _transport?.SendToAll(data, mode);
    }
    
    public void SendToPeer(NetPeer peer, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        if (peer?.QuicPeer == null) return;
        var data = writer.CopyData();
        var mode = ConvertDeliveryMethod(method);
        _transport?.Send(peer.QuicPeer, data, mode);
    }
    
    public void SendToPeer(int peerId, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        lock (_lock)
        {
            if (_peers.TryGetValue(peerId, out var peer))
            {
                SendToPeer(peer, writer, method);
            }
        }
    }
    
    public void BroadcastExcept(NetPeer except, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        var data = writer.CopyData();
        var mode = ConvertDeliveryMethod(method);
        _transport?.SendToAllExcept(except?.Id ?? -1, data, mode);
    }
    
    public void BroadcastExcept(int exceptId, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        var data = writer.CopyData();
        var mode = ConvertDeliveryMethod(method);
        _transport?.SendToAllExcept(exceptId, data, mode);
    }
    
    public PlayerState GetPlayer(int peerId)
    {
        lock (_lock)
        {
            return _players.TryGetValue(peerId, out var state) ? state : null;
        }
    }
    
    public IEnumerable<PlayerState> GetAllPlayers()
    {
        lock (_lock)
        {
            return _players.Values.ToList();
        }
    }
    
    public IEnumerable<int> GetAllPeerIds()
    {
        lock (_lock)
        {
            return _players.Keys.ToList();
        }
    }
    
    public NetPeer GetPeer(int peerId)
    {
        lock (_lock)
        {
            return _peers.TryGetValue(peerId, out var peer) ? peer : null;
        }
    }
    
    public void DisconnectPeer(int peerId, string reason)
    {
        lock (_lock)
        {
            if (_peers.TryGetValue(peerId, out var peer) && peer.QuicPeer != null)
            {
                _transport?.Disconnect(peer.QuicPeer, reason);
            }
        }
    }
    
    private void OnQuicPeerConnected(QuicPeer quicPeer)
    {
        Log.Info($"Player connected: {quicPeer.EndPoint} (ID: {quicPeer.Id})");
        
        var netPeer = new NetPeer
        {
            Id = quicPeer.Id,
            EndPoint = quicPeer.EndPoint,
            ConnectionState = ConnectionState.Connected,
            QuicPeer = quicPeer,
            SendAction = (data, method) => _transport?.Send(quicPeer, data, ConvertDeliveryMethod(method))
        };
        
        var state = new PlayerState
        {
            PeerId = quicPeer.Id,
            EndPoint = quicPeer.EndPoint,
            PlayerName = $"Player_{quicPeer.Id}",
            ConnectTime = DateTime.Now
        };
        
        lock (_lock)
        {
            _peers[quicPeer.Id] = netPeer;
            _players[quicPeer.Id] = state;
        }
        
        OnPlayerConnected?.Invoke(quicPeer.Id, state);
    }
    
    private void OnQuicPeerDisconnected(QuicPeer quicPeer, string reason)
    {
        Log.Info($"Player disconnected: {quicPeer.EndPoint} (Reason: {reason})");
        
        lock (_lock)
        {
            _peers.Remove(quicPeer.Id);
            _players.Remove(quicPeer.Id);
        }
        
        OnPlayerDisconnected?.Invoke(quicPeer.Id, DisconnectReason.RemoteConnectionClose);
    }
    
    private void OnQuicDataReceived(QuicPeer quicPeer, byte[] data, DeliveryMode mode)
    {
        var reader = new NetDataReader(data);
        OnDataReceived?.Invoke(quicPeer.Id, reader, 0);
    }
    
    private DeliveryMode ConvertDeliveryMethod(DeliveryMethod method)
    {
        return method switch
        {
            DeliveryMethod.Unreliable => DeliveryMode.Unreliable,
            DeliveryMethod.ReliableOrdered => DeliveryMode.ReliableOrdered,
            DeliveryMethod.ReliableUnordered => DeliveryMode.Reliable,
            _ => DeliveryMode.Reliable
        };
    }
    
    private byte[] GetServerLogo()
    {
        if (_logoLoaded) return _cachedLogoData;
        _logoLoaded = true;
        
        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_logo.png");
        if (File.Exists(logoPath))
        {
            try
            {
                var fileInfo = new FileInfo(logoPath);
                if (fileInfo.Length <= 1024 * 1024)
                {
                    _cachedLogoData = File.ReadAllBytes(logoPath);
                    Log.Debug($"Loaded logo: {logoPath} ({_cachedLogoData.Length} bytes)");
                }
                else
                {
                    Log.Warn($"Logo too large (max 1MB): {fileInfo.Length} bytes");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadLogo");
            }
        }
        return _cachedLogoData;
    }

    public void OnPeerConnected(NetPeer peer) { }
    public void OnPeerDisconnected(NetPeer peer, DisconnectReason reason) { }
    public void OnNetworkReceive(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod) { }
    public void OnNetworkError(string endPoint, int socketError)
    {
        Log.Warn($"Network error: {socketError} from {endPoint}");
    }
    public void OnConnectionRequest(NetConnectionRequest request)
    {
        if (PlayerCount < _config.MaxPlayers)
        {
            request.Accept();
        }
        else
        {
            request.Reject();
        }
    }
}

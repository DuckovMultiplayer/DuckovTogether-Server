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
using LiteNetLib;
using LiteNetLib.Utils;
using DuckovTogether.Core;

namespace DuckovTogether.Net;

public class HeadlessNetService : INetEventListener
{
    private readonly ServerConfig _config;
    private NetManager? _netManager;
    private NetDataWriter _writer = new();
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly object _lock = new();
    
    public bool IsRunning => _netManager?.IsRunning ?? false;
    public int PlayerCount => _players.Count;
    public NetManager? NetManager => _netManager;
    
    public event Action<int, PlayerState>? OnPlayerConnected;
    public event Action<int, DisconnectReason>? OnPlayerDisconnected;
    public event Action<int, NetPacketReader, byte>? OnDataReceived;
    
    public HeadlessNetService(ServerConfig config)
    {
        _config = config;
    }
    
    public bool Start()
    {
        _netManager = new NetManager(this)
        {
            BroadcastReceiveEnabled = _config.EnableBroadcast,
            UnconnectedMessagesEnabled = true,
            ChannelsCount = 4,
            UseNativeSockets = true
        };
        
        var started = _netManager.Start(_config.Port);
        if (started)
        {
            Console.WriteLine($"[Server] Started on port {_config.Port}");
            Console.WriteLine($"[Server] Max players: {_config.MaxPlayers}");
            Console.WriteLine("[Server] Press Ctrl+C to stop");
            Console.WriteLine("[Server] Type 'help' for commands");
            Console.WriteLine();
            PrintNetworkInfo();
        }
        else
        {
            Console.WriteLine($"[Server] Failed to start on port {_config.Port}");
        }
        return started;
    }
    
    private void PrintNetworkInfo()
    {
        Console.WriteLine("===========================================");
        Console.WriteLine("  Connection Information");
        Console.WriteLine("===========================================");
        Console.WriteLine($"  Local:    127.0.0.1:{_config.Port}");
        
        var lanIPs = GetLanAddresses();
        foreach (var ip in lanIPs)
        {
            Console.WriteLine($"  LAN:      {ip}:{_config.Port}");
        }
        
        Task.Run(async () =>
        {
            var publicIP = await GetPublicIPAsync();
            if (!string.IsNullOrEmpty(publicIP))
            {
                Console.WriteLine($"  Public:   {publicIP}:{_config.Port}");
            }
            Console.WriteLine("===========================================");
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
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    addresses.Add(ip.ToString());
                }
            }
        }
        catch { }
        return addresses;
    }
    
    private async Task<string?> GetPublicIPAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetStringAsync("https://api.ipify.org");
            return response.Trim();
        }
        catch
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetStringAsync("https://icanhazip.com");
                return response.Trim();
            }
            catch
            {
                return null;
            }
        }
    }
    
    public void Stop()
    {
        _netManager?.Stop();
        lock (_lock)
        {
            _players.Clear();
        }
        Console.WriteLine("[Server] Stopped");
    }
    
    public void Update()
    {
        _netManager?.PollEvents();
    }
    
    public void SendToAll(NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        _netManager?.SendToAll(writer, method);
    }
    
    public void SendToPeer(NetPeer peer, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        peer.Send(writer, method);
    }
    
    public void BroadcastExcept(NetPeer except, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        if (_netManager == null) return;
        foreach (var peer in _netManager.ConnectedPeerList)
        {
            if (peer != except)
            {
                peer.Send(writer, method);
            }
        }
    }
    
    public PlayerState? GetPlayer(int peerId)
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
    
    public NetPeer? GetPeer(int peerId)
    {
        if (_netManager == null) return null;
        foreach (var peer in _netManager.ConnectedPeerList)
        {
            if (peer.Id == peerId) return peer;
        }
        return null;
    }
    
    public void Start(int port, string key)
    {
        Start();
    }
    
    public void Poll()
    {
        Update();
    }
    
    public void SendToAll(byte[] data, DeliveryMethod method)
    {
        _writer.Reset();
        _writer.Put(data);
        _netManager?.SendToAll(_writer, method);
    }
    
    public void SendToPeer(int peerId, byte[] data, DeliveryMethod method)
    {
        var peer = GetPeer(peerId);
        if (peer != null)
        {
            _writer.Reset();
            _writer.Put(data);
            peer.Send(_writer, method);
        }
    }
    
    public void DisconnectPeer(int peerId, string reason)
    {
        var peer = GetPeer(peerId);
        if (peer != null)
        {
            _writer.Reset();
            _writer.Put(reason);
            peer.Disconnect(_writer);
        }
    }
    
    public void OnPeerConnected(NetPeer peer)
    {
        Console.WriteLine($"[Server] Player connected: {peer.EndPoint} (ID: {peer.Id})");
        
        var state = new PlayerState
        {
            PeerId = peer.Id,
            EndPoint = peer.EndPoint.ToString(),
            PlayerName = $"Player_{peer.Id}",
            ConnectTime = DateTime.Now
        };
        
        lock (_lock)
        {
            _players[peer.Id] = state;
        }
        
        OnPlayerConnected?.Invoke(peer.Id, state);
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Console.WriteLine($"[Server] Player disconnected: {peer.EndPoint} (Reason: {disconnectInfo.Reason})");
        
        lock (_lock)
        {
            _players.Remove(peer.Id);
        }
        
        OnPlayerDisconnected?.Invoke(peer.Id, disconnectInfo.Reason);
    }
    
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Console.WriteLine($"[Server] Network error: {socketError} from {endPoint}");
    }
    
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        OnDataReceived?.Invoke(peer.Id, reader, channelNumber);
    }
    
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        var msg = reader.GetString();
        if (msg == "DISCOVER_REQUEST")
        {
            _writer.Reset();
            _writer.Put("DISCOVER_RESPONSE");
            _writer.Put(_config.ServerName);
            _writer.Put(PlayerCount);
            _writer.Put(_config.MaxPlayers);
            _writer.Put(Plugins.PluginManager.Instance?.LoadedPluginCount ?? 0);
            _writer.Put(_config.ServerIcon ?? "default");
            _netManager?.SendUnconnectedMessage(_writer, remoteEndPoint);
            Console.WriteLine($"[Server] Discovery request from {remoteEndPoint}");
        }
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        lock (_lock)
        {
            if (_players.TryGetValue(peer.Id, out var state))
            {
                state.Latency = latency;
            }
        }
    }
    
    public void OnConnectionRequest(ConnectionRequest request)
    {
        if (PlayerCount >= _config.MaxPlayers)
        {
            request.Reject();
            Console.WriteLine($"[Server] Connection rejected (server full): {request.RemoteEndPoint}");
            return;
        }
        
        var data = request.Data;
        if (data != null && data.GetString() == _config.GameKey)
        {
            request.Accept();
            Console.WriteLine($"[Server] Connection accepted: {request.RemoteEndPoint}");
        }
        else
        {
            request.Reject();
            Console.WriteLine($"[Server] Connection rejected (invalid key): {request.RemoteEndPoint}");
        }
    }
}

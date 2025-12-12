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
using System.Text;
using DuckovNet;
using DuckovTogether.Core;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Net;

public class QuicNetService
{
    private readonly ServerConfig _config;
    private QuicTransport _transport;
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly object _lock = new();
    
    public bool IsRunning => _transport?.IsRunning ?? false;
    public int PlayerCount => _players.Count;
    
    public event Action<int, PlayerState> OnPlayerConnected;
    public event Action<int, string> OnPlayerDisconnected;
    public event Action<int, byte[], DeliveryMode> OnDataReceived;
    
    public QuicNetService(ServerConfig config)
    {
        _config = config;
    }
    
    public async Task<bool> StartAsync()
    {
        if (!QuicTransport.IsSupported)
        {
            Log.Warn("QUIC not supported, falling back to UDP+KCP");
            return await StartFallbackAsync();
        }
        
        _transport = new QuicTransport();
        _transport.OnPeerConnectedQuic += OnPeerConnectedHandler;
        _transport.OnPeerDisconnectedQuic += OnPeerDisconnectedHandler;
        _transport.OnDataReceivedQuic += OnDataReceivedHandler;
        
        var started = await _transport.StartServerAsync(_config.Port);
        if (started)
        {
            Log.Info($"Started on port {_config.Port}");
            Log.Info($"Protocol: {QuicTransport.PROTOCOL_VERSION}");
            Log.Info($"Max players: {_config.MaxPlayers}");
            Log.Info("Press Ctrl+C to stop");
            Console.WriteLine();
            PrintNetworkInfo();
        }
        return started;
    }
    
    private async Task<bool> StartFallbackAsync()
    {
        _transport = new QuicTransport();
        Log.Info("Using DuckovNet UDP+KCP fallback");
        return false;
    }
    
    private void PrintNetworkInfo()
    {
        Log.Info("===========================================");
        Log.Info("  Connection Information (QUIC)");
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
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
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
        lock (_lock) { _players.Clear(); }
        Log.Info("Server stopped");
    }
    
    public void SendToAll(byte[] data, DeliveryMode mode = DeliveryMode.Reliable)
    {
        _transport?.SendToAll(data, mode);
    }
    
    public void SendToPeer(int peerId, byte[] data, DeliveryMode mode = DeliveryMode.Reliable)
    {
        var peer = _transport?.GetPeer(peerId);
        if (peer != null) _transport?.Send(peer, data, mode);
    }
    
    public void BroadcastExcept(int exceptId, byte[] data, DeliveryMode mode = DeliveryMode.Reliable)
    {
        _transport?.SendToAllExcept(exceptId, data, mode);
    }
    
    public PlayerState GetPlayer(int peerId)
    {
        lock (_lock) { return _players.TryGetValue(peerId, out var state) ? state : null; }
    }
    
    public IEnumerable<PlayerState> GetAllPlayers()
    {
        lock (_lock) { return _players.Values.ToList(); }
    }
    
    public void DisconnectPeer(int peerId, string reason)
    {
        var peer = _transport?.GetPeer(peerId);
        if (peer != null) _transport?.Disconnect(peer, reason);
    }
    
    private void OnPeerConnectedHandler(QuicPeer peer)
    {
        Log.Info($"Player connected: {peer.EndPoint} (ID: {peer.Id})");
        
        var state = new PlayerState
        {
            PeerId = peer.Id,
            EndPoint = peer.EndPoint,
            PlayerName = $"Player_{peer.Id}",
            ConnectTime = DateTime.Now
        };
        
        lock (_lock) { _players[peer.Id] = state; }
        OnPlayerConnected?.Invoke(peer.Id, state);
    }
    
    private void OnPeerDisconnectedHandler(QuicPeer peer, string reason)
    {
        Log.Info($"Player disconnected: {peer.EndPoint} (Reason: {reason})");
        lock (_lock) { _players.Remove(peer.Id); }
        OnPlayerDisconnected?.Invoke(peer.Id, reason);
    }
    
    private void OnDataReceivedHandler(QuicPeer peer, byte[] data, DeliveryMode mode)
    {
        OnDataReceived?.Invoke(peer.Id, data, mode);
    }
    
    public int GetPeerLatency(int peerId)
    {
        var peer = _transport?.GetPeer(peerId);
        return peer?.Latency ?? -1;
    }
}

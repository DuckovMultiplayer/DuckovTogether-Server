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
using DuckovTogether.Core.GameLogic;

namespace DuckovTogether.Net;

public class HeadlessNetService : INetEventListener
{
    private readonly ServerConfig _config;
    private DualProtocolServer? _server;
    private NetManager? _netManager;
    private NetDataWriter _writer = new();
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly object _lock = new();
    
    public bool IsRunning => _server?.IsRunning ?? false;
    public int PlayerCount => _players.Count;
    public NetManager? NetManager => _netManager;
    public DualProtocolServer? Server => _server;
    
    public event Action<int, PlayerState>? OnPlayerConnected;
    public event Action<int, DisconnectReason>? OnPlayerDisconnected;
    public event Action<int, NetPacketReader, byte>? OnDataReceived;
    
    public HeadlessNetService(ServerConfig config)
    {
        _config = config;
    }
    
    public bool Start()
    {
        _server = new DualProtocolServer();
        _server.OnPeerConnected += OnDualPeerConnected;
        _server.OnPeerDisconnected += OnDualPeerDisconnected;
        _server.OnDataReceived += OnDualDataReceived;
        _server.OnUnconnectedReceived += OnUnconnectedMessage;
        
        var kcpPort = _config.Port;
        var quicPort = _config.Port + 1;
        
        bool started;
        if (QuicTransport.IsSupported && !string.IsNullOrEmpty(_config.CertPath))
        {
            started = _server.Start(kcpPort, quicPort, _config.CertPath, _config.KeyPath);
        }
        else
        {
            started = _server.StartKcpOnly(kcpPort);
        }
        
        if (started)
        {
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
        
        _netManager = new NetManager(this);
        
        return started;
    }
    
    private void OnDualPeerConnected(DualPeer dualPeer)
    {
        var peer = new NetPeer { Id = dualPeer.Id, EndPoint = dualPeer.EndPoint };
        OnPeerConnected(peer);
    }
    
    private void OnDualPeerDisconnected(DualPeer dualPeer, string reason)
    {
        var peer = new NetPeer { Id = dualPeer.Id };
        OnPeerDisconnected(peer, DisconnectReason.RemoteConnectionClose);
    }
    
    private void OnDualDataReceived(DualPeer dualPeer, byte[] data, DeliveryMode mode)
    {
        var peer = new NetPeer { Id = dualPeer.Id };
        var reader = new NetPacketReader(data);
        var method = mode == DeliveryMode.Unreliable ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered;
        OnNetworkReceive(peer, reader, method);
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
    
    public void SendToPeer(int peerId, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        var peer = GetPeer(peerId);
        peer?.Send(writer, method);
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
        
        SendServerState(peer.Id);
    }
    
    private void SendServerState(int peerId)
    {
        try
        {
            var currentScene = GameServer.Instance?.Saves?.CurrentWorld?.CurrentScene ?? "";
            var gameTime = GameServer.Instance?.Saves?.CurrentWorld?.GameTime ?? 8f;
            var gameDay = GameServer.Instance?.Saves?.CurrentWorld?.GameDay ?? 1;
            
            var serverState = new
            {
                type = "serverState",
                scene = currentScene,
                gameTime = gameTime,
                gameDay = gameDay,
                serverName = _config.ServerName,
                maxPlayers = _config.MaxPlayers,
                playerCount = _players.Count
            };
            
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(serverState);
            _writer.Reset();
            _writer.Put((byte)9);
            _writer.Put(json);
            
            SendToPeer(peerId, _writer, DeliveryMethod.ReliableOrdered);
            Console.WriteLine($"[Server] Sent server state to peer {peerId}: scene={currentScene}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Error sending server state: {ex.Message}");
        }
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectReason reason)
    {
        Console.WriteLine($"[Server] Player disconnected: {peer.EndPoint} (Reason: {reason})");
        
        lock (_lock)
        {
            _players.Remove(peer.Id);
        }
        
        OnPlayerDisconnected?.Invoke(peer.Id, reason);
    }
    
    public void OnNetworkError(string endPoint, int socketError)
    {
        Console.WriteLine($"[Server] Network error: {socketError} from {endPoint}");
    }
    
    public void OnNetworkReceive(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod)
    {
        var packetReader = new NetPacketReader(reader.RawData, reader.Position, reader.AvailableBytes);
        OnDataReceived?.Invoke(peer.Id, packetReader, 0);
    }
    
    private byte[] _cachedLogoData;
    private bool _logoLoaded;
    
    public byte[] GetServerLogo()
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
                    Console.WriteLine($"[Server] Loaded logo: {logoPath} ({_cachedLogoData.Length} bytes)");
                }
                else
                {
                    Console.WriteLine($"[Server] Logo too large (max 1MB): {fileInfo.Length} bytes");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Failed to load logo: {ex.Message}");
            }
        }
        return _cachedLogoData;
    }
    
    private void OnUnconnectedMessage(IPEndPoint remoteEndPoint, byte[] data)
    {
        var reader = new NetDataReader(data);
        if (!reader.TryGetString(out var msg)) return;
        
        if (msg == "LOGO_REQUEST")
        {
            SendLogoChunks(remoteEndPoint);
            return;
        }
        
        if (msg == "DISCOVER_REQUEST")
        {
            _writer.Reset();
            _writer.Put("DISCOVER_RESPONSE");
            _writer.Put(_config.ServerName);
            _writer.Put(PlayerCount);
            _writer.Put(_config.MaxPlayers);
            _writer.Put(Plugins.PluginManager.Instance?.LoadedPluginCount ?? 0);
            _writer.Put(_config.ServerIcon ?? "default");
            _writer.Put(_config.ServerDescription ?? "");
            _writer.Put("KCP/UDP");
            
            var logoData = GetServerLogo();
            var maxLogoSize = 50000;
            if (logoData != null && logoData.Length > 0 && logoData.Length <= maxLogoSize)
            {
                _writer.Put(true);
                _writer.Put(logoData.Length);
                _writer.Put(logoData);
            }
            else
            {
                _writer.Put(false);
                _writer.Put(logoData?.Length ?? 0);
            }
            
            var responseData = _writer.CopyData();
            _server?.SendUnconnected(remoteEndPoint, responseData);
            Console.WriteLine($"[Server] Discovery response to {remoteEndPoint}, {responseData.Length} bytes");
        }
    }
    
    private const int CHUNK_SIZE = 40000;
    
    private void SendLogoChunks(IPEndPoint endpoint)
    {
        var logoData = GetServerLogo();
        if (logoData == null || logoData.Length == 0)
        {
            var writer = new NetDataWriter();
            writer.Put("LOGO_RESPONSE");
            writer.Put(0);
            writer.Put(0);
            writer.Put(0);
            _server?.SendUnconnected(endpoint, writer.CopyData());
            return;
        }
        
        var totalChunks = (logoData.Length + CHUNK_SIZE - 1) / CHUNK_SIZE;
        
        for (int i = 0; i < totalChunks; i++)
        {
            var offset = i * CHUNK_SIZE;
            var length = Math.Min(CHUNK_SIZE, logoData.Length - offset);
            
            var writer = new NetDataWriter();
            writer.Put("LOGO_RESPONSE");
            writer.Put(logoData.Length);
            writer.Put(i);
            writer.Put(totalChunks);
            writer.Put(length);
            writer.Put(logoData, offset, length);
            
            _server?.SendUnconnected(endpoint, writer.CopyData());
            System.Threading.Thread.Sleep(5);
        }
        
        Console.WriteLine($"[Server] Sent logo in {totalChunks} chunks to {endpoint}");
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
            
            var logoData = GetServerLogo();
            if (logoData != null && logoData.Length > 0)
            {
                _writer.Put(true);
                _writer.Put(logoData.Length);
                _writer.Put(logoData);
            }
            else
            {
                _writer.Put(false);
            }
            
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
    
    public void OnConnectionRequest(NetConnectionRequest request)
    {
        if (PlayerCount >= _config.MaxPlayers)
        {
            request.Reject();
            Console.WriteLine($"[Server] Connection rejected (server full): {request.RemoteEndPoint}");
            return;
        }
        
        if (request.Key == _config.GameKey)
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

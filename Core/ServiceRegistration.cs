// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using DuckovTogether.Core.Sync;
using DuckovTogether.Core.GameLogic;
using DuckovTogether.Net;
using DuckovNet;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Core;

public static class ServiceRegistration
{
    private static HeadlessNetService? _netService;
    private static ILogger? _logger;
    
    public static HeadlessNetService NetService => _netService ?? throw new InvalidOperationException("NetService not initialized");
    public static ILogger Logger => _logger ?? new ConsoleLogger();
    
    public static void RegisterAllServices(HeadlessNetService netService)
    {
        _netService = netService;
        _logger = new ConsoleLogger();
        
        var container = ServiceContainer.Instance;
        container.Register<ILogger>(_logger);
        container.Register<INetService>(new NetServiceAdapter(netService));
        
        DuckovTogetherServer.Core.Logging.Log.Info("All services registered");
    }
    
    public static void Log(string message) => Logger.Log(message);
    public static void LogWarning(string message) => Logger.LogWarning(message);
    public static void LogError(string message) => Logger.LogError(message);
    public static void LogError(string message, Exception ex) => Logger.LogError(message, ex);
}

public class NetServiceAdapter : INetService
{
    private readonly HeadlessNetService _netService;
    
    public NetServiceAdapter(HeadlessNetService netService)
    {
        _netService = netService;
    }
    
    public bool IsRunning => _netService.IsRunning;
    public int PlayerCount => _netService.PlayerCount;
    
    public void Start(int port, string key) => _netService.Start(port, key);
    public void Stop() => _netService.Stop();
    public void Poll() => _netService.Poll();
    
    public void SendToAll(byte[] data, DeliveryMethod method)
    {
        var writer = new NetDataWriter();
        writer.Put(data);
        _netService.SendToAll(writer, method);
    }
    
    public void SendToPeer(int peerId, byte[] data, DeliveryMethod method)
    {
        var peer = _netService.GetPeer(peerId);
        if (peer != null)
        {
            var writer = new NetDataWriter();
            writer.Put(data);
            _netService.SendToPeer(peer, writer, method);
        }
    }
    
    public void DisconnectPeer(int peerId, string reason)
    {
        _netService.DisconnectPeer(peerId, reason);
    }
    
    public NetPeer? GetPeer(int peerId) => _netService.GetPeer(peerId);
    
    public IEnumerable<int> GetAllPeerIds() => _netService.GetAllPeerIds();
}

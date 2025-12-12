// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using DuckovNet;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Net;

public class MessageQueue
{
    private static MessageQueue? _instance;
    public static MessageQueue Instance => _instance ??= new MessageQueue();
    
    private readonly ConcurrentQueue<QueuedMessage> _highPriority = new();
    private readonly ConcurrentQueue<QueuedMessage> _normalPriority = new();
    private readonly ConcurrentQueue<QueuedMessage> _lowPriority = new();
    
    private readonly NetDataWriter _writer = new();
    private HeadlessNetService? _netService;
    
    private const int MAX_MESSAGES_PER_TICK = 100;
    private const int MAX_BYTES_PER_TICK = 65536;
    
    public int HighPriorityCount => _highPriority.Count;
    public int NormalPriorityCount => _normalPriority.Count;
    public int LowPriorityCount => _lowPriority.Count;
    public int TotalQueued => HighPriorityCount + NormalPriorityCount + LowPriorityCount;
    
    public long TotalMessagesSent { get; private set; }
    public long TotalBytesSent { get; private set; }
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Log.Info("MessageQueue initialized");
    }
    
    public void EnqueueBroadcast(byte[] data, MessagePriority priority = MessagePriority.Normal, int? excludePeerId = null)
    {
        var msg = new QueuedMessage
        {
            Data = data,
            IsBroadcast = true,
            ExcludePeerId = excludePeerId,
            DeliveryMethod = priority == MessagePriority.High ? DeliveryMethod.ReliableOrdered : DeliveryMethod.ReliableUnordered
        };
        
        EnqueueByPriority(msg, priority);
    }
    
    public void EnqueueToPlayer(int peerId, byte[] data, MessagePriority priority = MessagePriority.Normal)
    {
        var msg = new QueuedMessage
        {
            Data = data,
            TargetPeerId = peerId,
            IsBroadcast = false,
            DeliveryMethod = priority == MessagePriority.High ? DeliveryMethod.ReliableOrdered : DeliveryMethod.ReliableUnordered
        };
        
        EnqueueByPriority(msg, priority);
    }
    
    private void EnqueueByPriority(QueuedMessage msg, MessagePriority priority)
    {
        switch (priority)
        {
            case MessagePriority.High:
                _highPriority.Enqueue(msg);
                break;
            case MessagePriority.Low:
                _lowPriority.Enqueue(msg);
                break;
            default:
                _normalPriority.Enqueue(msg);
                break;
        }
    }
    
    public void ProcessQueue()
    {
        if (_netService?.NetManager == null) return;
        
        int messageCount = 0;
        int byteCount = 0;
        
        while (messageCount < MAX_MESSAGES_PER_TICK && byteCount < MAX_BYTES_PER_TICK)
        {
            QueuedMessage msg;
            
            if (_highPriority.TryDequeue(out msg) ||
                _normalPriority.TryDequeue(out msg) ||
                _lowPriority.TryDequeue(out msg))
            {
                SendMessage(msg);
                messageCount++;
                byteCount += msg.Data.Length;
                TotalMessagesSent++;
                TotalBytesSent += msg.Data.Length;
            }
            else
            {
                break;
            }
        }
    }
    
    private void SendMessage(QueuedMessage msg)
    {
        if (_netService?.NetManager == null) return;
        
        _writer.Reset();
        _writer.Put(msg.Data);
        
        if (msg.IsBroadcast)
        {
            foreach (var peer in _netService.NetManager.ConnectedPeerList)
            {
                if (msg.ExcludePeerId.HasValue && peer.Id == msg.ExcludePeerId.Value)
                    continue;
                    
                peer.Send(_writer, msg.DeliveryMethod);
            }
        }
        else if (msg.TargetPeerId.HasValue)
        {
            foreach (var peer in _netService.NetManager.ConnectedPeerList)
            {
                if (peer.Id == msg.TargetPeerId.Value)
                {
                    peer.Send(_writer, msg.DeliveryMethod);
                    break;
                }
            }
        }
    }
    
    public void Clear()
    {
        while (_highPriority.TryDequeue(out _)) { }
        while (_normalPriority.TryDequeue(out _)) { }
        while (_lowPriority.TryDequeue(out _)) { }
    }
}

public enum MessagePriority
{
    High,
    Normal,
    Low
}

public struct QueuedMessage
{
    public byte[] Data;
    public int? TargetPeerId;
    public int? ExcludePeerId;
    public bool IsBroadcast;
    public DeliveryMethod DeliveryMethod;
}

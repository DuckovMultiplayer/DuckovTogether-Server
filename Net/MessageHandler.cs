// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;
using DuckovTogether.Core.Sync;
using DuckovTogether.Core.GameLogic;

namespace DuckovTogether.Net;

public enum MessageType : byte
{
    ClientStatus = 1,
    PlayerPosition = 2,
    PlayerAnimation = 3,
    ChatMessage = 4,
    SceneVote = 5,
    PlayerEquipment = 6,
    PlayerHealth = 7,
    JsonMessage = 9,
    AISync = 10,
    AIHealth = 11,
    AIAnimation = 12,
    LootSync = 20,
    ItemPickup = 21,
    ItemDrop = 22,
    ItemTransfer = 23,
    ContainerOpen = 24,
    WeaponFire = 30,
    WeaponReload = 31,
    WeaponSwitch = 32,
    GrenadeThrow = 33,
    MeleeAttack = 34,
    Damage = 40,
    DoorInteract = 50,
    SwitchInteract = 51,
    ExtractStart = 52,
    SetId = 100,
    Kick = 101
}

public class SetIdData
{
    public string type { get; set; } = "setId";
    public string networkId { get; set; } = "";
    public string timestamp { get; set; } = "";
}

public class BaseJsonMessage
{
    public string type { get; set; } = "";
}

public class PlayerListData
{
    public string type { get; set; } = "playerList";
    public List<PlayerListItem> players { get; set; } = new();
}

public class PlayerListItem
{
    public int peerId { get; set; }
    public string endPoint { get; set; } = "";
    public string playerName { get; set; } = "";
    public bool isInGame { get; set; }
    public string sceneId { get; set; } = "";
    public int latency { get; set; }
}

public class MessageHandler
{
    private readonly HeadlessNetService _netService;
    private readonly NetDataWriter _writer = new();
    private DateTime _lastPlayerListBroadcast = DateTime.MinValue;
    private const double PLAYER_LIST_INTERVAL = 2.0;
    
    public MessageHandler(HeadlessNetService netService)
    {
        _netService = netService;
        _netService.OnDataReceived += HandleMessage;
        _netService.OnPlayerConnected += OnPlayerConnected;
        _netService.OnPlayerDisconnected += OnPlayerDisconnected;
        
        MessageQueue.Instance.Initialize(netService);
        SyncManager.Instance.Initialize(netService);
        PlayerSyncManager.Instance.Initialize(netService);
        CombatSyncManager.Instance.Initialize(netService);
        WorldSyncManager.Instance.Initialize(netService);
        ItemSyncManager.Instance.Initialize(netService);
        
        AIManager.Instance.OnAIAttack += OnAIAttackPlayer;
        AIManager.Instance.OnAIDeath += OnAIDeathHandler;
    }
    
    private void OnAIAttackPlayer(int entityId, int targetPlayerId, float damage)
    {
        CombatSyncManager.Instance.OnPlayerDamage(targetPlayerId, entityId, damage, "ai_attack", 
            System.Numerics.Vector3.Zero);
    }
    
    private void OnAIDeathHandler(int entityId)
    {
        SyncManager.Instance.BroadcastAIDeath(entityId);
    }
    
    private DateTime _lastDeltaSync = DateTime.Now;
    private const double DELTA_SYNC_INTERVAL = 0.05;
    
    public void Update()
    {
        SyncManager.Instance.Update();
        PlayerSyncManager.Instance.Update();
        WorldSyncManager.Instance.Update(0.016f);
        
        MessageQueue.Instance.ProcessQueue();
        
        var now = DateTime.Now;
        if ((now - _lastDeltaSync).TotalSeconds >= DELTA_SYNC_INTERVAL)
        {
            _lastDeltaSync = now;
            BroadcastDeltaSync();
        }
    }
    
    private void BroadcastDeltaSync()
    {
        var dirtyPlayers = DeltaSyncManager.Instance.GetDirtyPlayers();
        if (dirtyPlayers.Count > 0)
        {
            var packet = new DeltaSyncPacket
            {
                type = "delta_sync",
                players = dirtyPlayers,
                ai = new List<AIDeltaPacket>()
            };
            
            var json = JsonConvert.SerializeObject(packet);
            var data = CreateJsonMessage(json);
            MessageQueue.Instance.EnqueueBroadcast(data, MessagePriority.Normal);
        }
        
        var dirtyAI = DeltaSyncManager.Instance.GetDirtyAI();
        if (dirtyAI.Count > 0)
        {
            var packet = new DeltaSyncPacket
            {
                type = "delta_sync",
                players = new List<PlayerDeltaPacket>(),
                ai = dirtyAI
            };
            
            var json = JsonConvert.SerializeObject(packet);
            var data = CreateJsonMessage(json);
            MessageQueue.Instance.EnqueueBroadcast(data, MessagePriority.Normal);
        }
    }
    
    private byte[] CreateJsonMessage(string json)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var data = new byte[1 + jsonBytes.Length];
        data[0] = (byte)MessageType.JsonMessage;
        Buffer.BlockCopy(jsonBytes, 0, data, 1, jsonBytes.Length);
        return data;
    }
    
    private void BroadcastPlayerList()
    {
        if (_netService.PlayerCount == 0) return;
        
        var playerList = new PlayerListData();
        foreach (var player in _netService.GetAllPlayers())
        {
            playerList.players.Add(new PlayerListItem
            {
                peerId = player.PeerId,
                endPoint = player.EndPoint,
                playerName = player.PlayerName,
                isInGame = player.IsInGame,
                sceneId = player.SceneId,
                latency = player.Latency
            });
        }
        
        var json = JsonConvert.SerializeObject(playerList);
        _writer.Reset();
        _writer.Put((byte)MessageType.JsonMessage);
        _writer.Put(json);
        _netService.SendToAll(_writer);
    }
    
    private void OnPlayerDisconnected(int peerId, DisconnectReason reason)
    {
        PlayerSyncManager.Instance.OnPlayerDisconnected(peerId);
        BroadcastPlayerList();
    }
    
    private void OnPlayerConnected(int peerId, Core.PlayerState state)
    {
        SendSetId(peerId, state.EndPoint);
    }
    
    private void SendSetId(int peerId, string endPoint)
    {
        var setIdData = new SetIdData
        {
            networkId = endPoint,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        var json = JsonConvert.SerializeObject(setIdData);
        
        _writer.Reset();
        _writer.Put((byte)MessageType.JsonMessage);
        _writer.Put(json);
        
        var peer = GetPeerById(peerId);
        if (peer != null)
        {
            _netService.SendToPeer(peer, _writer);
            Console.WriteLine($"[MessageHandler] Sent SetId to peer {peerId}: {endPoint}");
        }
    }
    
    private NetPeer? GetPeerById(int peerId)
    {
        if (_netService.NetManager == null) return null;
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            if (peer.Id == peerId) return peer;
        }
        return null;
    }
    
    private void HandleMessage(int peerId, NetPacketReader reader, byte channel)
    {
        if (reader.AvailableBytes < 1) return;
        
        var msgType = (MessageType)reader.GetByte();
        
        switch (msgType)
        {
            case MessageType.ClientStatus:
                HandleClientStatus(peerId, reader);
                break;
            case MessageType.PlayerPosition:
                HandlePlayerPosition(peerId, reader);
                break;
            case MessageType.PlayerAnimation:
                HandlePlayerAnimation(peerId, reader);
                break;
            case MessageType.PlayerEquipment:
                HandlePlayerEquipment(peerId, reader);
                break;
            case MessageType.ChatMessage:
                HandleChatMessage(peerId, reader);
                break;
            case MessageType.JsonMessage:
                HandleJsonMessage(peerId, reader, channel);
                break;
            case MessageType.WeaponFire:
                HandleWeaponFire(peerId, reader);
                break;
            case MessageType.Damage:
                HandleDamage(peerId, reader);
                break;
            case MessageType.ItemPickup:
                HandleItemPickup(peerId, reader);
                break;
            case MessageType.ItemDrop:
                HandleItemDrop(peerId, reader);
                break;
            case MessageType.DoorInteract:
                HandleDoorInteract(peerId, reader);
                break;
            default:
                BroadcastRawMessage(peerId, reader, channel);
                break;
        }
    }
    
    private void HandlePlayerAnimation(int peerId, NetPacketReader reader)
    {
        try
        {
            var speed = reader.GetFloat();
            var dirX = reader.GetFloat();
            var dirY = reader.GetFloat();
            var hand = reader.GetInt();
            var gunReady = reader.GetBool();
            var dashing = reader.GetBool();
            var reloading = reader.GetBool();
            
            PlayerSyncManager.Instance.UpdatePlayerAnimation(peerId, speed, dirX, dirY, hand, gunReady, dashing, reloading);
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void HandlePlayerEquipment(int peerId, NetPacketReader reader)
    {
        try
        {
            var weaponId = reader.GetInt();
            var armorId = reader.GetInt();
            var helmetId = reader.GetInt();
            var hotbarCount = reader.GetInt();
            var hotbar = new List<int>();
            for (int i = 0; i < Math.Min(hotbarCount, 10); i++)
            {
                hotbar.Add(reader.GetInt());
            }
            
            PlayerSyncManager.Instance.UpdatePlayerEquipment(peerId, weaponId, armorId, helmetId, hotbar);
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void HandleWeaponFire(int peerId, NetPacketReader reader)
    {
        try
        {
            var weaponId = reader.GetInt();
            var ox = reader.GetFloat();
            var oy = reader.GetFloat();
            var oz = reader.GetFloat();
            var dx = reader.GetFloat();
            var dy = reader.GetFloat();
            var dz = reader.GetFloat();
            var ammoType = reader.GetInt();
            
            CombatSyncManager.Instance.OnWeaponFire(peerId, weaponId, 
                new System.Numerics.Vector3(ox, oy, oz),
                new System.Numerics.Vector3(dx, dy, dz), ammoType);
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void HandleDamage(int peerId, NetPacketReader reader)
    {
        try
        {
            var targetType = reader.GetInt();
            var targetId = reader.GetInt();
            var damage = reader.GetFloat();
            var damageType = reader.GetString();
            var hx = reader.GetFloat();
            var hy = reader.GetFloat();
            var hz = reader.GetFloat();
            
            var hitPoint = new System.Numerics.Vector3(hx, hy, hz);
            
            if (targetType == 0)
            {
                CombatSyncManager.Instance.OnPlayerDamage(targetId, peerId, damage, damageType, hitPoint);
            }
            else
            {
                CombatSyncManager.Instance.OnAIDamage(targetId, peerId, damage, damageType, hitPoint);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void HandleItemPickup(int peerId, NetPacketReader reader)
    {
        try
        {
            var containerId = reader.GetInt();
            var slotIndex = reader.GetInt();
            var itemTypeId = reader.GetInt();
            var count = reader.GetInt();
            
            ItemSyncManager.Instance.OnItemPickup(peerId, containerId, slotIndex, itemTypeId, count);
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void HandleItemDrop(int peerId, NetPacketReader reader)
    {
        try
        {
            var itemTypeId = reader.GetInt();
            var count = reader.GetInt();
            var x = reader.GetFloat();
            var y = reader.GetFloat();
            var z = reader.GetFloat();
            
            ItemSyncManager.Instance.OnItemDrop(peerId, itemTypeId, count, new System.Numerics.Vector3(x, y, z));
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void HandleDoorInteract(int peerId, NetPacketReader reader)
    {
        try
        {
            var doorId = reader.GetInt();
            var isOpen = reader.GetBool();
            
            WorldSyncManager.Instance.OnDoorInteract(doorId, isOpen, peerId);
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void HandleJsonMessage(int peerId, NetPacketReader reader, byte channel)
    {
        try
        {
            var json = reader.GetString();
            var baseMsg = JsonConvert.DeserializeObject<BaseJsonMessage>(json);
            if (baseMsg == null) return;
            
            var peer = GetPeerById(peerId);
            
            var requestTypes = new[] { 
                "sceneVoteRequest", "sceneVoteReady", "updateClientStatus",
                "lootRequest", "itemDropRequest", "itemPickupRequest",
                "damageReport", "ai_health_report", "clientStatus"
            };
            
            if (requestTypes.Contains(baseMsg.type))
            {
                ClientRequestHandler.Instance.HandleJsonRequest(peerId, json, peer!);
                
                if (baseMsg.type == "clientStatus" || baseMsg.type == "updateClientStatus")
                {
                    HandleJsonClientStatus(peerId, json);
                }
            }
            else
            {
                BroadcastJsonMessage(peerId, json, channel);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] HandleJsonMessage: {ex.Message}");
        }
    }
    
    private void HandleJsonClientStatus(int peerId, string json)
    {
        try
        {
            dynamic data = JsonConvert.DeserializeObject(json)!;
            var player = _netService.GetPlayer(peerId);
            if (player != null)
            {
                player.PlayerName = (string?)data.playerName ?? player.PlayerName;
                player.IsInGame = (bool?)data.isInGame ?? false;
                player.SceneId = (string?)data.sceneId ?? "";
                player.LastUpdate = DateTime.Now;
                Console.WriteLine($"[JsonStatus] {player.PlayerName} - InGame: {player.IsInGame}, Scene: {player.SceneId}");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[MessageHandler] Parse error: {ex.Message}"); }
    }
    
    private void BroadcastJsonMessage(int senderPeerId, string json, byte channel)
    {
        if (_netService.NetManager == null) return;
        
        _writer.Reset();
        _writer.Put((byte)MessageType.JsonMessage);
        _writer.Put(json);
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            if (peer.Id != senderPeerId)
            {
                peer.Send(_writer, channel, DeliveryMethod.ReliableOrdered);
            }
        }
    }
    
    private void HandleClientStatus(int peerId, NetPacketReader reader)
    {
        try
        {
            var playerName = reader.GetString();
            var isInGame = reader.GetBool();
            var sceneId = reader.GetString();
            
            var player = _netService.GetPlayer(peerId);
            if (player != null)
            {
                player.PlayerName = playerName;
                player.IsInGame = isInGame;
                player.SceneId = sceneId;
                player.LastUpdate = DateTime.Now;
                
                Console.WriteLine($"[Status] {playerName} - InGame: {isInGame}, Scene: {sceneId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] HandleClientStatus: {ex.Message}");
        }
    }
    
    private void HandlePlayerPosition(int peerId, NetPacketReader reader)
    {
        try
        {
            var x = reader.GetFloat();
            var y = reader.GetFloat();
            var z = reader.GetFloat();
            
            var player = _netService.GetPlayer(peerId);
            if (player != null)
            {
                player.Position = new System.Numerics.Vector3(x, y, z);
                player.LastUpdate = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] HandlePlayerPosition: {ex.Message}");
        }
    }
    
    private void HandleChatMessage(int peerId, NetPacketReader reader)
    {
        try
        {
            var message = reader.GetString();
            var player = _netService.GetPlayer(peerId);
            var playerName = player?.PlayerName ?? $"Player_{peerId}";
            
            Console.WriteLine($"[Chat] {playerName}: {message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] HandleChatMessage: {ex.Message}");
        }
    }
    
    private void BroadcastRawMessage(int senderPeerId, NetPacketReader reader, byte channel)
    {
        if (_netService.NetManager == null) return;
        
        reader.SetPosition(0);
        var data = reader.GetRemainingBytes();
        
        _writer.Reset();
        _writer.Put(data);
        
        var deliveryMethod = channel switch
        {
            0 => DeliveryMethod.ReliableOrdered,
            1 => DeliveryMethod.ReliableOrdered,
            2 => DeliveryMethod.ReliableUnordered,
            3 => DeliveryMethod.Unreliable,
            _ => DeliveryMethod.ReliableOrdered
        };
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            if (peer.Id != senderPeerId)
            {
                peer.Send(_writer, channel, deliveryMethod);
            }
        }
    }
}

public class DeltaSyncPacket
{
    public string type { get; set; } = "delta_sync";
    public List<PlayerDeltaPacket> players { get; set; } = new();
    public List<AIDeltaPacket> ai { get; set; } = new();
}

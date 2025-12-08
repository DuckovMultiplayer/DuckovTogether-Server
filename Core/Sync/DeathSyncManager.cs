// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System.Numerics;
using DuckovTogether.Core.GameLogic;
using DuckovTogether.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class DeathSyncManager
{
    private static DeathSyncManager? _instance;
    public static DeathSyncManager Instance => _instance ??= new DeathSyncManager();
    
    private HeadlessNetService? _netService;
    private readonly NetDataWriter _writer = new();
    private int _nextLootUid = 10000;
    
    private readonly Dictionary<int, DeadLootBoxState> _deadLootBoxes = new();
    private readonly Dictionary<int, PlayerDeathState> _playerDeaths = new();
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Console.WriteLine("[DeathSync] Initialized");
    }
    
    public void OnPlayerDeath(int peerId, int killerId, string cause, Vector3 deathPosition)
    {
        Console.WriteLine($"[DeathSync] Player {peerId} killed by {killerId}, cause: {cause}");
        
        var lootUid = _nextLootUid++;
        var deathState = new PlayerDeathState
        {
            PeerId = peerId,
            KillerId = killerId,
            Cause = cause,
            DeathPosition = deathPosition,
            DeathTime = DateTime.Now,
            LootUid = lootUid
        };
        _playerDeaths[peerId] = deathState;
        
        var deathData = new PlayerDeathMessage
        {
            type = "playerDeath",
            peerId = peerId,
            killerId = killerId,
            cause = cause,
            position = new Vec3 { x = deathPosition.X, y = deathPosition.Y, z = deathPosition.Z },
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        BroadcastJson(deathData);
        
        SpawnDeadLootBox(peerId, lootUid, deathPosition);
        
        PlayerSyncManager.Instance.OnPlayerDeath(peerId, killerId, cause);
    }
    
    public void OnAIDeath(int aiId, int killerId, Vector3 deathPosition, string aiType)
    {
        Console.WriteLine($"[DeathSync] AI {aiId} ({aiType}) killed by {killerId}");
        
        var lootUid = _nextLootUid++;
        
        var lootBox = new DeadLootBoxState
        {
            LootUid = lootUid,
            EntityId = aiId,
            EntityType = "AI",
            AIType = aiType,
            Position = deathPosition,
            Rotation = Vector3.Zero,
            SpawnTime = DateTime.Now,
            Items = GenerateLootForAI(aiType)
        };
        _deadLootBoxes[lootUid] = lootBox;
        
        var spawnData = new DeadLootSpawnMessage
        {
            type = "deadLootSpawn",
            aiId = aiId,
            lootUid = lootUid,
            position = new Vec3 { x = deathPosition.X, y = deathPosition.Y, z = deathPosition.Z },
            rotation = new Vec3 { x = 0, y = 0, z = 0 },
            aiType = aiType,
            items = lootBox.Items.Select(i => new LootItemData
            {
                slot = i.Slot,
                itemId = i.ItemId,
                count = i.Count
            }).ToArray(),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(spawnData);
        
        AIManager.Instance.RemoveEntity(aiId);
    }
    
    private void SpawnDeadLootBox(int peerId, int lootUid, Vector3 position)
    {
        var player = _netService?.GetPlayer(peerId);
        var playerName = player?.PlayerName ?? $"Player_{peerId}";
        
        var lootBox = new DeadLootBoxState
        {
            LootUid = lootUid,
            EntityId = peerId,
            EntityType = "Player",
            AIType = "",
            Position = position,
            Rotation = Vector3.Zero,
            SpawnTime = DateTime.Now,
            Items = new List<LootItemState>()
        };
        _deadLootBoxes[lootUid] = lootBox;
        
        var spawnData = new DeadLootSpawnMessage
        {
            type = "deadLootSpawn",
            aiId = 0,
            lootUid = lootUid,
            position = new Vec3 { x = position.X, y = position.Y, z = position.Z },
            rotation = new Vec3 { x = 0, y = 0, z = 0 },
            aiType = "Player",
            playerName = playerName,
            items = Array.Empty<LootItemData>(),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(spawnData);
        Console.WriteLine($"[DeathSync] Spawned player loot box: lootUid={lootUid}, player={playerName}");
    }
    
    private List<LootItemState> GenerateLootForAI(string aiType)
    {
        var items = new List<LootItemState>();
        var random = new Random();
        
        switch (aiType.ToLower())
        {
            case "scav":
                if (random.Next(100) < 60)
                    items.Add(new LootItemState { Slot = 0, ItemId = "ammo_9mm", Count = random.Next(5, 20) });
                if (random.Next(100) < 40)
                    items.Add(new LootItemState { Slot = 1, ItemId = "med_bandage", Count = 1 });
                if (random.Next(100) < 30)
                    items.Add(new LootItemState { Slot = 2, ItemId = "food_bread", Count = 1 });
                break;
                
            case "pmc":
                if (random.Next(100) < 80)
                    items.Add(new LootItemState { Slot = 0, ItemId = "ammo_545", Count = random.Next(10, 30) });
                if (random.Next(100) < 60)
                    items.Add(new LootItemState { Slot = 1, ItemId = "med_ifak", Count = 1 });
                if (random.Next(100) < 40)
                    items.Add(new LootItemState { Slot = 2, ItemId = "gear_vest", Count = 1 });
                break;
                
            case "boss":
                items.Add(new LootItemState { Slot = 0, ItemId = "ammo_762", Count = random.Next(20, 60) });
                items.Add(new LootItemState { Slot = 1, ItemId = "med_salewa", Count = 1 });
                items.Add(new LootItemState { Slot = 2, ItemId = "key_rare", Count = 1 });
                break;
                
            default:
                if (random.Next(100) < 50)
                    items.Add(new LootItemState { Slot = 0, ItemId = "ammo_9mm", Count = random.Next(5, 15) });
                break;
        }
        
        return items;
    }
    
    public void OnPlayerRespawn(int peerId, Vector3 spawnPosition)
    {
        Console.WriteLine($"[DeathSync] Player {peerId} respawning at {spawnPosition}");
        
        _playerDeaths.Remove(peerId);
        
        PlayerSyncManager.Instance.OnPlayerRespawn(peerId, spawnPosition);
    }
    
    public void SendDeadLootBoxesToPlayer(int peerId)
    {
        foreach (var box in _deadLootBoxes.Values)
        {
            var spawnData = new DeadLootSpawnMessage
            {
                type = "deadLootSpawn",
                aiId = box.EntityType == "AI" ? box.EntityId : 0,
                lootUid = box.LootUid,
                position = new Vec3 { x = box.Position.X, y = box.Position.Y, z = box.Position.Z },
                rotation = new Vec3 { x = box.Rotation.X, y = box.Rotation.Y, z = box.Rotation.Z },
                aiType = box.AIType,
                items = box.Items.Select(i => new LootItemData
                {
                    slot = i.Slot,
                    itemId = i.ItemId,
                    count = i.Count
                }).ToArray(),
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };
            
            SendJsonToPeer(peerId, spawnData);
        }
    }
    
    public DeadLootBoxState? GetLootBox(int lootUid)
    {
        return _deadLootBoxes.TryGetValue(lootUid, out var box) ? box : null;
    }
    
    public void RemoveLootBox(int lootUid)
    {
        _deadLootBoxes.Remove(lootUid);
    }
    
    public void ClearSceneLootBoxes(string sceneId)
    {
        _deadLootBoxes.Clear();
        _playerDeaths.Clear();
        Console.WriteLine($"[DeathSync] Cleared all loot boxes for scene change: {sceneId}");
    }
    
    private void BroadcastJson(object data)
    {
        if (_netService?.NetManager == null) return;
        
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }
    }
    
    private void SendJsonToPeer(int peerId, object data)
    {
        if (_netService?.NetManager == null) return;
        
        var peer = _netService.NetManager.ConnectedPeerList.FirstOrDefault(p => p.Id == peerId);
        if (peer == null) return;
        
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
}

public class DeadLootBoxState
{
    public int LootUid { get; set; }
    public int EntityId { get; set; }
    public string EntityType { get; set; } = "";
    public string AIType { get; set; } = "";
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public DateTime SpawnTime { get; set; }
    public List<LootItemState> Items { get; set; } = new();
}

public class LootItemState
{
    public int Slot { get; set; }
    public string ItemId { get; set; } = "";
    public int Count { get; set; }
}

public class PlayerDeathState
{
    public int PeerId { get; set; }
    public int KillerId { get; set; }
    public string Cause { get; set; } = "";
    public Vector3 DeathPosition { get; set; }
    public DateTime DeathTime { get; set; }
    public int LootUid { get; set; }
}

public class PlayerDeathMessage
{
    public string type { get; set; } = "playerDeath";
    public int peerId { get; set; }
    public int killerId { get; set; }
    public string cause { get; set; } = "";
    public Vec3 position { get; set; } = new();
    public string timestamp { get; set; } = "";
}

public class DeadLootSpawnMessage
{
    public string type { get; set; } = "deadLootSpawn";
    public int aiId { get; set; }
    public int lootUid { get; set; }
    public Vec3 position { get; set; } = new();
    public Vec3 rotation { get; set; } = new();
    public string aiType { get; set; } = "";
    public string playerName { get; set; } = "";
    public LootItemData[] items { get; set; } = Array.Empty<LootItemData>();
    public string timestamp { get; set; } = "";
}

public class LootItemData
{
    public int slot { get; set; }
    public string itemId { get; set; } = "";
    public int count { get; set; }
}

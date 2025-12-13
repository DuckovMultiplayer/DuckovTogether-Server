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
using DuckovNet;
using DuckovTogetherServer.Core.Logging;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class SyncManager
{
    private static SyncManager? _instance;
    public static SyncManager Instance => _instance ??= new SyncManager();
    
    private HeadlessNetService? _netService;
    private readonly NetDataWriter _writer = new();
    
    private DateTime _lastAISync = DateTime.Now;
    private DateTime _lastLootSync = DateTime.Now;
    private DateTime _lastPlayerSync = DateTime.Now;
    
    private const double AI_SYNC_INTERVAL = 0.1;
    private const double LOOT_SYNC_INTERVAL = 1.0;
    private const double PLAYER_SYNC_INTERVAL = 0.5;
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Log.Info("SyncManager initialized");
    }
    
    public void Update()
    {
        var now = DateTime.Now;
        
        if ((now - _lastAISync).TotalSeconds >= AI_SYNC_INTERVAL)
        {
            BroadcastAIState();
            _lastAISync = now;
        }
        
        if ((now - _lastLootSync).TotalSeconds >= LOOT_SYNC_INTERVAL)
        {
            _lastLootSync = now;
        }
        
        if ((now - _lastPlayerSync).TotalSeconds >= PLAYER_SYNC_INTERVAL)
        {
            BroadcastPlayerList();
            _lastPlayerSync = now;
        }
    }
    
    public void BroadcastAIState()
    {
        if (_netService?.NetManager == null) return;
        
        var currentScene = GameServer.Instance.Saves.CurrentWorld.CurrentScene;
        if (string.IsNullOrEmpty(currentScene)) return;
        
        var entities = AIManager.Instance.GetEntitiesInScene(currentScene).ToList();
        if (entities.Count == 0) return;
        
        var transformData = new AITransformSnapshot
        {
            type = "ai_transform_snapshot",
            transforms = entities.Select(e => new AITransformEntry
            {
                aiId = e.EntityId,
                position = new Vec3Data { x = e.Position.X, y = e.Position.Y, z = e.Position.Z },
                forward = new Vec3Data { x = e.Forward.X, y = e.Forward.Y, z = e.Forward.Z }
            }).ToList()
        };
        
        BroadcastJson(transformData);
        
        var animData = new AIAnimSnapshot
        {
            type = "ai_anim_snapshot",
            anims = entities.Select(e => new AIAnimEntry
            {
                aiId = e.EntityId,
                speed = e.Speed,
                dirX = e.DirX,
                dirY = e.DirY,
                hand = e.HandState,
                gunReady = e.GunReady,
                dashing = e.Dashing
            }).ToList()
        };
        
        BroadcastJson(animData);
    }
    
    public void BroadcastAIHealth(int aiId, float maxHealth, float currentHealth)
    {
        var data = new AIHealthSync
        {
            type = "ai_health_sync",
            aiId = aiId,
            maxHealth = maxHealth,
            currentHealth = currentHealth
        };
        
        BroadcastJson(data);
    }
    
    public void BroadcastAIDeath(int aiId)
    {
        BroadcastAIHealth(aiId, 100, 0);
    }
    
    public void SendLootFullSync(NetPeer peer, string sceneId)
    {
        var containers = ItemManager.Instance.GetContainersInScene(sceneId).ToList();
        
        var data = new LootFullSync
        {
            type = "lootFullSync",
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            lootBoxes = containers.Select(c => new LootBoxInfo
            {
                lootUid = c.ContainerId.GetHashCode(),
                position = new Vec3Data { x = c.Position.X, y = c.Position.Y, z = c.Position.Z },
                capacity = 10,
                items = c.Items.Select((item, idx) => new LootItemInfo
                {
                    position = idx,
                    typeId = item.ItemId.GetHashCode(),
                    stack = item.Count
                }).ToArray()
            }).ToArray()
        };
        
        SendJsonToPeer(peer, data);
        Log.Debug($"Sent loot sync to {peer.EndPoint}: {containers.Count} containers");
    }
    
    public void BroadcastPlayerList()
    {
        if (_netService == null) return;
        
        var players = _netService.GetAllPlayers().ToList();
        var data = new PlayerListSync
        {
            type = "playerList",
            players = players.Select(p => new PlayerListEntry
            {
                peerId = p.PeerId,
                endPoint = p.EndPoint,
                playerName = p.PlayerName,
                isInGame = p.IsInGame,
                sceneId = p.SceneId,
                latency = p.Latency
            }).ToList()
        };
        
        BroadcastJson(data);
    }
    
    public void BroadcastSceneVote(string sceneId, Dictionary<string, bool> votes)
    {
        var players = _netService?.GetAllPlayers().ToList() ?? new List<PlayerState>();
        var hostScene = GameServer.Instance?.Saves?.CurrentWorld?.CurrentScene ?? "";
        
        var data = new SceneVoteSync
        {
            type = "sceneVote",
            voteId = (int)(DateTime.Now.Ticks % 100000),
            active = !string.IsNullOrEmpty(sceneId),
            targetSceneId = sceneId,
            targetSceneDisplayName = sceneId,
            curtainGuid = "",
            locationName = "",
            notifyEvac = false,
            saveToFile = true,
            useLocation = false,
            hostSceneId = hostScene,
            playerList = new PlayerListData
            {
                items = votes.Select(kv => new PlayerInfoData
                {
                    playerId = kv.Key,
                    playerName = players.FirstOrDefault(p => p.PeerId.ToString() == kv.Key)?.PlayerName ?? $"Player_{kv.Key}",
                    steamId = "",
                    steamName = "",
                    ready = kv.Value
                }).ToArray()
            },
            totalPlayers = votes.Count,
            readyPlayers = votes.Count(v => v.Value),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        var json = JsonConvert.SerializeObject(data);
        Log.Info($"[VOTE] Broadcasting sceneVote: target={sceneId}, active={data.active}, players={votes.Count}, ready={data.readyPlayers}");
        Log.Debug($"[VOTE] JSON: {json}");
        
        if (_netService?.Server != null)
        {
            _writer.Reset();
            _writer.Put((byte)9);
            _writer.Put(json);
            _netService.SendToAll(_writer, DeliveryMethod.ReliableOrdered);
            Log.Info($"[VOTE] Sent to all players via DualProtocolServer");
        }
        else
        {
            BroadcastJson(data);
            Log.Warn("[VOTE] Fallback to NetManager broadcast");
        }
    }
    
    public void BroadcastForceSceneLoad(string sceneId)
    {
        var data = new ForceSceneLoad
        {
            type = "forceSceneLoad",
            sceneId = sceneId,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(data);
        Log.Info($"Broadcast force scene load: {sceneId}");
    }
    
    public void SendSetId(NetPeer peer, string networkId)
    {
        var data = new SetIdMessage
        {
            type = "setId",
            networkId = networkId,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        SendJsonToPeer(peer, data);
    }
    
    public void SendKick(NetPeer peer, string reason)
    {
        var data = new KickMessage
        {
            type = "kick",
            reason = reason,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        SendJsonToPeer(peer, data);
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
    
    private void SendJsonToPeer(NetPeer peer, object data)
    {
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
}

public class AITransformSnapshot
{
    public string type { get; set; } = "ai_transform_snapshot";
    public List<AITransformEntry> transforms { get; set; } = new();
}

public class AITransformEntry
{
    public int aiId { get; set; }
    public Vec3Data position { get; set; } = new();
    public Vec3Data forward { get; set; } = new();
}

public class Vec3Data
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public class AIAnimSnapshot
{
    public string type { get; set; } = "ai_anim_snapshot";
    public List<AIAnimEntry> anims { get; set; } = new();
}

public class AIAnimEntry
{
    public int aiId { get; set; }
    public float speed { get; set; }
    public float dirX { get; set; }
    public float dirY { get; set; }
    public int hand { get; set; }
    public bool gunReady { get; set; }
    public bool dashing { get; set; }
}

public class AIHealthSync
{
    public string type { get; set; } = "ai_health_sync";
    public int aiId { get; set; }
    public float maxHealth { get; set; }
    public float currentHealth { get; set; }
}

public class LootFullSync
{
    public string type { get; set; } = "lootFullSync";
    public string timestamp { get; set; } = "";
    public LootBoxInfo[] lootBoxes { get; set; } = Array.Empty<LootBoxInfo>();
}

public class LootBoxInfo
{
    public int lootUid { get; set; }
    public int aiId { get; set; }
    public Vec3Data position { get; set; } = new();
    public Vec3Data rotation { get; set; } = new();
    public int capacity { get; set; }
    public LootItemInfo[] items { get; set; } = Array.Empty<LootItemInfo>();
}

public class LootItemInfo
{
    public int position { get; set; }
    public int typeId { get; set; }
    public int stack { get; set; }
    public float durability { get; set; }
    public float durabilityLoss { get; set; }
    public bool inspected { get; set; }
}

public class PlayerListSync
{
    public string type { get; set; } = "playerList";
    public List<PlayerListEntry> players { get; set; } = new();
}

public class PlayerListEntry
{
    public int peerId { get; set; }
    public string endPoint { get; set; } = "";
    public string playerName { get; set; } = "";
    public bool isInGame { get; set; }
    public string sceneId { get; set; } = "";
    public int latency { get; set; }
}

public class SceneVoteSync
{
    public string type { get; set; } = "sceneVote";
    public int voteId { get; set; } = 1;
    public bool active { get; set; } = true;
    public string targetSceneId { get; set; } = "";
    public string targetSceneDisplayName { get; set; } = "";
    public string curtainGuid { get; set; } = "";
    public string locationName { get; set; } = "";
    public bool notifyEvac { get; set; }
    public bool saveToFile { get; set; } = true;
    public bool useLocation { get; set; }
    public string hostSceneId { get; set; } = "";
    public PlayerListData playerList { get; set; } = new();
    public int totalPlayers { get; set; }
    public int readyPlayers { get; set; }
    public string timestamp { get; set; } = "";
}

public class PlayerListData
{
    public PlayerInfoData[] items { get; set; } = Array.Empty<PlayerInfoData>();
}

public class PlayerInfoData
{
    public string playerId { get; set; } = "";
    public string playerName { get; set; } = "";
    public string steamId { get; set; } = "";
    public string steamName { get; set; } = "";
    public bool ready { get; set; }
}

public class VoteEntry
{
    public string playerId { get; set; } = "";
    public bool ready { get; set; }
}

public class ForceSceneLoad
{
    public string type { get; set; } = "forceSceneLoad";
    public string sceneId { get; set; } = "";
    public string timestamp { get; set; } = "";
}

public class SetIdMessage
{
    public string type { get; set; } = "setId";
    public string networkId { get; set; } = "";
    public string timestamp { get; set; } = "";
}

public class KickMessage
{
    public string type { get; set; } = "kick";
    public string reason { get; set; } = "";
    public string timestamp { get; set; } = "";
}

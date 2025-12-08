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
using DuckovTogether.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class PlayerSyncManager
{
    private static PlayerSyncManager? _instance;
    public static PlayerSyncManager Instance => _instance ??= new PlayerSyncManager();
    
    private HeadlessNetService? _netService;
    private readonly NetDataWriter _writer = new();
    private readonly Dictionary<int, PlayerSyncState> _playerStates = new();
    
    private DateTime _lastTransformSync = DateTime.Now;
    private DateTime _lastAnimSync = DateTime.Now;
    private DateTime _lastEquipmentSync = DateTime.Now;
    
    private const double TRANSFORM_SYNC_RATE = 0.05;
    private const double ANIM_SYNC_RATE = 0.1;
    private const double EQUIPMENT_SYNC_RATE = 0.5;
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Console.WriteLine("[PlayerSync] Initialized");
    }
    
    public void Update()
    {
        var now = DateTime.Now;
        
        if ((now - _lastTransformSync).TotalSeconds >= TRANSFORM_SYNC_RATE)
        {
            BroadcastPlayerTransforms();
            _lastTransformSync = now;
        }
        
        if ((now - _lastAnimSync).TotalSeconds >= ANIM_SYNC_RATE)
        {
            BroadcastPlayerAnimations();
            _lastAnimSync = now;
        }
        
        if ((now - _lastEquipmentSync).TotalSeconds >= EQUIPMENT_SYNC_RATE)
        {
            BroadcastPlayerEquipment();
            _lastEquipmentSync = now;
        }
    }
    
    public void UpdatePlayerTransform(int peerId, Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        if (!_playerStates.TryGetValue(peerId, out var state))
        {
            state = new PlayerSyncState { PeerId = peerId };
            _playerStates[peerId] = state;
        }
        
        state.Position = position;
        state.Rotation = rotation;
        state.Velocity = velocity;
        state.LastUpdate = DateTime.Now;
        
        DeltaSyncManager.Instance.UpdatePlayerState(peerId, position, rotation, velocity,
            state.CurrentHealth, state.WeaponId, state.Speed, state.DirX, state.DirY,
            state.HandState, state.GunReady, state.Dashing, state.Reloading);
    }
    
    public void UpdatePlayerAnimation(int peerId, float speed, float dirX, float dirY, int handState, bool gunReady, bool dashing, bool reloading)
    {
        if (!_playerStates.TryGetValue(peerId, out var state))
        {
            state = new PlayerSyncState { PeerId = peerId };
            _playerStates[peerId] = state;
        }
        
        state.Speed = speed;
        state.DirX = dirX;
        state.DirY = dirY;
        state.HandState = handState;
        state.GunReady = gunReady;
        state.Dashing = dashing;
        state.Reloading = reloading;
        
        DeltaSyncManager.Instance.UpdatePlayerState(peerId, state.Position, state.Rotation, state.Velocity,
            state.CurrentHealth, state.WeaponId, speed, dirX, dirY, handState, gunReady, dashing, reloading);
    }
    
    public void UpdatePlayerHealth(int peerId, float currentHealth, float maxHealth)
    {
        if (!_playerStates.TryGetValue(peerId, out var state))
        {
            state = new PlayerSyncState { PeerId = peerId };
            _playerStates[peerId] = state;
        }
        
        state.CurrentHealth = currentHealth;
        state.MaxHealth = maxHealth;
        
        BroadcastPlayerHealth(peerId, currentHealth, maxHealth);
    }
    
    public void UpdatePlayerEquipment(int peerId, int weaponId, int armorId, int helmetId, List<int> hotbarItems)
    {
        if (!_playerStates.TryGetValue(peerId, out var state))
        {
            state = new PlayerSyncState { PeerId = peerId };
            _playerStates[peerId] = state;
        }
        
        state.WeaponId = weaponId;
        state.ArmorId = armorId;
        state.HelmetId = helmetId;
        state.HotbarItems = hotbarItems;
    }
    
    public void OnPlayerJoined(int peerId, string playerName, Vector3 position, Vector3 rotation, string customFaceJson = "")
    {
        if (!_playerStates.ContainsKey(peerId))
        {
            _playerStates[peerId] = new PlayerSyncState
            {
                PeerId = peerId,
                Position = position,
                Rotation = rotation,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };
        }
        
        var data = new PlayerJoinSync
        {
            type = "playerJoin",
            peerId = peerId,
            playerName = playerName,
            position = new Vec3 { x = position.X, y = position.Y, z = position.Z },
            rotation = new Vec3 { x = rotation.X, y = rotation.Y, z = rotation.Z },
            customFaceJson = customFaceJson,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJsonExcept(peerId, data);
        Console.WriteLine($"[PlayerSync] Player joined: {playerName} (ID: {peerId})");
    }
    
    public void SendExistingPlayersToNewPlayer(int newPeerId)
    {
        foreach (var state in _playerStates.Values)
        {
            if (state.PeerId == newPeerId) continue;
            
            var player = _netService?.GetPlayer(state.PeerId);
            if (player == null) continue;
            
            var data = new PlayerJoinSync
            {
                type = "playerJoin",
                peerId = state.PeerId,
                playerName = player.PlayerName,
                position = new Vec3 { x = state.Position.X, y = state.Position.Y, z = state.Position.Z },
                rotation = new Vec3 { x = state.Rotation.X, y = state.Rotation.Y, z = state.Rotation.Z },
                customFaceJson = "",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };
            
            SendJsonToPeer(newPeerId, data);
        }
    }
    
    public void OnPlayerDisconnected(int peerId)
    {
        _playerStates.Remove(peerId);
        DeltaSyncManager.Instance.RemovePlayer(peerId);
        
        var data = new PlayerDisconnectSync
        {
            type = "playerDisconnect",
            peerId = peerId,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(data);
    }
    
    public void OnPlayerDeath(int peerId, int killerId, string cause)
    {
        var data = new PlayerDeathSync
        {
            type = "playerDeath",
            peerId = peerId,
            killerId = killerId,
            cause = cause,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(data);
    }
    
    public void OnPlayerRespawn(int peerId, Vector3 spawnPosition)
    {
        if (_playerStates.TryGetValue(peerId, out var state))
        {
            state.Position = spawnPosition;
            state.CurrentHealth = state.MaxHealth;
        }
        
        var data = new PlayerRespawnSync
        {
            type = "playerRespawn",
            peerId = peerId,
            position = new Vec3 { x = spawnPosition.X, y = spawnPosition.Y, z = spawnPosition.Z },
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(data);
    }
    
    private void BroadcastPlayerTransforms()
    {
        if (_netService?.NetManager == null || _playerStates.Count == 0) return;
        
        var transforms = new PlayerTransformSnapshot
        {
            type = "player_transform_snapshot",
            transforms = _playerStates.Values.Select(s => new PlayerTransformEntry
            {
                peerId = s.PeerId,
                position = new Vec3 { x = s.Position.X, y = s.Position.Y, z = s.Position.Z },
                rotation = new Vec3 { x = s.Rotation.X, y = s.Rotation.Y, z = s.Rotation.Z },
                velocity = new Vec3 { x = s.Velocity.X, y = s.Velocity.Y, z = s.Velocity.Z }
            }).ToList()
        };
        
        BroadcastJson(transforms);
    }
    
    private void BroadcastPlayerAnimations()
    {
        if (_netService?.NetManager == null || _playerStates.Count == 0) return;
        
        var anims = new PlayerAnimSnapshot
        {
            type = "player_anim_snapshot",
            anims = _playerStates.Values.Select(s => new PlayerAnimEntry
            {
                peerId = s.PeerId,
                speed = s.Speed,
                dirX = s.DirX,
                dirY = s.DirY,
                hand = s.HandState,
                gunReady = s.GunReady,
                dashing = s.Dashing,
                reloading = s.Reloading
            }).ToList()
        };
        
        BroadcastJson(anims);
    }
    
    private void BroadcastPlayerEquipment()
    {
        if (_netService?.NetManager == null || _playerStates.Count == 0) return;
        
        var equipment = new PlayerEquipmentSnapshot
        {
            type = "player_equipment_snapshot",
            equipment = _playerStates.Values.Select(s => new PlayerEquipmentEntry
            {
                peerId = s.PeerId,
                weaponId = s.WeaponId,
                armorId = s.ArmorId,
                helmetId = s.HelmetId,
                hotbar = s.HotbarItems.ToArray()
            }).ToList()
        };
        
        BroadcastJson(equipment);
    }
    
    private void BroadcastPlayerHealth(int peerId, float current, float max)
    {
        var data = new PlayerHealthSync
        {
            type = "playerHealth",
            peerId = peerId,
            currentHealth = current,
            maxHealth = max
        };
        
        BroadcastJson(data);
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
    
    private void BroadcastJsonExcept(int excludePeerId, object data)
    {
        if (_netService?.NetManager == null) return;
        
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            if (peer.Id != excludePeerId)
            {
                peer.Send(_writer, DeliveryMethod.ReliableOrdered);
            }
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

public class PlayerSyncState
{
    public int PeerId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Vector3 Velocity { get; set; }
    public float Speed { get; set; }
    public float DirX { get; set; }
    public float DirY { get; set; }
    public int HandState { get; set; }
    public bool GunReady { get; set; }
    public bool Dashing { get; set; }
    public bool Reloading { get; set; }
    public float CurrentHealth { get; set; } = 100f;
    public float MaxHealth { get; set; } = 100f;
    public int WeaponId { get; set; }
    public int ArmorId { get; set; }
    public int HelmetId { get; set; }
    public List<int> HotbarItems { get; set; } = new();
    public DateTime LastUpdate { get; set; }
}

public class PlayerTransformSnapshot
{
    public string type { get; set; } = "player_transform_snapshot";
    public List<PlayerTransformEntry> transforms { get; set; } = new();
}

public class PlayerTransformEntry
{
    public int peerId { get; set; }
    public Vec3 position { get; set; } = new();
    public Vec3 rotation { get; set; } = new();
    public Vec3 velocity { get; set; } = new();
}

public class Vec3
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public class PlayerAnimSnapshot
{
    public string type { get; set; } = "player_anim_snapshot";
    public List<PlayerAnimEntry> anims { get; set; } = new();
}

public class PlayerAnimEntry
{
    public int peerId { get; set; }
    public float speed { get; set; }
    public float dirX { get; set; }
    public float dirY { get; set; }
    public int hand { get; set; }
    public bool gunReady { get; set; }
    public bool dashing { get; set; }
    public bool reloading { get; set; }
}

public class PlayerEquipmentSnapshot
{
    public string type { get; set; } = "player_equipment_snapshot";
    public List<PlayerEquipmentEntry> equipment { get; set; } = new();
}

public class PlayerEquipmentEntry
{
    public int peerId { get; set; }
    public int weaponId { get; set; }
    public int armorId { get; set; }
    public int helmetId { get; set; }
    public int[] hotbar { get; set; } = Array.Empty<int>();
}

public class PlayerHealthSync
{
    public string type { get; set; } = "playerHealth";
    public int peerId { get; set; }
    public float currentHealth { get; set; }
    public float maxHealth { get; set; }
}

public class PlayerDisconnectSync
{
    public string type { get; set; } = "playerDisconnect";
    public int peerId { get; set; }
    public string timestamp { get; set; } = "";
}

public class PlayerDeathSync
{
    public string type { get; set; } = "playerDeath";
    public int peerId { get; set; }
    public int killerId { get; set; }
    public string cause { get; set; } = "";
    public string timestamp { get; set; } = "";
}

public class PlayerRespawnSync
{
    public string type { get; set; } = "playerRespawn";
    public int peerId { get; set; }
    public Vec3 position { get; set; } = new();
    public string timestamp { get; set; } = "";
}

public class PlayerJoinSync
{
    public string type { get; set; } = "playerJoin";
    public int peerId { get; set; }
    public string playerName { get; set; } = "";
    public Vec3 position { get; set; } = new();
    public Vec3 rotation { get; set; } = new();
    public string customFaceJson { get; set; } = "";
    public string timestamp { get; set; } = "";
}

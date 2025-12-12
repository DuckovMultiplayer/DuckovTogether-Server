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
using DuckovNet;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class EntitySyncManager
{
    private static EntitySyncManager? _instance;
    public static EntitySyncManager Instance => _instance ??= new EntitySyncManager();
    
    private readonly Dictionary<uint, EntityState> _entities = new();
    private readonly object _lock = new();
    private uint _nextEntityId = 1;
    
    public uint RegisterEntity(string entityType, string prefabId, Vector3 position, Quaternion rotation, string sceneId)
    {
        lock (_lock)
        {
            var id = _nextEntityId++;
            _entities[id] = new EntityState
            {
                EntityId = id,
                EntityType = entityType,
                PrefabId = prefabId,
                Position = position,
                Rotation = rotation,
                SceneId = sceneId,
                SpawnTime = DateTime.UtcNow,
                IsActive = true
            };
            return id;
        }
    }
    
    public void UpdateEntityPosition(uint entityId, Vector3 position, Quaternion rotation)
    {
        lock (_lock)
        {
            if (_entities.TryGetValue(entityId, out var entity))
            {
                entity.Position = position;
                entity.Rotation = rotation;
                entity.LastUpdate = DateTime.UtcNow;
            }
        }
    }
    
    public void DeactivateEntity(uint entityId)
    {
        lock (_lock)
        {
            if (_entities.TryGetValue(entityId, out var entity))
            {
                entity.IsActive = false;
            }
        }
    }
    
    public void RemoveEntity(uint entityId)
    {
        lock (_lock)
        {
            _entities.Remove(entityId);
        }
    }
    
    public EntityState? GetEntity(uint entityId)
    {
        lock (_lock)
        {
            return _entities.TryGetValue(entityId, out var entity) ? entity : null;
        }
    }
    
    public List<EntityState> GetEntitiesInScene(string sceneId)
    {
        lock (_lock)
        {
            return _entities.Values
                .Where(e => e.SceneId == sceneId && e.IsActive)
                .ToList();
        }
    }
    
    public void BroadcastEntitySpawn(uint entityId, INetService netService)
    {
        lock (_lock)
        {
            if (!_entities.TryGetValue(entityId, out var entity)) return;
            
            var msg = new EntitySpawnMessage
            {
                type = "entity_spawn",
                entityId = entity.EntityId,
                entityType = entity.EntityType,
                prefabId = entity.PrefabId,
                posX = entity.Position.X,
                posY = entity.Position.Y,
                posZ = entity.Position.Z,
                rotX = entity.Rotation.X,
                rotY = entity.Rotation.Y,
                rotZ = entity.Rotation.Z,
                rotW = entity.Rotation.W,
                sceneId = entity.SceneId
            };
            
            var json = JsonConvert.SerializeObject(msg);
            var writer = new NetDataWriter();
            writer.Put((byte)9);
            writer.Put(json);
            netService.SendToAll(writer.Data, DeliveryMethod.ReliableOrdered);
        }
    }
    
    public void SendFullSyncToPlayer(int peerId, string sceneId, INetService netService)
    {
        lock (_lock)
        {
            var entities = _entities.Values
                .Where(e => e.SceneId == sceneId && e.IsActive)
                .ToList();
            
            var msg = new EntityFullSyncMessage
            {
                type = "entity_full_sync",
                sceneId = sceneId,
                entities = entities.Select(e => new EntityData
                {
                    entityId = e.EntityId,
                    entityType = e.EntityType,
                    prefabId = e.PrefabId,
                    posX = e.Position.X,
                    posY = e.Position.Y,
                    posZ = e.Position.Z,
                    rotX = e.Rotation.X,
                    rotY = e.Rotation.Y,
                    rotZ = e.Rotation.Z,
                    rotW = e.Rotation.W
                }).ToList()
            };
            
            var json = JsonConvert.SerializeObject(msg);
            var writer = new NetDataWriter();
            writer.Put((byte)9);
            writer.Put(json);
            netService.SendToPeer(peerId, writer.Data, DeliveryMethod.ReliableOrdered);
        }
    }
    
    public void ClearScene(string sceneId)
    {
        lock (_lock)
        {
            var toRemove = _entities.Where(kv => kv.Value.SceneId == sceneId)
                .Select(kv => kv.Key).ToList();
            foreach (var id in toRemove)
                _entities.Remove(id);
        }
    }
}

public class EntityState
{
    public uint EntityId { get; set; }
    public string EntityType { get; set; } = "";
    public string PrefabId { get; set; } = "";
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public string SceneId { get; set; } = "";
    public DateTime SpawnTime { get; set; }
    public DateTime LastUpdate { get; set; }
    public bool IsActive { get; set; }
}

public class EntitySpawnMessage
{
    public string type { get; set; } = "";
    public uint entityId { get; set; }
    public string entityType { get; set; } = "";
    public string prefabId { get; set; } = "";
    public float posX { get; set; }
    public float posY { get; set; }
    public float posZ { get; set; }
    public float rotX { get; set; }
    public float rotY { get; set; }
    public float rotZ { get; set; }
    public float rotW { get; set; }
    public string sceneId { get; set; } = "";
}

public class EntityFullSyncMessage
{
    public string type { get; set; } = "";
    public string sceneId { get; set; } = "";
    public List<EntityData> entities { get; set; } = new();
}

public class EntityData
{
    public uint entityId { get; set; }
    public string entityType { get; set; } = "";
    public string prefabId { get; set; } = "";
    public float posX { get; set; }
    public float posY { get; set; }
    public float posZ { get; set; }
    public float rotX { get; set; }
    public float rotY { get; set; }
    public float rotZ { get; set; }
    public float rotW { get; set; }
}

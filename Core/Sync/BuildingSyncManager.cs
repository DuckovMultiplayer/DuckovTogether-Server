// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using DuckovTogether.Core.Save;
using DuckovTogetherServer.Core.Logging;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class BuildingSyncManager
{
    private static BuildingSyncManager? _instance;
    public static BuildingSyncManager Instance => _instance ??= new BuildingSyncManager();
    
    private readonly object _lock = new();
    private Action<string>? _broadcastJson;
    
    public void Initialize(Action<string>? broadcastJson = null)
    {
        _broadcastJson = broadcastJson;
        Log.Info("BuildingSync initialized");
    }
    
    public void SetBroadcastHandler(Action<string> handler)
    {
        _broadcastJson = handler;
    }
    
    private void BroadcastJsonMessage(string json)
    {
        _broadcastJson?.Invoke(json);
    }
    
    public void InitializeOld()
    {
        Log.Info("BuildingSync initialized");
    }
    
    public void OnBuildingPlaced(string playerId, string buildingId, string buildingType, 
        string sceneId, float posX, float posY, float posZ, float rotX, float rotY, float rotZ)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            
            if (world.Buildings.ContainsKey(buildingId))
            {
                Log.Debug($"Building {buildingId} already exists");
                return;
            }
            
            var building = new BuildingState
            {
                BuildingId = buildingId,
                BuildingType = buildingType,
                OwnerId = playerId,
                SceneId = sceneId,
                PosX = posX,
                PosY = posY,
                PosZ = posZ,
                RotX = rotX,
                RotY = rotY,
                RotZ = rotZ,
                Level = 1,
                Health = 100f,
                IsDestroyed = false,
                PlacedAt = DateTime.Now
            };
            
            world.Buildings[buildingId] = building;
            Log.Debug($"Building placed: {buildingType} by {playerId} at ({posX}, {posY}, {posZ})");
            
            BroadcastBuildingPlaced(building);
        }
    }
    
    public void OnBuildingDestroyed(string playerId, string buildingId)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            
            if (!world.Buildings.TryGetValue(buildingId, out var building))
            {
                Log.Debug($"Building {buildingId} not found");
                return;
            }
            
            building.IsDestroyed = true;
            building.DestroyedAt = DateTime.Now;
            Log.Debug($"Building destroyed: {buildingId} by {playerId}");
            
            BroadcastBuildingDestroyed(buildingId);
        }
    }
    
    public void OnBuildingUpgraded(string playerId, string buildingId, int newLevel)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            
            if (!world.Buildings.TryGetValue(buildingId, out var building))
            {
                Log.Debug($"Building {buildingId} not found");
                return;
            }
            
            building.Level = newLevel;
            Log.Debug($"Building upgraded: {buildingId} to level {newLevel}");
            
            BroadcastBuildingUpgraded(buildingId, newLevel);
        }
    }
    
    public void OnBuildingDamaged(string buildingId, float newHealth)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            
            if (!world.Buildings.TryGetValue(buildingId, out var building))
                return;
            
            building.Health = newHealth;
            
            if (newHealth <= 0)
            {
                building.IsDestroyed = true;
                building.DestroyedAt = DateTime.Now;
                BroadcastBuildingDestroyed(buildingId);
            }
        }
    }
    
    public string GetBuildingsForScene(string sceneId)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            var buildings = world.Buildings.Values
                .Where(b => b.SceneId == sceneId && !b.IsDestroyed)
                .Select(b => new BuildingSyncData
                {
                    buildingId = b.BuildingId,
                    buildingType = b.BuildingType,
                    ownerId = b.OwnerId,
                    posX = b.PosX,
                    posY = b.PosY,
                    posZ = b.PosZ,
                    rotX = b.RotX,
                    rotY = b.RotY,
                    rotZ = b.RotZ,
                    level = b.Level,
                    health = b.Health,
                    timestamp = b.PlacedAt.Ticks,
                    isDestroyed = b.IsDestroyed
                })
                .ToArray();
            
            var msg = new BuildingFullSyncMessage
            {
                type = "buildingFullSync",
                buildings = buildings,
                timestamp = DateTime.UtcNow.Ticks
            };
            
            return JsonConvert.SerializeObject(msg);
        }
    }
    
    public string GetAllBuildings()
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            var buildings = world.Buildings.Values
                .Where(b => !b.IsDestroyed)
                .Select(b => new BuildingSyncData
                {
                    buildingId = b.BuildingId,
                    buildingType = b.BuildingType,
                    ownerId = b.OwnerId,
                    posX = b.PosX,
                    posY = b.PosY,
                    posZ = b.PosZ,
                    rotX = b.RotX,
                    rotY = b.RotY,
                    rotZ = b.RotZ,
                    level = b.Level,
                    health = b.Health,
                    timestamp = b.PlacedAt.Ticks,
                    isDestroyed = b.IsDestroyed
                })
                .ToArray();
            
            var msg = new BuildingFullSyncMessage
            {
                type = "buildingFullSync",
                buildings = buildings,
                timestamp = DateTime.UtcNow.Ticks
            };
            
            return JsonConvert.SerializeObject(msg);
        }
    }
    
    private void BroadcastBuildingPlaced(BuildingState building)
    {
        var msg = new
        {
            type = "buildingPlaced",
            buildingId = building.BuildingId,
            buildingType = building.BuildingType,
            ownerId = building.OwnerId,
            posX = building.PosX,
            posY = building.PosY,
            posZ = building.PosZ,
            rotX = building.RotX,
            rotY = building.RotY,
            rotZ = building.RotZ,
            timestamp = building.PlacedAt.Ticks
        };
        
        var json = JsonConvert.SerializeObject(msg);
        BroadcastJsonMessage(json);
    }
    
    private void BroadcastBuildingDestroyed(string buildingId)
    {
        var msg = new
        {
            type = "buildingDestroyed",
            buildingId = buildingId,
            timestamp = DateTime.UtcNow.Ticks
        };
        
        var json = JsonConvert.SerializeObject(msg);
        BroadcastJsonMessage(json);
    }
    
    private void BroadcastBuildingUpgraded(string buildingId, int newLevel)
    {
        var msg = new
        {
            type = "buildingUpgraded",
            buildingId = buildingId,
            newLevel = newLevel,
            timestamp = DateTime.UtcNow.Ticks
        };
        
        var json = JsonConvert.SerializeObject(msg);
        BroadcastJsonMessage(json);
    }
}

public class BuildingSyncData
{
    public string buildingId { get; set; } = "";
    public string buildingType { get; set; } = "";
    public string ownerId { get; set; } = "";
    public float posX { get; set; }
    public float posY { get; set; }
    public float posZ { get; set; }
    public float rotX { get; set; }
    public float rotY { get; set; }
    public float rotZ { get; set; }
    public int level { get; set; }
    public float health { get; set; }
    public long timestamp { get; set; }
    public bool isDestroyed { get; set; }
}

public class BuildingFullSyncMessage
{
    public string type { get; set; } = "";
    public BuildingSyncData[] buildings { get; set; } = Array.Empty<BuildingSyncData>();
    public long timestamp { get; set; }
}

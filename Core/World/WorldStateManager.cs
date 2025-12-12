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

namespace DuckovTogether.Core.World;

public class WorldStateManager
{
    private static WorldStateManager? _instance;
    public static WorldStateManager Instance => _instance ??= new WorldStateManager();
    
    private readonly object _lock = new();
    private DateTime _lastAutoSave = DateTime.Now;
    private const double AUTO_SAVE_INTERVAL = 300;
    
    public string CurrentSceneId { get; private set; } = "";
    public bool IsVoteActive { get; private set; }
    public string VoteTargetScene { get; private set; } = "";
    public Dictionary<string, bool> VoteStatus { get; } = new();
    
    private WorldStateManager() { }
    
    public void Initialize()
    {
        ServerSaveManager.Instance.LoadWorld();
        CurrentSceneId = ServerSaveManager.Instance.CurrentWorld.CurrentScene;
        Log.Info($"WorldState initialized, scene: {CurrentSceneId}");
    }
    
    public void Update()
    {
        if ((DateTime.Now - _lastAutoSave).TotalSeconds >= AUTO_SAVE_INTERVAL)
        {
            ServerSaveManager.Instance.SaveAll();
            _lastAutoSave = DateTime.Now;
        }
    }
    
    public void OnPlayerJoin(string playerId, string playerName)
    {
        lock (_lock)
        {
            var playerData = ServerSaveManager.Instance.LoadPlayer(playerId);
            playerData.PlayerName = playerName;
            playerData.LastLogin = DateTime.Now;
            ServerSaveManager.Instance.SavePlayer(playerId);
        }
    }
    
    public void OnPlayerLeave(string playerId)
    {
        lock (_lock)
        {
            if (ServerSaveManager.Instance.PlayerSaves.TryGetValue(playerId, out var data))
            {
                data.LastSaved = DateTime.Now;
                ServerSaveManager.Instance.SavePlayer(playerId);
            }
            
            if (VoteStatus.ContainsKey(playerId))
            {
                VoteStatus.Remove(playerId);
                CheckVoteResult();
            }
        }
    }
    
    public void StartVote(string sceneId, string initiatorId)
    {
        lock (_lock)
        {
            if (IsVoteActive)
            {
                Log.Debug("Vote already active");
                return;
            }
            
            IsVoteActive = true;
            VoteTargetScene = sceneId;
            VoteStatus.Clear();
            VoteStatus[initiatorId] = true;
            
            Log.Info($"Vote started for scene: {sceneId} by {initiatorId}");
            BroadcastVoteState();
        }
    }
    
    public void SetVoteReady(string playerId, bool ready)
    {
        lock (_lock)
        {
            if (!IsVoteActive) return;
            VoteStatus[playerId] = ready;
            Log.Debug($"Player {playerId} vote: {ready}");
            BroadcastVoteState();
            CheckVoteResult();
        }
    }
    
    public void CancelVote()
    {
        lock (_lock)
        {
            IsVoteActive = false;
            VoteTargetScene = "";
            VoteStatus.Clear();
            Log.Info("Vote cancelled");
            BroadcastVoteState();
        }
    }
    
    private void BroadcastVoteState()
    {
        try
        {
            Sync.SyncManager.Instance.BroadcastSceneVote(VoteTargetScene, VoteStatus);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BroadcastVoteState");
        }
    }
    
    private void CheckVoteResult()
    {
        if (!IsVoteActive || VoteStatus.Count == 0) return;
        
        var allReady = VoteStatus.Values.All(v => v);
        if (allReady && VoteStatus.Count >= 1)
        {
            Log.Info($"Vote passed! Transitioning to: {VoteTargetScene}");
            TransitionScene(VoteTargetScene);
        }
    }
    
    public void TransitionScene(string newSceneId)
    {
        lock (_lock)
        {
            CurrentSceneId = newSceneId;
            ServerSaveManager.Instance.CurrentWorld.CurrentScene = newSceneId;
            
            IsVoteActive = false;
            VoteTargetScene = "";
            VoteStatus.Clear();
            
            ServerSaveManager.Instance.SaveWorld();
            Log.Info($"Scene changed to: {newSceneId}");
        }
    }
    
    public void UpdateLootContainer(string containerId, string sceneId, bool isLooted, string? lootedBy)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            if (!world.LootContainers.TryGetValue(containerId, out var container))
            {
                container = new LootContainerState { ContainerId = containerId, SceneId = sceneId };
                world.LootContainers[containerId] = container;
            }
            
            container.IsLooted = isLooted;
            if (isLooted)
            {
                container.LootedAt = DateTime.Now;
                container.LootedBy = lootedBy;
            }
        }
    }
    
    public void UpdateAIState(string aiId, string aiType, string sceneId, bool isDead, float health, float x, float y, float z)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            if (!world.AIEntities.TryGetValue(aiId, out var ai))
            {
                ai = new AIState { AIId = aiId, AIType = aiType, SceneId = sceneId };
                world.AIEntities[aiId] = ai;
            }
            
            ai.IsDead = isDead;
            ai.Health = health;
            ai.PosX = x;
            ai.PosY = y;
            ai.PosZ = z;
        }
    }
    
    public void SpawnDroppedItem(string itemId, string itemType, string sceneId, float x, float y, float z, string? droppedBy)
    {
        lock (_lock)
        {
            var item = new DroppedItemState
            {
                ItemId = itemId,
                ItemType = itemType,
                SceneId = sceneId,
                PosX = x,
                PosY = y,
                PosZ = z,
                DroppedAt = DateTime.Now,
                DroppedBy = droppedBy
            };
            ServerSaveManager.Instance.CurrentWorld.DroppedItems.Add(item);
        }
    }
    
    public bool PickupDroppedItem(string itemId, string pickupBy)
    {
        lock (_lock)
        {
            var item = ServerSaveManager.Instance.CurrentWorld.DroppedItems
                .FirstOrDefault(i => i.ItemId == itemId);
            
            if (item != null)
            {
                ServerSaveManager.Instance.CurrentWorld.DroppedItems.Remove(item);
                Log.Debug($"Item {itemId} picked up by {pickupBy}");
                return true;
            }
            return false;
        }
    }
    
    public void PlaceBuilding(string playerId, string buildingId, string buildingType, 
        string sceneId, float posX, float posY, float posZ, float rotX, float rotY, float rotZ)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            
            world.Buildings[buildingId] = new BuildingState
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
            
            Log.Debug($"Building placed: {buildingType} by {playerId}");
        }
    }
    
    public void DestroyBuilding(string buildingId, string destroyedBy)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            
            if (world.Buildings.TryGetValue(buildingId, out var building))
            {
                building.IsDestroyed = true;
                building.DestroyedAt = DateTime.Now;
                Log.Debug($"Building destroyed: {buildingId} by {destroyedBy}");
            }
        }
    }
    
    public void UpgradeBuilding(string buildingId, int newLevel)
    {
        lock (_lock)
        {
            var world = ServerSaveManager.Instance.CurrentWorld;
            
            if (world.Buildings.TryGetValue(buildingId, out var building))
            {
                building.Level = newLevel;
                Log.Debug($"Building upgraded: {buildingId} to level {newLevel}");
            }
        }
    }
    
    public IReadOnlyDictionary<string, BuildingState> GetBuildingsForScene(string sceneId)
    {
        lock (_lock)
        {
            return ServerSaveManager.Instance.CurrentWorld.Buildings
                .Where(b => b.Value.SceneId == sceneId && !b.Value.IsDestroyed)
                .ToDictionary(b => b.Key, b => b.Value);
        }
    }
    
    public void Shutdown()
    {
        Log.Info("WorldState shutting down...");
        ServerSaveManager.Instance.SaveAll();
    }
}

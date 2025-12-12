// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Core.Save;

public class ServerSaveManager
{
    private static ServerSaveManager? _instance;
    public static ServerSaveManager Instance => _instance ??= new ServerSaveManager();
    
    private readonly string _saveDirectory;
    private readonly string _worldSavePath;
    private readonly string _playerSavePath;
    
    public WorldState CurrentWorld { get; private set; } = new();
    public Dictionary<string, PlayerSaveData> PlayerSaves { get; } = new();
    
    private ServerSaveManager()
    {
        _saveDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_saves");
        _worldSavePath = Path.Combine(_saveDirectory, "world");
        _playerSavePath = Path.Combine(_saveDirectory, "players");
        
        Directory.CreateDirectory(_saveDirectory);
        Directory.CreateDirectory(_worldSavePath);
        Directory.CreateDirectory(_playerSavePath);
    }
    
    public void LoadWorld(string worldId = "default")
    {
        var path = Path.Combine(_worldSavePath, $"{worldId}.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                CurrentWorld = JsonConvert.DeserializeObject<WorldState>(json) ?? new WorldState();
                Log.Info($"Loaded world: {worldId}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadWorld");
                CurrentWorld = new WorldState();
            }
        }
        else
        {
            CurrentWorld = new WorldState { WorldId = worldId, CreatedAt = DateTime.Now };
            SaveWorld();
            Log.Info($"Created new world: {worldId}");
        }
    }
    
    public void SaveWorld()
    {
        var path = Path.Combine(_worldSavePath, $"{CurrentWorld.WorldId}.json");
        CurrentWorld.LastSaved = DateTime.Now;
        
        try
        {
            var json = JsonConvert.SerializeObject(CurrentWorld, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SaveWorld");
        }
    }
    
    public PlayerSaveData LoadPlayer(string playerId)
    {
        if (PlayerSaves.TryGetValue(playerId, out var cached))
            return cached;
        
        var path = Path.Combine(_playerSavePath, $"{SanitizeFileName(playerId)}.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<PlayerSaveData>(json) ?? new PlayerSaveData { PlayerId = playerId };
                PlayerSaves[playerId] = data;
                Log.Debug($"Loaded player: {playerId}");
                return data;
            }
            catch
            {
                return CreateNewPlayer(playerId);
            }
        }
        
        return CreateNewPlayer(playerId);
    }
    
    private PlayerSaveData CreateNewPlayer(string playerId)
    {
        var data = new PlayerSaveData
        {
            PlayerId = playerId,
            CreatedAt = DateTime.Now
        };
        PlayerSaves[playerId] = data;
        SavePlayer(playerId);
        Log.Debug($"Created new player: {playerId}");
        return data;
    }
    
    public void SavePlayer(string playerId)
    {
        if (!PlayerSaves.TryGetValue(playerId, out var data))
            return;
        
        var path = Path.Combine(_playerSavePath, $"{SanitizeFileName(playerId)}.json");
        data.LastSaved = DateTime.Now;
        
        try
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"SavePlayer {playerId}");
        }
    }
    
    public void SaveAll()
    {
        SaveWorld();
        foreach (var playerId in PlayerSaves.Keys.ToList())
        {
            SavePlayer(playerId);
        }
        Log.Info("Saved all data");
    }
    
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}

public class WorldState
{
    public string WorldId { get; set; } = "default";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSaved { get; set; }
    public string CurrentScene { get; set; } = "";
    public int GameDay { get; set; } = 1;
    public float GameTime { get; set; } = 8f;
    public Dictionary<string, LootContainerState> LootContainers { get; set; } = new();
    public Dictionary<string, AIState> AIEntities { get; set; } = new();
    public List<DroppedItemState> DroppedItems { get; set; } = new();
    public Dictionary<string, BuildingState> Buildings { get; set; } = new();
}

public class LootContainerState
{
    public string ContainerId { get; set; } = "";
    public string SceneId { get; set; } = "";
    public bool IsLooted { get; set; }
    public DateTime? LootedAt { get; set; }
    public string? LootedBy { get; set; }
    public List<ItemState> Items { get; set; } = new();
}

public class AIState
{
    public string AIId { get; set; } = "";
    public string AIType { get; set; } = "";
    public string SceneId { get; set; } = "";
    public bool IsDead { get; set; }
    public float Health { get; set; } = 100f;
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
}

public class DroppedItemState
{
    public string ItemId { get; set; } = "";
    public string ItemType { get; set; } = "";
    public string SceneId { get; set; } = "";
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public DateTime DroppedAt { get; set; }
    public string? DroppedBy { get; set; }
}

public class ItemState
{
    public string ItemId { get; set; } = "";
    public string ItemType { get; set; } = "";
    public int Count { get; set; } = 1;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class BuildingState
{
    public string BuildingId { get; set; } = "";
    public string BuildingType { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string SceneId { get; set; } = "";
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float RotX { get; set; }
    public float RotY { get; set; }
    public float RotZ { get; set; }
    public int Level { get; set; } = 1;
    public float Health { get; set; } = 100f;
    public bool IsDestroyed { get; set; }
    public DateTime PlacedAt { get; set; }
    public DateTime? DestroyedAt { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class PlayerSaveData
{
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSaved { get; set; }
    public DateTime LastLogin { get; set; }
    public int TotalPlayTime { get; set; }
    public string LastScene { get; set; } = "";
    public float LastPosX { get; set; }
    public float LastPosY { get; set; }
    public float LastPosZ { get; set; }
    public List<ItemState> Inventory { get; set; } = new();
    public Dictionary<string, object> Stats { get; set; } = new();
}

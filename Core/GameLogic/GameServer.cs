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
using DuckovTogether.Core.Save;
using DuckovTogether.Core.World;

namespace DuckovTogether.Core.GameLogic;

public class GameServer
{
    private static GameServer? _instance;
    public static GameServer Instance => _instance ??= new GameServer();
    
    public AIManager AI => AIManager.Instance;
    public ItemManager Items => ItemManager.Instance;
    public WorldStateManager World => WorldStateManager.Instance;
    public ServerSaveManager Saves => ServerSaveManager.Instance;
    
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly object _lock = new();
    
    private string _currentScene = "";
    private float _gameTime = 8f;
    private int _gameDay = 1;
    
    private DateTime _lastTick = DateTime.Now;
    private DateTime _lastAIBroadcast = DateTime.Now;
    private DateTime _lastStateBroadcast = DateTime.Now;
    
    private const double AI_BROADCAST_INTERVAL = 0.1;
    private const double STATE_BROADCAST_INTERVAL = 1.0;
    
    public event Action<int, AIEntity>? OnAIStateChanged;
    public event Action<string, LootContainer>? OnLootSpawned;
    public event Action<string>? OnSceneChanged;
    
    public void Initialize()
    {
        var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(dataPath);
        
        Items.LoadItemDatabase(Path.Combine(dataPath, "items.json"));
        LoadSceneData(dataPath);
        
        World.Initialize();
        
        Console.WriteLine("[GameServer] Initialized");
    }
    
    private void LoadSceneData(string dataPath)
    {
        var scenesPath = Path.Combine(dataPath, "scenes");
        if (!Directory.Exists(scenesPath))
        {
            Directory.CreateDirectory(scenesPath);
            CreateDefaultSceneData(scenesPath);
        }
        
        foreach (var file in Directory.GetFiles(scenesPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var sceneData = Newtonsoft.Json.JsonConvert.DeserializeObject<SceneData>(json);
                if (sceneData != null)
                {
                    RegisterSceneData(sceneData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameServer] Failed to load scene: {file} - {ex.Message}");
            }
        }
    }
    
    private void CreateDefaultSceneData(string scenesPath)
    {
        var defaultScene = new SceneData
        {
            SceneId = "Level_GroundZero_Main",
            DisplayName = "Ground Zero",
            AISpawns = new List<AISpawnPoint>
            {
                new() { SpawnerId = 1, SceneId = "Level_GroundZero_Main", AIType = "Scav", Position = new Vector3(10, 0, 10), MaxHealth = 100, DetectRange = 15 },
                new() { SpawnerId = 2, SceneId = "Level_GroundZero_Main", AIType = "Scav", Position = new Vector3(-10, 0, 15), MaxHealth = 100, DetectRange = 15 },
                new() { SpawnerId = 3, SceneId = "Level_GroundZero_Main", AIType = "Scav", Position = new Vector3(20, 0, -5), MaxHealth = 100, DetectRange = 15 }
            },
            LootSpawns = new List<LootSpawnPoint>
            {
                new() { SpawnerId = 1, SceneId = "Level_GroundZero_Main", Position = new Vector3(5, 0, 5), ContainerType = "crate", MinItems = 1, MaxItems = 3, LootTable = new List<LootTableEntry>
                {
                    new() { ItemId = "ammo_9mm", Chance = 40, MinCount = 10, MaxCount = 30 },
                    new() { ItemId = "med_bandage", Chance = 30, MinCount = 1, MaxCount = 2 },
                    new() { ItemId = "food_bread", Chance = 20, MinCount = 1, MaxCount = 1 }
                }},
                new() { SpawnerId = 2, SceneId = "Level_GroundZero_Main", Position = new Vector3(-5, 0, -5), ContainerType = "cabinet", MinItems = 1, MaxItems = 2 }
            },
            PlayerSpawns = new List<Vector3>
            {
                new(0, 0, 0),
                new(2, 0, 0),
                new(-2, 0, 0),
                new(0, 0, 2)
            }
        };
        
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(defaultScene, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(Path.Combine(scenesPath, "Level_GroundZero_Main.json"), json);
        
        RegisterSceneData(defaultScene);
        Console.WriteLine("[GameServer] Created default scene data");
    }
    
    private void RegisterSceneData(SceneData data)
    {
        foreach (var spawn in data.AISpawns)
        {
            AI.RegisterSpawnPoint(data.SceneId, spawn);
        }
        
        foreach (var spawn in data.LootSpawns)
        {
            Items.RegisterLootSpawnPoint(data.SceneId, spawn);
        }
        
        Console.WriteLine($"[GameServer] Registered scene: {data.SceneId} ({data.AISpawns.Count} AI, {data.LootSpawns.Count} loot)");
    }
    
    public void Update()
    {
        var now = DateTime.Now;
        var deltaTime = (float)(now - _lastTick).TotalSeconds;
        _lastTick = now;
        
        UpdateGameTime(deltaTime);
        
        lock (_lock)
        {
            AI.Update(_players);
        }
        
        World.Update();
        
        if ((now - _lastAIBroadcast).TotalSeconds >= AI_BROADCAST_INTERVAL)
        {
            _lastAIBroadcast = now;
        }
        
        if ((now - _lastStateBroadcast).TotalSeconds >= STATE_BROADCAST_INTERVAL)
        {
            _lastStateBroadcast = now;
        }
    }
    
    private void UpdateGameTime(float deltaTime)
    {
        _gameTime += deltaTime * 0.001f;
        if (_gameTime >= 24f)
        {
            _gameTime -= 24f;
            _gameDay++;
        }
    }
    
    public void OnPlayerJoin(int peerId, PlayerState state)
    {
        lock (_lock)
        {
            _players[peerId] = state;
        }
        
        World.OnPlayerJoin(state.EndPoint, state.PlayerName);
        Console.WriteLine($"[GameServer] Player joined: {state.PlayerName} (ID: {peerId})");
    }
    
    public void OnPlayerLeave(int peerId)
    {
        lock (_lock)
        {
            if (_players.TryGetValue(peerId, out var state))
            {
                World.OnPlayerLeave(state.EndPoint);
                _players.Remove(peerId);
                Console.WriteLine($"[GameServer] Player left: {state.PlayerName}");
            }
        }
    }
    
    public void UpdatePlayerState(int peerId, PlayerState state)
    {
        lock (_lock)
        {
            _players[peerId] = state;
        }
    }
    
    public void ChangeScene(string sceneId)
    {
        _currentScene = sceneId;
        
        AI.LoadSceneData(sceneId);
        Items.LoadSceneLoot(sceneId);
        
        OnSceneChanged?.Invoke(sceneId);
        Console.WriteLine($"[GameServer] Scene changed to: {sceneId}");
    }
    
    public void Shutdown()
    {
        Console.WriteLine("[GameServer] Shutting down...");
        Saves.SaveAll();
    }
}

public class SceneData
{
    public string SceneId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<AISpawnPoint> AISpawns { get; set; } = new();
    public List<LootSpawnPoint> LootSpawns { get; set; } = new();
    public List<Vector3> PlayerSpawns { get; set; } = new();
}

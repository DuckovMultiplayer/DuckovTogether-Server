// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using DuckovTogether.Core;
using DuckovTogether.Core.Assets;
using DuckovTogether.Core.GameLogic;
using DuckovTogether.Core.Save;
using DuckovTogether.Core.Security;
using DuckovTogether.Core.World;
using DuckovTogether.Net;
using DuckovTogether.Plugins;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether;

class Program
{
    private static bool _running = true;
    private static HeadlessNetService? _netService;
    private static MessageHandler? _messageHandler;
    
    static void Main(string[] args)
    {
        Log.Initialize("logs", LogLevel.Debug);
        
        Console.Title = "Duckov Headless Server";
        Log.Info("===========================================");
        Log.Info("  Duckov Coop Mod - Headless Server");
        Log.Info("  Version: 1.0.0");
        Log.Info("===========================================");
        
        var configPath = "server_config.json";
        var config = ServerConfig.Load(configPath);
        
        if (!File.Exists(configPath))
        {
            config.Save(configPath);
            Log.Info($"Created default config: {configPath}");
        }
        
        foreach (var arg in args)
        {
            if (arg.StartsWith("--port="))
            {
                if (int.TryParse(arg.Substring(7), out var port))
                {
                    config.Port = port;
                }
            }
            else if (arg.StartsWith("--max-players="))
            {
                if (int.TryParse(arg.Substring(14), out var maxPlayers))
                {
                    config.MaxPlayers = maxPlayers;
                }
            }
            else if (arg.StartsWith("--name="))
            {
                config.ServerName = arg.Substring(7);
            }
            else if (arg.StartsWith("--game-path="))
            {
                config.GamePath = arg.Substring(12);
            }
        }
        
        Log.Info($"Port: {config.Port}");
        Log.Info($"Max Players: {config.MaxPlayers}");
        Log.Info($"Server Name: {config.ServerName}");
        Log.Info($"Tick Rate: {config.TickRate} Hz");
        Log.Info($"Game Path: {config.GamePath}");
        
        var gamePath = config.GamePath;
        if (string.IsNullOrEmpty(gamePath))
        {
            gamePath = GamePathDetector.DetectGamePath();
        }
        
        if (!string.IsNullOrEmpty(gamePath))
        {
            Log.Info($"Game path: {gamePath}");
            Log.Info("Loading game resources...");
            
            Core.Assets.GameDataExtractor.Instance.LoadKnownScenes(config.KnownScenes);
            
            if (UnityAssetReader.Instance.Initialize(gamePath))
            {
                UnityAssetReader.Instance.SaveExtractedData(config.ExtractedDataPath);
                Log.Info("Game resources loaded successfully");
                
                var dataPath = Path.Combine(AppContext.BaseDirectory, "Data");
                if (Directory.Exists(dataPath))
                {
                    SceneDataManager.Instance.LoadFromDirectory(dataPath);
                    Core.Sync.GameDataValidator.Instance.Initialize();
                    
                    var itemsJsonPath = Path.Combine(dataPath, "items.json");
                    if (File.Exists(itemsJsonPath))
                    {
                        BuildingDataExtractor.Instance.ExtractFromItems(itemsJsonPath);
                        BuildingDataExtractor.Instance.SaveToJson(Path.Combine(dataPath, "buildings.json"));
                    }
                }
            }
            else
            {
                Log.Warn("Failed to load game resources, running in proxy mode");
            }
        }
        else
        {
            Log.Warn("Could not find game installation");
            Log.Warn("Set 'GamePath' in server_config.json or use --game-path=<path>");
        }
        
        ValidationService.Instance.Initialize(config.GameKey);
        GameServer.Instance.Initialize();
        
        _netService = new HeadlessNetService(config);
        _messageHandler = new MessageHandler(_netService);
        
        PluginManager.Instance.Initialize(_netService, AppContext.BaseDirectory);
        PluginManager.Instance.LoadPlugins();
        
        if (!_netService.Start())
        {
            Log.Fatal("Failed to start server. Press any key to exit...");
            Console.ReadKey();
            return;
        }
        
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _running = false;
            Log.Info("Shutting down...");
        };
        
        var inputThread = new Thread(InputLoop);
        inputThread.IsBackground = true;
        inputThread.Start();
        
        var tickInterval = 1000 / config.TickRate;
        var lastTick = DateTime.Now;
        
        while (_running)
        {
            var deltaTime = (float)(DateTime.Now - lastTick).TotalSeconds;
            
            _netService.Update();
            _messageHandler.Update();
            GameServer.Instance.Update();
            PluginManager.Instance.Update(deltaTime);
            
            var elapsed = (DateTime.Now - lastTick).TotalMilliseconds;
            if (elapsed < tickInterval)
            {
                Thread.Sleep((int)(tickInterval - elapsed));
            }
            lastTick = DateTime.Now;
        }
        
        PluginManager.Instance.UnloadPlugins();
        GameServer.Instance.Shutdown();
        ValidationService.Instance.Shutdown();
        _netService.Stop();
        Log.Info("Goodbye!"); Log.Shutdown();
    }
    
    static void InputLoop()
    {
        while (_running)
        {
            var input = Console.ReadLine()?.Trim().ToLower();
            if (string.IsNullOrEmpty(input)) continue;
            
            switch (input)
            {
                case "help":
                    Log.Info("Commands: status, players, kick <id>, save, world, scene <id>, plugins, quit");
                    break;
                    
                case "plugins":
                    var pluginList = PluginManager.Instance.GetPluginList().ToList();
                    if (pluginList.Count == 0)
                    {
                        Log.Info("No plugins loaded");
                    }
                    else
                    {
                        Log.Info($"Loaded {pluginList.Count} plugins: {string.Join(", ", pluginList)}");
                    }
                    break;
                    
                case "status":
                    Log.Info($"Running: {_netService?.IsRunning}, Players: {_netService?.PlayerCount}");
                    break;
                    
                case "players":
                    var players = _netService?.GetAllPlayers();
                    if (players == null || !players.Any())
                    {
                        Log.Info("No players connected");
                    }
                    else
                    {
                        foreach (var p in players)
                        {
                            Log.Info($"[{p.PeerId}] {p.PlayerName} - {p.EndPoint} (Ping: {p.Latency}ms)");
                        }
                    }
                    break;
                    
                case "save":
                    ServerSaveManager.Instance.SaveAll();
                    Log.Info("All data saved");
                    break;
                    
                case "world":
                    var world = GameServer.Instance.Saves.CurrentWorld;
                    Log.Info($"World: {world.WorldId}, Scene: {world.CurrentScene}, Day: {world.GameDay}, AI: {GameServer.Instance.AI.EntityCount}");
                    break;
                    
                case "ai":
                    Log.Info($"AI entities: {GameServer.Instance.AI.EntityCount}");
                    break;
                    
                case "data":
                    Log.Info($"Scenes: {SceneDataManager.Instance.Scenes.Count}, Items: {SceneDataManager.Instance.Items.Count}, AI: {SceneDataManager.Instance.AITypes.Count}");
                    break;
                    
                case "buildings":
                    Log.Info($"Buildings: {BuildingDataExtractor.Instance.Buildings.Count}");
                    break;
                    
                case "quit":
                case "exit":
                case "stop":
                    _running = false;
                    break;
                    
                default:
                    if (input.StartsWith("scene "))
                    {
                        var sceneId = input.Substring(6).Trim();
                        GameServer.Instance.ChangeScene(sceneId);
                        break;
                    }
                    if (input.StartsWith("kick "))
                    {
                        var idStr = input.Substring(5).Trim();
                        if (int.TryParse(idStr, out var kickId))
                        {
                            var peer = _netService?.NetManager?.ConnectedPeerList
                                .FirstOrDefault(p => p.Id == kickId);
                            if (peer != null)
                            {
                                peer.Disconnect();
                                Log.Info($"Kicked player {kickId}");
                            }
                            else
                            {
                                Log.Warn($"Player {kickId} not found");
                            }
                        }
                    }
                    else if (!PluginManager.Instance.TryExecuteCommand(input))
                    {
                        Log.Warn($"Unknown command: {input}");
                    }
                    break;
            }
        }
    }
}

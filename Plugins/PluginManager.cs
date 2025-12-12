// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System.Reflection;
using DuckovTogether.Net;
using DuckovTogether.Core;
using DuckovNet;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Plugins;

public class PluginManager : IPluginHost
{
    private static PluginManager? _instance;
    public static PluginManager Instance => _instance ??= new PluginManager();
    
    private readonly List<PluginContainer> _plugins = new();
    private readonly Dictionary<string, Action<string[]>> _commands = new();
    private readonly Dictionary<byte, List<Action<int, byte[]>>> _messageHandlers = new();
    private readonly Dictionary<Type, object> _services = new();
    private readonly List<ScheduledTask> _tasks = new();
    private int _nextTaskId = 1;
    
    private HeadlessNetService? _netService;
    
    public string DataPath { get; private set; } = "";
    public string PluginPath { get; private set; } = "";
    
    public int LoadedPluginCount => _plugins.Count;
    
    public void Initialize(HeadlessNetService netService, string basePath)
    {
        _netService = netService;
        DataPath = Path.Combine(basePath, "Data");
        PluginPath = Path.Combine(basePath, "Plugins");
        
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(PluginPath);
        
        DuckovTogetherServer.Core.Logging.Log.Info("PluginManager initialized");
        DuckovTogetherServer.Core.Logging.Log.Debug($"Plugin directory: {PluginPath}");
    }
    
    public void LoadPlugins()
    {
        if (!Directory.Exists(PluginPath))
        {
            DuckovTogetherServer.Core.Logging.Log.Debug("No plugins directory found");
            return;
        }
        
        var dllFiles = Directory.GetFiles(PluginPath, "*.dll");
        DuckovTogetherServer.Core.Logging.Log.Debug($"Found {dllFiles.Length} plugin files");
        
        foreach (var dllPath in dllFiles)
        {
            try
            {
                LoadPlugin(dllPath);
            }
            catch (Exception ex)
            {
                DuckovTogetherServer.Core.Logging.Log.Error($"Failed to load {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }
        
        DuckovTogetherServer.Core.Logging.Log.Info($"Loaded {_plugins.Count} plugins");
    }
    
    private void LoadPlugin(string dllPath)
    {
        var assembly = Assembly.LoadFrom(dllPath);
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        
        foreach (var type in pluginTypes)
        {
            var plugin = (IPlugin)Activator.CreateInstance(type)!;
            var container = new PluginContainer
            {
                Plugin = plugin,
                Assembly = assembly,
                FilePath = dllPath
            };
            
            _plugins.Add(container);
            
            try
            {
                plugin.OnLoad(this);
                RegisterPluginCommands(plugin);
                DuckovTogetherServer.Core.Logging.Log.Info($"Loaded plugin: {plugin.Name} v{plugin.Version} by {plugin.Author}");
            }
            catch (Exception ex)
            {
                DuckovTogetherServer.Core.Logging.Log.Error($"Error initializing {plugin.Name}: {ex.Message}");
                _plugins.Remove(container);
            }
        }
    }
    
    private void RegisterPluginCommands(IPlugin plugin)
    {
        var methods = plugin.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<CommandAttribute>();
            if (attr != null)
            {
                var handler = (Action<string[]>)Delegate.CreateDelegate(typeof(Action<string[]>), plugin, method);
                RegisterCommand(attr.Name, attr.Description, handler);
            }
        }
    }
    
    public void UnloadPlugins()
    {
        foreach (var container in _plugins.ToList())
        {
            try
            {
                container.Plugin.OnUnload();
                DuckovTogetherServer.Core.Logging.Log.Info($"Unloaded: {container.Plugin.Name}");
            }
            catch (Exception ex)
            {
                DuckovTogetherServer.Core.Logging.Log.Error($"Error unloading {container.Plugin.Name}: {ex.Message}");
            }
        }
        _plugins.Clear();
        _commands.Clear();
        _messageHandlers.Clear();
    }
    
    public void Update(float deltaTime)
    {
        foreach (var container in _plugins)
        {
            try
            {
                container.Plugin.OnUpdate(deltaTime);
            }
            catch (Exception ex)
            {
                DuckovTogetherServer.Core.Logging.Log.Error($"Plugin {container.Plugin.Name} update error: {ex.Message}");
            }
        }
        
        ProcessScheduledTasks(deltaTime);
    }
    
    private void ProcessScheduledTasks(float deltaTime)
    {
        var now = DateTime.Now;
        var toRemove = new List<ScheduledTask>();
        
        foreach (var task in _tasks.ToList())
        {
            if (now >= task.ExecuteTime)
            {
                try
                {
                    task.Action();
                }
                catch (Exception ex)
                {
                    DuckovTogetherServer.Core.Logging.Log.Error($"Task error: {ex.Message}");
                }
                
                if (task.IsRepeating)
                {
                    task.ExecuteTime = now.AddSeconds(task.Interval);
                }
                else
                {
                    toRemove.Add(task);
                }
            }
        }
        
        foreach (var task in toRemove)
        {
            _tasks.Remove(task);
        }
    }
    
    public bool TryExecuteCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        
        var cmdName = parts[0].ToLower();
        var args = parts.Skip(1).ToArray();
        
        if (_commands.TryGetValue(cmdName, out var handler))
        {
            try
            {
                handler(args);
                return true;
            }
            catch (Exception ex)
            {
                DuckovTogetherServer.Core.Logging.Log.Error($"Command error '{cmdName}': {ex.Message}");
                return true;
            }
        }
        
        return false;
    }
    
    public void Log(string message) => DuckovTogetherServer.Core.Logging.Log.Info($"[Plugin] {message}");
    public void LogWarning(string message) => DuckovTogetherServer.Core.Logging.Log.Warn($"[Plugin] {message}");
    public void LogError(string message) => DuckovTogetherServer.Core.Logging.Log.Error($"[Plugin] {message}");
    
    public void RegisterCommand(string name, string description, Action<string[]> handler)
    {
        _commands[name.ToLower()] = handler;
    }
    
    public void UnregisterCommand(string name)
    {
        _commands.Remove(name.ToLower());
    }
    
    public void RegisterMessageHandler(byte messageType, Action<int, byte[]> handler)
    {
        if (!_messageHandlers.ContainsKey(messageType))
            _messageHandlers[messageType] = new List<Action<int, byte[]>>();
        _messageHandlers[messageType].Add(handler);
    }
    
    public void UnregisterMessageHandler(byte messageType)
    {
        _messageHandlers.Remove(messageType);
    }
    
    public void HandleMessage(int peerId, byte messageType, byte[] data)
    {
        if (_messageHandlers.TryGetValue(messageType, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler(peerId, data);
                }
                catch (Exception ex)
                {
                    DuckovTogetherServer.Core.Logging.Log.Error($"Message handler error: {ex.Message}");
                }
            }
        }
    }
    
    public void BroadcastMessage(byte messageType, byte[] data)
    {
        if (_netService == null) return;
        
        var writer = new NetDataWriter();
        writer.Put(messageType);
        writer.Put(data);
        _netService.SendToAll(writer);
    }
    
    public void SendToPlayer(int peerId, byte messageType, byte[] data)
    {
        if (_netService?.NetManager == null) return;
        
        var writer = new NetDataWriter();
        writer.Put(messageType);
        writer.Put(data);
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            if (peer.Id == peerId)
            {
                _netService.SendToPeer(peer, writer);
                break;
            }
        }
    }
    
    public IEnumerable<IPlayerInfo> GetOnlinePlayers()
    {
        if (_netService == null) return Enumerable.Empty<IPlayerInfo>();
        return _netService.GetAllPlayers().Select(p => new PlayerInfoWrapper(p));
    }
    
    public IPlayerInfo? GetPlayer(int peerId)
    {
        var player = _netService?.GetPlayer(peerId);
        return player != null ? new PlayerInfoWrapper(player) : null;
    }
    
    public void ScheduleTask(float delaySeconds, Action task)
    {
        _tasks.Add(new ScheduledTask
        {
            Id = _nextTaskId++,
            ExecuteTime = DateTime.Now.AddSeconds(delaySeconds),
            Action = task,
            IsRepeating = false
        });
    }
    
    public void ScheduleRepeating(float intervalSeconds, Action task, out int taskId)
    {
        var id = _nextTaskId++;
        _tasks.Add(new ScheduledTask
        {
            Id = id,
            ExecuteTime = DateTime.Now.AddSeconds(intervalSeconds),
            Action = task,
            IsRepeating = true,
            Interval = intervalSeconds
        });
        taskId = id;
    }
    
    public void CancelTask(int taskId)
    {
        _tasks.RemoveAll(t => t.Id == taskId);
    }
    
    public T? GetService<T>() where T : class
    {
        return _services.TryGetValue(typeof(T), out var service) ? service as T : null;
    }
    
    public void RegisterService<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }
    
    public IEnumerable<(string Name, string Description)> GetCommands()
    {
        return _commands.Select(c => (c.Key, "Plugin command"));
    }
    
    public IEnumerable<string> GetPluginList()
    {
        return _plugins.Select(p => $"{p.Plugin.Name} v{p.Plugin.Version} by {p.Plugin.Author}");
    }
}

internal class PluginContainer
{
    public IPlugin Plugin { get; set; } = null!;
    public Assembly Assembly { get; set; } = null!;
    public string FilePath { get; set; } = "";
}

internal class ScheduledTask
{
    public int Id { get; set; }
    public DateTime ExecuteTime { get; set; }
    public Action Action { get; set; } = null!;
    public bool IsRepeating { get; set; }
    public float Interval { get; set; }
}

internal class PlayerInfoWrapper : IPlayerInfo
{
    private readonly PlayerState _state;
    
    public PlayerInfoWrapper(PlayerState state)
    {
        _state = state;
    }
    
    public int PeerId => _state.PeerId;
    public string Name => _state.PlayerName;
    public string EndPoint => _state.EndPoint;
    public bool IsInGame => _state.IsInGame;
    public string SceneId => _state.SceneId;
    public int Latency => _state.Latency;
}

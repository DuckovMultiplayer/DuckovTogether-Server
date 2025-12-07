// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

namespace DuckovTogether.Plugins;

public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }
    
    void OnLoad(IPluginHost host);
    void OnUnload();
    void OnUpdate(float deltaTime);
}

public interface IPluginHost
{
    void Log(string message);
    void LogWarning(string message);
    void LogError(string message);
    
    void RegisterCommand(string name, string description, Action<string[]> handler);
    void UnregisterCommand(string name);
    
    void RegisterMessageHandler(byte messageType, Action<int, byte[]> handler);
    void UnregisterMessageHandler(byte messageType);
    
    void BroadcastMessage(byte messageType, byte[] data);
    void SendToPlayer(int peerId, byte messageType, byte[] data);
    
    IEnumerable<IPlayerInfo> GetOnlinePlayers();
    IPlayerInfo GetPlayer(int peerId);
    
    void ScheduleTask(float delaySeconds, Action task);
    void ScheduleRepeating(float intervalSeconds, Action task, out int taskId);
    void CancelTask(int taskId);
    
    T GetService<T>() where T : class;
    void RegisterService<T>(T service) where T : class;
    
    string DataPath { get; }
    string PluginPath { get; }
}

public interface IPlayerInfo
{
    int PeerId { get; }
    string Name { get; }
    string EndPoint { get; }
    bool IsInGame { get; }
    string SceneId { get; }
    int Latency { get; }
}

[AttributeUsage(AttributeTargets.Class)]
public class PluginInfoAttribute : Attribute
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
}

[AttributeUsage(AttributeTargets.Method)]
public class CommandAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Usage { get; set; } = "";
    
    public CommandAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class EventHandlerAttribute : Attribute
{
    public Type EventType { get; }
    
    public EventHandlerAttribute(Type eventType)
    {
        EventType = eventType;
    }
}

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

namespace DuckovTogether.Plugins;

public abstract class PluginEvent
{
    public bool Cancelled { get; set; }
    public DateTime Timestamp { get; } = DateTime.Now;
}

public class PlayerJoinEvent : PluginEvent
{
    public int PeerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string EndPoint { get; set; } = "";
}

public class PlayerLeaveEvent : PluginEvent
{
    public int PeerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Reason { get; set; } = "";
}

public class PlayerChatEvent : PluginEvent
{
    public int PeerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Message { get; set; } = "";
}

public class PlayerMoveEvent : PluginEvent
{
    public int PeerId { get; set; }
    public Vector3 OldPosition { get; set; }
    public Vector3 NewPosition { get; set; }
}

public class PlayerDamageEvent : PluginEvent
{
    public int VictimPeerId { get; set; }
    public int AttackerPeerId { get; set; }
    public float Damage { get; set; }
    public string DamageType { get; set; } = "";
}

public class PlayerDeathEvent : PluginEvent
{
    public int VictimPeerId { get; set; }
    public int KillerPeerId { get; set; }
    public string DeathCause { get; set; } = "";
}

public class AISpawnEvent : PluginEvent
{
    public int EntityId { get; set; }
    public string AIType { get; set; } = "";
    public Vector3 Position { get; set; }
}

public class AIDeathEvent : PluginEvent
{
    public int EntityId { get; set; }
    public int KillerPeerId { get; set; }
}

public class SceneChangeEvent : PluginEvent
{
    public string OldSceneId { get; set; } = "";
    public string NewSceneId { get; set; } = "";
}

public class ExtractStartEvent : PluginEvent
{
    public int PeerId { get; set; }
    public string ExtractPointId { get; set; } = "";
}

public class ExtractCompleteEvent : PluginEvent
{
    public int PeerId { get; set; }
    public string ExtractPointId { get; set; } = "";
}

public class ItemPickupEvent : PluginEvent
{
    public int PeerId { get; set; }
    public int ItemTypeId { get; set; }
    public int Count { get; set; }
}

public class ItemDropEvent : PluginEvent
{
    public int PeerId { get; set; }
    public int ItemTypeId { get; set; }
    public int Count { get; set; }
    public Vector3 Position { get; set; }
}

public static class PluginEventDispatcher
{
    private static readonly Dictionary<Type, List<Delegate>> _handlers = new();
    
    public static void Register<T>(Action<T> handler) where T : PluginEvent
    {
        var type = typeof(T);
        if (!_handlers.ContainsKey(type))
            _handlers[type] = new List<Delegate>();
        _handlers[type].Add(handler);
    }
    
    public static void Unregister<T>(Action<T> handler) where T : PluginEvent
    {
        var type = typeof(T);
        if (_handlers.ContainsKey(type))
            _handlers[type].Remove(handler);
    }
    
    public static bool Dispatch<T>(T evt) where T : PluginEvent
    {
        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    ((Action<T>)handler)(evt);
                    if (evt.Cancelled) break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EventDispatcher] Error: {ex.Message}");
                }
            }
        }
        return !evt.Cancelled;
    }
    
    public static void Clear()
    {
        _handlers.Clear();
    }
}

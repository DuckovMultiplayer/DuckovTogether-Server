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
using DuckovTogether.Core.GameLogic;
using DuckovTogether.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class ItemAuthorityManager
{
    private static ItemAuthorityManager? _instance;
    public static ItemAuthorityManager Instance => _instance ??= new ItemAuthorityManager();
    
    private HeadlessNetService? _netService;
    private readonly NetDataWriter _writer = new();
    
    private readonly Dictionary<int, LootContainerState> _lootContainers = new();
    private readonly Dictionary<int, HashSet<int>> _playerInventories = new();
    private readonly Dictionary<int, ItemTransferLock> _transferLocks = new();
    
    private int _nextContainerId = 1000;
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Console.WriteLine("[ItemAuthority] Initialized");
    }
    
    public void RegisterContainer(int containerId, Vector3 position, string containerType, List<LootItemState> items)
    {
        var container = new LootContainerState
        {
            ContainerId = containerId,
            Position = position,
            ContainerType = containerType,
            Items = items.ToDictionary(i => i.Slot, i => i),
            CreatedTime = DateTime.Now
        };
        _lootContainers[containerId] = container;
        Console.WriteLine($"[ItemAuthority] Registered container {containerId} with {items.Count} items");
    }
    
    public int CreateContainer(Vector3 position, string containerType)
    {
        var containerId = _nextContainerId++;
        var items = LootTableConfig.Instance.GenerateLootForContainer(containerType);
        
        var itemStates = items.Select((item, index) => new LootItemState
        {
            Slot = index,
            ItemId = item.ItemId,
            Count = item.Count
        }).ToList();
        
        RegisterContainer(containerId, position, containerType, itemStates);
        return containerId;
    }
    
    public bool ValidatePickup(int peerId, int containerId, int slotIndex, out string? error)
    {
        error = null;
        
        if (!_lootContainers.TryGetValue(containerId, out var container))
        {
            error = "Container not found";
            return false;
        }
        
        if (!container.Items.TryGetValue(slotIndex, out var item))
        {
            error = "Slot is empty";
            return false;
        }
        
        var lockKey = containerId * 1000 + slotIndex;
        if (_transferLocks.TryGetValue(lockKey, out var existingLock))
        {
            if (existingLock.PeerId != peerId && (DateTime.Now - existingLock.LockTime).TotalSeconds < 2)
            {
                error = "Item is locked by another player";
                return false;
            }
        }
        
        _transferLocks[lockKey] = new ItemTransferLock
        {
            PeerId = peerId,
            ContainerId = containerId,
            SlotIndex = slotIndex,
            LockTime = DateTime.Now
        };
        
        return true;
    }
    
    public void ConfirmPickup(int peerId, int containerId, int slotIndex)
    {
        if (!_lootContainers.TryGetValue(containerId, out var container)) return;
        
        if (container.Items.TryGetValue(slotIndex, out var item))
        {
            container.Items.Remove(slotIndex);
            
            if (!_playerInventories.ContainsKey(peerId))
                _playerInventories[peerId] = new HashSet<int>();
            
            var pickupData = new ItemPickupConfirm
            {
                type = "itemPickupConfirm",
                peerId = peerId,
                containerId = containerId,
                slotIndex = slotIndex,
                itemId = item.ItemId,
                count = item.Count,
                success = true,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };
            
            BroadcastJson(pickupData);
            Console.WriteLine($"[ItemAuthority] Player {peerId} picked up {item.ItemId} x{item.Count} from container {containerId}");
        }
        
        var lockKey = containerId * 1000 + slotIndex;
        _transferLocks.Remove(lockKey);
    }
    
    public void RejectPickup(int peerId, int containerId, int slotIndex, string reason)
    {
        var rejectData = new ItemPickupReject
        {
            type = "itemPickupReject",
            peerId = peerId,
            containerId = containerId,
            slotIndex = slotIndex,
            reason = reason,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        SendJsonToPeer(peerId, rejectData);
        
        var lockKey = containerId * 1000 + slotIndex;
        _transferLocks.Remove(lockKey);
    }
    
    public bool ValidateDrop(int peerId, string itemId, int count, Vector3 position, out string? error)
    {
        error = null;
        
        if (string.IsNullOrEmpty(itemId))
        {
            error = "Invalid item ID";
            return false;
        }
        
        if (count <= 0)
        {
            error = "Invalid count";
            return false;
        }
        
        var player = _netService?.GetPlayer(peerId);
        if (player == null)
        {
            error = "Player not found";
            return false;
        }
        
        return true;
    }
    
    public void OnItemDrop(int peerId, string itemId, int count, Vector3 position)
    {
        var containerId = CreateDroppedItemContainer(position, itemId, count);
        
        var dropData = new ItemDropConfirm
        {
            type = "itemDropConfirm",
            peerId = peerId,
            containerId = containerId,
            itemId = itemId,
            count = count,
            position = new Vec3 { x = position.X, y = position.Y, z = position.Z },
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(dropData);
        Console.WriteLine($"[ItemAuthority] Player {peerId} dropped {itemId} x{count} at {position}");
    }
    
    private int CreateDroppedItemContainer(Vector3 position, string itemId, int count)
    {
        var containerId = _nextContainerId++;
        var items = new List<LootItemState>
        {
            new() { Slot = 0, ItemId = itemId, Count = count }
        };
        
        RegisterContainer(containerId, position, "dropped", items);
        return containerId;
    }
    
    public void SendContainerState(int peerId, int containerId)
    {
        if (!_lootContainers.TryGetValue(containerId, out var container)) return;
        
        var stateData = new ContainerStateSync
        {
            type = "containerState",
            containerId = containerId,
            containerType = container.ContainerType,
            position = new Vec3 { x = container.Position.X, y = container.Position.Y, z = container.Position.Z },
            items = container.Items.Values.Select(i => new LootItemData
            {
                slot = i.Slot,
                itemId = i.ItemId,
                count = i.Count
            }).ToArray(),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        SendJsonToPeer(peerId, stateData);
    }
    
    public void BroadcastContainerState(int containerId)
    {
        if (!_lootContainers.TryGetValue(containerId, out var container)) return;
        
        var stateData = new ContainerStateSync
        {
            type = "containerState",
            containerId = containerId,
            containerType = container.ContainerType,
            position = new Vec3 { x = container.Position.X, y = container.Position.Y, z = container.Position.Z },
            items = container.Items.Values.Select(i => new LootItemData
            {
                slot = i.Slot,
                itemId = i.ItemId,
                count = i.Count
            }).ToArray(),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(stateData);
    }
    
    public void OnPlayerDisconnected(int peerId)
    {
        _playerInventories.Remove(peerId);
        
        var locksToRemove = _transferLocks.Where(kv => kv.Value.PeerId == peerId).Select(kv => kv.Key).ToList();
        foreach (var key in locksToRemove)
        {
            _transferLocks.Remove(key);
        }
    }
    
    public void ClearSceneContainers()
    {
        _lootContainers.Clear();
        _transferLocks.Clear();
        Console.WriteLine("[ItemAuthority] Cleared all containers for scene change");
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

public class LootContainerState
{
    public int ContainerId { get; set; }
    public Vector3 Position { get; set; }
    public string ContainerType { get; set; } = "";
    public Dictionary<int, LootItemState> Items { get; set; } = new();
    public DateTime CreatedTime { get; set; }
}

public class ItemTransferLock
{
    public int PeerId { get; set; }
    public int ContainerId { get; set; }
    public int SlotIndex { get; set; }
    public DateTime LockTime { get; set; }
}

public class ItemPickupConfirm
{
    public string type { get; set; } = "itemPickupConfirm";
    public int peerId { get; set; }
    public int containerId { get; set; }
    public int slotIndex { get; set; }
    public string itemId { get; set; } = "";
    public int count { get; set; }
    public bool success { get; set; }
    public string timestamp { get; set; } = "";
}

public class ItemPickupReject
{
    public string type { get; set; } = "itemPickupReject";
    public int peerId { get; set; }
    public int containerId { get; set; }
    public int slotIndex { get; set; }
    public string reason { get; set; } = "";
    public string timestamp { get; set; } = "";
}

public class ItemDropConfirm
{
    public string type { get; set; } = "itemDropConfirm";
    public int peerId { get; set; }
    public int containerId { get; set; }
    public string itemId { get; set; } = "";
    public int count { get; set; }
    public Vec3 position { get; set; } = new();
    public string timestamp { get; set; } = "";
}

public class ContainerStateSync
{
    public string type { get; set; } = "containerState";
    public int containerId { get; set; }
    public string containerType { get; set; } = "";
    public Vec3 position { get; set; } = new();
    public LootItemData[] items { get; set; } = Array.Empty<LootItemData>();
    public string timestamp { get; set; } = "";
}

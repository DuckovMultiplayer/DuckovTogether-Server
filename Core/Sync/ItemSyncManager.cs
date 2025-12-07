using System.Numerics;
using DuckovTogether.Core.GameLogic;
using DuckovTogether.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class ItemSyncManager
{
    private static ItemSyncManager? _instance;
    public static ItemSyncManager Instance => _instance ??= new ItemSyncManager();
    
    private HeadlessNetService? _netService;
    private readonly NetDataWriter _writer = new();
    
    private readonly Dictionary<int, DroppedItemState> _droppedItems = new();
    private int _nextDropId = 1;
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Console.WriteLine("[ItemSync] Initialized");
    }
    
    public void OnItemPickup(int playerId, int containerId, int slotIndex, int itemTypeId, int count)
    {
        var data = new ItemPickupSync
        {
            type = "itemPickup",
            playerId = playerId,
            containerId = containerId,
            slotIndex = slotIndex,
            itemTypeId = itemTypeId,
            count = count,
            success = true,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public int OnItemDrop(int playerId, int itemTypeId, int count, Vector3 position)
    {
        var dropId = _nextDropId++;
        
        _droppedItems[dropId] = new DroppedItemState
        {
            DropId = dropId,
            ItemTypeId = itemTypeId,
            Count = count,
            Position = position,
            DroppedBy = playerId,
            DropTime = DateTime.Now
        };
        
        var data = new ItemDropSync
        {
            type = "itemDrop",
            dropId = dropId,
            playerId = playerId,
            itemTypeId = itemTypeId,
            count = count,
            position = new Vec3 { x = position.X, y = position.Y, z = position.Z },
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
        return dropId;
    }
    
    public void OnDroppedItemPickup(int playerId, int dropId)
    {
        if (!_droppedItems.TryGetValue(dropId, out var item))
            return;
            
        _droppedItems.Remove(dropId);
        
        var data = new DroppedItemPickupSync
        {
            type = "droppedItemPickup",
            dropId = dropId,
            playerId = playerId,
            itemTypeId = item.ItemTypeId,
            count = item.Count,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnItemTransfer(int playerId, int fromContainerId, int fromSlot, int toContainerId, int toSlot, int itemTypeId, int count)
    {
        var data = new ItemTransferSync
        {
            type = "itemTransfer",
            playerId = playerId,
            fromContainerId = fromContainerId,
            fromSlot = fromSlot,
            toContainerId = toContainerId,
            toSlot = toSlot,
            itemTypeId = itemTypeId,
            count = count,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnItemUse(int playerId, int itemTypeId, int slot)
    {
        var data = new ItemUseSync
        {
            type = "itemUse",
            playerId = playerId,
            itemTypeId = itemTypeId,
            slot = slot,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJsonExcept(playerId, data);
    }
    
    public void OnContainerOpen(int playerId, int containerId)
    {
        var data = new ContainerInteractSync
        {
            type = "containerOpen",
            playerId = playerId,
            containerId = containerId,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnContainerClose(int playerId, int containerId)
    {
        var data = new ContainerInteractSync
        {
            type = "containerClose",
            playerId = playerId,
            containerId = containerId,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void SendContainerContents(NetPeer peer, int containerId)
    {
        var data = new ContainerContentsSync
        {
            type = "containerContents",
            containerId = containerId,
            items = new List<ContainerItemInfo>(),
            timestamp = DateTime.Now.Ticks
        };
        
        SendJsonToPeer(peer, data);
    }
    
    public void SendDroppedItemsState(NetPeer peer)
    {
        var data = new DroppedItemsStateSync
        {
            type = "droppedItemsState",
            items = _droppedItems.Values.Select(i => new DroppedItemInfo
            {
                dropId = i.DropId,
                itemTypeId = i.ItemTypeId,
                count = i.Count,
                position = new Vec3 { x = i.Position.X, y = i.Position.Y, z = i.Position.Z }
            }).ToList(),
            timestamp = DateTime.Now.Ticks
        };
        
        SendJsonToPeer(peer, data);
    }
    
    public void CleanupOldDrops(TimeSpan maxAge)
    {
        var now = DateTime.Now;
        var toRemove = _droppedItems.Where(kv => now - kv.Value.DropTime > maxAge)
                                    .Select(kv => kv.Key)
                                    .ToList();
                                    
        foreach (var id in toRemove)
        {
            _droppedItems.Remove(id);
            
            var data = new DroppedItemDespawnSync
            {
                type = "droppedItemDespawn",
                dropId = id,
                timestamp = DateTime.Now.Ticks
            };
            
            BroadcastJson(data);
        }
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
    
    private void BroadcastJsonExcept(int excludePeerId, object data)
    {
        if (_netService?.NetManager == null) return;
        
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            if (peer.Id != excludePeerId)
            {
                peer.Send(_writer, DeliveryMethod.ReliableOrdered);
            }
        }
    }
    
    private void SendJsonToPeer(NetPeer peer, object data)
    {
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
}

public class DroppedItemState
{
    public int DropId { get; set; }
    public int ItemTypeId { get; set; }
    public int Count { get; set; }
    public Vector3 Position { get; set; }
    public int DroppedBy { get; set; }
    public DateTime DropTime { get; set; }
}

public class ItemPickupSync
{
    public string type { get; set; } = "itemPickup";
    public int playerId { get; set; }
    public int containerId { get; set; }
    public int slotIndex { get; set; }
    public int itemTypeId { get; set; }
    public int count { get; set; }
    public bool success { get; set; }
    public long timestamp { get; set; }
}

public class ItemDropSync
{
    public string type { get; set; } = "itemDrop";
    public int dropId { get; set; }
    public int playerId { get; set; }
    public int itemTypeId { get; set; }
    public int count { get; set; }
    public Vec3 position { get; set; } = new();
    public long timestamp { get; set; }
}

public class DroppedItemPickupSync
{
    public string type { get; set; } = "droppedItemPickup";
    public int dropId { get; set; }
    public int playerId { get; set; }
    public int itemTypeId { get; set; }
    public int count { get; set; }
    public long timestamp { get; set; }
}

public class ItemTransferSync
{
    public string type { get; set; } = "itemTransfer";
    public int playerId { get; set; }
    public int fromContainerId { get; set; }
    public int fromSlot { get; set; }
    public int toContainerId { get; set; }
    public int toSlot { get; set; }
    public int itemTypeId { get; set; }
    public int count { get; set; }
    public long timestamp { get; set; }
}

public class ItemUseSync
{
    public string type { get; set; } = "itemUse";
    public int playerId { get; set; }
    public int itemTypeId { get; set; }
    public int slot { get; set; }
    public long timestamp { get; set; }
}

public class ContainerInteractSync
{
    public string type { get; set; } = "";
    public int playerId { get; set; }
    public int containerId { get; set; }
    public long timestamp { get; set; }
}

public class ContainerContentsSync
{
    public string type { get; set; } = "containerContents";
    public int containerId { get; set; }
    public List<ContainerItemInfo> items { get; set; } = new();
    public long timestamp { get; set; }
}

public class ContainerItemInfo
{
    public int slot { get; set; }
    public int itemTypeId { get; set; }
    public int count { get; set; }
    public float durability { get; set; }
}

public class DroppedItemsStateSync
{
    public string type { get; set; } = "droppedItemsState";
    public List<DroppedItemInfo> items { get; set; } = new();
    public long timestamp { get; set; }
}

public class DroppedItemInfo
{
    public int dropId { get; set; }
    public int itemTypeId { get; set; }
    public int count { get; set; }
    public Vec3 position { get; set; } = new();
}

public class DroppedItemDespawnSync
{
    public string type { get; set; } = "droppedItemDespawn";
    public int dropId { get; set; }
    public long timestamp { get; set; }
}

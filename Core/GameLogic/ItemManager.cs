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
using Newtonsoft.Json;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Core.GameLogic;

public class ItemManager
{
    private static ItemManager? _instance;
    public static ItemManager Instance => _instance ??= new ItemManager();
    
    private readonly Dictionary<string, ItemDefinition> _itemDatabase = new();
    private readonly Dictionary<string, LootContainer> _containers = new();
    private readonly Dictionary<string, DroppedItem> _droppedItems = new();
    private readonly Dictionary<string, List<LootSpawnPoint>> _lootSpawnPoints = new();
    private readonly object _lock = new();
    
    private int _nextDropId = 1;
    
    public void LoadItemDatabase(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn($"Item database not found: {path}");
            CreateDefaultDatabase(path);
            return;
        }
        
        try
        {
            var json = File.ReadAllText(path);
            var items = JsonConvert.DeserializeObject<List<ItemDefinition>>(json);
            if (items != null)
            {
                foreach (var item in items)
                {
                    _itemDatabase[item.ItemId] = item;
                }
            }
            Log.Info($"Loaded {_itemDatabase.Count} items");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LoadItemDatabase");
        }
    }
    
    private void CreateDefaultDatabase(string path)
    {
        var defaultItems = new List<ItemDefinition>
        {
            new() { ItemId = "ammo_9mm", Name = "9mm Ammo", Category = ItemCategory.Ammo, StackSize = 60 },
            new() { ItemId = "ammo_762", Name = "7.62mm Ammo", Category = ItemCategory.Ammo, StackSize = 30 },
            new() { ItemId = "ammo_12g", Name = "12 Gauge", Category = ItemCategory.Ammo, StackSize = 20 },
            new() { ItemId = "med_bandage", Name = "Bandage", Category = ItemCategory.Medical, StackSize = 5 },
            new() { ItemId = "med_medkit", Name = "Medkit", Category = ItemCategory.Medical, StackSize = 1 },
            new() { ItemId = "food_bread", Name = "Bread", Category = ItemCategory.Food, StackSize = 3 },
            new() { ItemId = "weapon_pistol", Name = "Pistol", Category = ItemCategory.Weapon, StackSize = 1 },
            new() { ItemId = "weapon_rifle", Name = "Rifle", Category = ItemCategory.Weapon, StackSize = 1 },
            new() { ItemId = "armor_vest", Name = "Armor Vest", Category = ItemCategory.Armor, StackSize = 1 },
            new() { ItemId = "helmet_basic", Name = "Basic Helmet", Category = ItemCategory.Armor, StackSize = 1 }
        };
        
        var json = JsonConvert.SerializeObject(defaultItems, Formatting.Indented);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        
        foreach (var item in defaultItems)
        {
            _itemDatabase[item.ItemId] = item;
        }
        
        Log.Info($"Created default database with {defaultItems.Count} items");
    }
    
    public void LoadSceneLoot(string sceneId)
    {
        lock (_lock)
        {
            foreach (var container in _containers.Values.Where(c => c.SceneId == sceneId).ToList())
            {
                _containers.Remove(container.ContainerId);
            }
            
            if (_lootSpawnPoints.TryGetValue(sceneId, out var points))
            {
                foreach (var point in points)
                {
                    SpawnLootContainer(point);
                }
            }
            
            Log.Debug($"Loaded loot for scene: {sceneId}");
        }
    }
    
    public void RegisterLootSpawnPoint(string sceneId, LootSpawnPoint point)
    {
        lock (_lock)
        {
            if (!_lootSpawnPoints.ContainsKey(sceneId))
                _lootSpawnPoints[sceneId] = new List<LootSpawnPoint>();
            
            _lootSpawnPoints[sceneId].Add(point);
        }
    }
    
    public LootContainer SpawnLootContainer(LootSpawnPoint point)
    {
        var container = new LootContainer
        {
            ContainerId = $"{point.SceneId}_{point.SpawnerId}",
            SceneId = point.SceneId,
            Position = point.Position,
            ContainerType = point.ContainerType,
            Items = GenerateLoot(point.LootTable, point.MinItems, point.MaxItems),
            SpawnTime = DateTime.Now
        };
        
        lock (_lock)
        {
            _containers[container.ContainerId] = container;
        }
        
        return container;
    }
    
    private List<LootItem> GenerateLoot(List<LootTableEntry> lootTable, int min, int max)
    {
        var items = new List<LootItem>();
        var rng = new Random();
        var count = rng.Next(min, max + 1);
        
        for (int i = 0; i < count && lootTable.Count > 0; i++)
        {
            var roll = rng.NextDouble() * 100;
            foreach (var entry in lootTable)
            {
                if (roll <= entry.Chance)
                {
                    var amount = rng.Next(entry.MinCount, entry.MaxCount + 1);
                    items.Add(new LootItem
                    {
                        ItemId = entry.ItemId,
                        Count = amount
                    });
                    break;
                }
                roll -= entry.Chance;
            }
        }
        
        return items;
    }
    
    public List<LootItem>? TryLootContainer(string containerId, int playerId)
    {
        lock (_lock)
        {
            if (!_containers.TryGetValue(containerId, out var container))
                return null;
            
            if (container.IsLooted)
                return null;
            
            container.IsLooted = true;
            container.LootedAt = DateTime.Now;
            container.LootedBy = playerId;
            
            Log.Debug($"Container {containerId} looted by player {playerId}");
            return container.Items;
        }
    }
    
    public string DropItem(string itemId, int count, Vector3 position, string sceneId, int? droppedBy)
    {
        var dropId = $"drop_{_nextDropId++}";
        
        var item = new DroppedItem
        {
            DropId = dropId,
            ItemId = itemId,
            Count = count,
            Position = position,
            SceneId = sceneId,
            DroppedAt = DateTime.Now,
            DroppedBy = droppedBy
        };
        
        lock (_lock)
        {
            _droppedItems[dropId] = item;
        }
        
        Log.Debug($"Item dropped: {itemId} x{count} at {position}");
        return dropId;
    }
    
    public DroppedItem? TryPickupItem(string dropId, int playerId)
    {
        lock (_lock)
        {
            if (!_droppedItems.TryGetValue(dropId, out var item))
                return null;
            
            _droppedItems.Remove(dropId);
            Log.Debug($"Item {item.ItemId} picked up by player {playerId}");
            return item;
        }
    }
    
    public ItemDefinition? GetItem(string itemId)
    {
        return _itemDatabase.TryGetValue(itemId, out var item) ? item : null;
    }
    
    public IEnumerable<LootContainer> GetContainersInScene(string sceneId)
    {
        lock (_lock)
        {
            return _containers.Values.Where(c => c.SceneId == sceneId).ToList();
        }
    }
    
    public IEnumerable<DroppedItem> GetDroppedItemsInScene(string sceneId)
    {
        lock (_lock)
        {
            return _droppedItems.Values.Where(i => i.SceneId == sceneId).ToList();
        }
    }
}

public enum ItemCategory
{
    Weapon,
    Armor,
    Ammo,
    Medical,
    Medicine,
    Food,
    Key,
    Quest,
    Misc,
    Throwable,
    Consumable,
    Material,
    Tool,
    Attachment,
    Container,
    Currency,
    Junk,
    Resource,
    Equipment,
    Accessory,
    Backpack,
    Rig,
    Helmet,
    Clothing
}

public class ItemDefinition
{
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public ItemCategory Category { get; set; }
    public int StackSize { get; set; } = 1;
    public float Weight { get; set; } = 0.1f;
    public int Value { get; set; } = 100;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class LootContainer
{
    public string ContainerId { get; set; } = "";
    public string SceneId { get; set; } = "";
    public Vector3 Position { get; set; }
    public string ContainerType { get; set; } = "crate";
    public List<LootItem> Items { get; set; } = new();
    public bool IsLooted { get; set; }
    public DateTime SpawnTime { get; set; }
    public DateTime? LootedAt { get; set; }
    public int? LootedBy { get; set; }
}

public class LootItem
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; } = 1;
}

public class DroppedItem
{
    public string DropId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int Count { get; set; } = 1;
    public Vector3 Position { get; set; }
    public string SceneId { get; set; } = "";
    public DateTime DroppedAt { get; set; }
    public int? DroppedBy { get; set; }
}

public class LootSpawnPoint
{
    public int SpawnerId { get; set; }
    public string SceneId { get; set; } = "";
    public Vector3 Position { get; set; }
    public string ContainerType { get; set; } = "crate";
    public List<LootTableEntry> LootTable { get; set; } = new();
    public int MinItems { get; set; } = 1;
    public int MaxItems { get; set; } = 3;
    public float RespawnTime { get; set; } = 600f;
}

public class LootTableEntry
{
    public string ItemId { get; set; } = "";
    public float Chance { get; set; } = 10f;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
}

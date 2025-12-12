// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using DuckovTogetherServer.Core.Logging;
using Newtonsoft.Json;

namespace DuckovTogether.Core.GameLogic;

public class LootTableConfig
{
    private static LootTableConfig? _instance;
    public static LootTableConfig Instance => _instance ??= new LootTableConfig();
    
    public Dictionary<string, LootTable> Tables { get; private set; } = new();
    public Dictionary<string, AILootConfig> AILoot { get; private set; } = new();
    public Dictionary<string, ContainerLootConfig> ContainerLoot { get; private set; } = new();
    
    public void Initialize(string dataPath)
    {
        var lootPath = Path.Combine(dataPath, "loot_tables.json");
        
        if (File.Exists(lootPath))
        {
            try
            {
                var json = File.ReadAllText(lootPath);
                var config = JsonConvert.DeserializeObject<LootConfigData>(json);
                if (config != null)
                {
                    Tables = config.Tables ?? new();
                    AILoot = config.AILoot ?? new();
                    ContainerLoot = config.ContainerLoot ?? new();
                    Log.Info($"Loaded {Tables.Count} tables, {AILoot.Count} AI configs");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load config: {ex.Message}");
                CreateDefaultConfig(lootPath);
            }
        }
        else
        {
            CreateDefaultConfig(lootPath);
        }
    }
    
    private void CreateDefaultConfig(string path)
    {
        Tables = new Dictionary<string, LootTable>
        {
            ["common"] = new LootTable
            {
                TableId = "common",
                Entries = new List<LootEntry>
                {
                    new() { ItemId = "ammo_9mm", Weight = 100, MinCount = 5, MaxCount = 20 },
                    new() { ItemId = "med_bandage", Weight = 60, MinCount = 1, MaxCount = 2 },
                    new() { ItemId = "food_bread", Weight = 40, MinCount = 1, MaxCount = 1 },
                    new() { ItemId = "misc_duct_tape", Weight = 20, MinCount = 1, MaxCount = 1 }
                }
            },
            ["military"] = new LootTable
            {
                TableId = "military",
                Entries = new List<LootEntry>
                {
                    new() { ItemId = "ammo_545", Weight = 100, MinCount = 10, MaxCount = 30 },
                    new() { ItemId = "ammo_762", Weight = 80, MinCount = 10, MaxCount = 20 },
                    new() { ItemId = "med_ifak", Weight = 50, MinCount = 1, MaxCount = 1 },
                    new() { ItemId = "gear_vest", Weight = 30, MinCount = 1, MaxCount = 1 },
                    new() { ItemId = "gear_helmet", Weight = 20, MinCount = 1, MaxCount = 1 }
                }
            },
            ["rare"] = new LootTable
            {
                TableId = "rare",
                Entries = new List<LootEntry>
                {
                    new() { ItemId = "key_labs", Weight = 10, MinCount = 1, MaxCount = 1 },
                    new() { ItemId = "key_marked", Weight = 5, MinCount = 1, MaxCount = 1 },
                    new() { ItemId = "weapon_ak74", Weight = 15, MinCount = 1, MaxCount = 1 },
                    new() { ItemId = "med_salewa", Weight = 40, MinCount = 1, MaxCount = 1 }
                }
            }
        };
        
        AILoot = new Dictionary<string, AILootConfig>
        {
            ["scav"] = new AILootConfig
            {
                AIType = "scav",
                MinItems = 1,
                MaxItems = 3,
                LootTables = new[] { "common" },
                GuaranteedItems = new List<GuaranteedItem>()
            },
            ["pmc"] = new AILootConfig
            {
                AIType = "pmc",
                MinItems = 2,
                MaxItems = 5,
                LootTables = new[] { "common", "military" },
                GuaranteedItems = new List<GuaranteedItem>
                {
                    new() { ItemId = "ammo_545", MinCount = 10, MaxCount = 30, Chance = 80 }
                }
            },
            ["boss"] = new AILootConfig
            {
                AIType = "boss",
                MinItems = 4,
                MaxItems = 8,
                LootTables = new[] { "military", "rare" },
                GuaranteedItems = new List<GuaranteedItem>
                {
                    new() { ItemId = "key_rare", MinCount = 1, MaxCount = 1, Chance = 100 }
                }
            }
        };
        
        ContainerLoot = new Dictionary<string, ContainerLootConfig>
        {
            ["crate"] = new ContainerLootConfig
            {
                ContainerType = "crate",
                MinItems = 1,
                MaxItems = 4,
                LootTables = new[] { "common" }
            },
            ["weapon_box"] = new ContainerLootConfig
            {
                ContainerType = "weapon_box",
                MinItems = 1,
                MaxItems = 3,
                LootTables = new[] { "military" }
            },
            ["safe"] = new ContainerLootConfig
            {
                ContainerType = "safe",
                MinItems = 2,
                MaxItems = 5,
                LootTables = new[] { "rare" }
            }
        };
        
        var config = new LootConfigData
        {
            Tables = Tables,
            AILoot = AILoot,
            ContainerLoot = ContainerLoot
        };
        
        try
        {
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(path, json);
            Log.Info($"Created default config: {path}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save config: {ex.Message}");
        }
    }
    
    public List<LootItem> GenerateLootForAI(string aiType)
    {
        var items = new List<LootItem>();
        var random = new Random();
        
        var aiTypeLower = aiType.ToLower();
        if (!AILoot.TryGetValue(aiTypeLower, out var config))
        {
            config = AILoot.GetValueOrDefault("scav") ?? new AILootConfig { MinItems = 1, MaxItems = 2, LootTables = new[] { "common" } };
        }
        
        foreach (var guaranteed in config.GuaranteedItems)
        {
            if (random.Next(100) < guaranteed.Chance)
            {
                items.Add(new LootItem
                {
                    ItemId = guaranteed.ItemId,
                    Count = random.Next(guaranteed.MinCount, guaranteed.MaxCount + 1)
                });
            }
        }
        
        var itemCount = random.Next(config.MinItems, config.MaxItems + 1);
        for (int i = 0; i < itemCount; i++)
        {
            var tableId = config.LootTables[random.Next(config.LootTables.Length)];
            if (Tables.TryGetValue(tableId, out var table))
            {
                var item = RollFromTable(table, random);
                if (item != null)
                {
                    items.Add(item);
                }
            }
        }
        
        return items;
    }
    
    public List<LootItem> GenerateLootForContainer(string containerType)
    {
        var items = new List<LootItem>();
        var random = new Random();
        
        if (!ContainerLoot.TryGetValue(containerType.ToLower(), out var config))
        {
            config = ContainerLoot.GetValueOrDefault("crate") ?? new ContainerLootConfig { MinItems = 1, MaxItems = 2, LootTables = new[] { "common" } };
        }
        
        var itemCount = random.Next(config.MinItems, config.MaxItems + 1);
        for (int i = 0; i < itemCount; i++)
        {
            var tableId = config.LootTables[random.Next(config.LootTables.Length)];
            if (Tables.TryGetValue(tableId, out var table))
            {
                var item = RollFromTable(table, random);
                if (item != null)
                {
                    items.Add(item);
                }
            }
        }
        
        return items;
    }
    
    private LootItem? RollFromTable(LootTable table, Random random)
    {
        var totalWeight = table.Entries.Sum(e => e.Weight);
        var roll = random.Next(totalWeight);
        
        var current = 0;
        foreach (var entry in table.Entries)
        {
            current += entry.Weight;
            if (roll < current)
            {
                return new LootItem
                {
                    ItemId = entry.ItemId,
                    Count = random.Next(entry.MinCount, entry.MaxCount + 1)
                };
            }
        }
        
        return null;
    }
}

public class LootConfigData
{
    public Dictionary<string, LootTable> Tables { get; set; } = new();
    public Dictionary<string, AILootConfig> AILoot { get; set; } = new();
    public Dictionary<string, ContainerLootConfig> ContainerLoot { get; set; } = new();
}

public class LootTable
{
    public string TableId { get; set; } = "";
    public List<LootEntry> Entries { get; set; } = new();
}

public class LootEntry
{
    public string ItemId { get; set; } = "";
    public int Weight { get; set; } = 100;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
}

public class AILootConfig
{
    public string AIType { get; set; } = "";
    public int MinItems { get; set; } = 1;
    public int MaxItems { get; set; } = 3;
    public string[] LootTables { get; set; } = Array.Empty<string>();
    public List<GuaranteedItem> GuaranteedItems { get; set; } = new();
}

public class GuaranteedItem
{
    public string ItemId { get; set; } = "";
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public int Chance { get; set; } = 100;
}

public class ContainerLootConfig
{
    public string ContainerType { get; set; } = "";
    public int MinItems { get; set; } = 1;
    public int MaxItems { get; set; } = 3;
    public string[] LootTables { get; set; } = Array.Empty<string>();
}

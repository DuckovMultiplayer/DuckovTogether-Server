// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Core.Assets;

public class UnityAssetReader
{
    private static UnityAssetReader? _instance;
    public static UnityAssetReader Instance => _instance ??= new UnityAssetReader();
    
    private string? _gamePath;
    private AssetsManager? _assetsManager;
    private GameTypeAnalyzer? _typeAnalyzer;
    
    public Dictionary<int, ItemData> Items { get; } = new();
    public Dictionary<string, SceneData> Scenes { get; } = new();
    public Dictionary<string, AITypeData> AITypes { get; } = new();
    public Dictionary<string, LootTableData> LootTables { get; } = new();
    public Dictionary<string, string> Localization { get; } = new();
    public Dictionary<long, ScriptableObjectData> ScriptableObjects { get; } = new();
    
    public bool Initialize(string gamePath)
    {
        _gamePath = gamePath;
        
        if (!Directory.Exists(gamePath))
        {
            Log.Warn($"Game path not found: {gamePath}");
            return false;
        }
        
        var dataPath = Path.Combine(gamePath, "Duckov_Data");
        if (!Directory.Exists(dataPath))
        {
            Log.Warn($"Data path not found: {dataPath}");
            return false;
        }
        
        _typeAnalyzer = GameTypeAnalyzer.Instance;
        _typeAnalyzer.Initialize(gamePath);
        
        _assetsManager = new AssetsManager();
        _assetsManager.UseTemplateFieldCache = true;
        _assetsManager.UseMonoTemplateFieldCache = true;
        
        LoadClassDatabase(gamePath);
        
        LoadLocalization();
        
        if (!LoadExportedData())
        {
            Log.Info("No exported data found, trying native parsing...");
            
            if (!TryNativeParse())
            {
                Log.Info("Native parse incomplete, trying AssetsTools...");
                LoadGameAssets();
                ProcessExtractedData();
            }
        }
        
        Log.Info($"Loaded: {Items.Count} items, {Scenes.Count} scenes, {AITypes.Count} AI types");
        return true;
    }
    
    private void LoadClassDatabase(string gamePath)
    {
        var managedPath = Path.Combine(gamePath, "Duckov_Data", "Managed");
        if (!Directory.Exists(managedPath))
        {
            Log.Debug("Managed folder not found");
            return;
        }
        
        var assemblyPaths = new List<string>();
        var targetAssemblies = new[] { "Assembly-CSharp.dll", "TeamSoda.Duckov.Core.dll", "ItemStatsSystem.dll" };
        
        foreach (var asm in targetAssemblies)
        {
            var asmPath = Path.Combine(managedPath, asm);
            if (File.Exists(asmPath))
            {
                assemblyPaths.Add(asmPath);
            }
        }
        
        Log.Debug($"Found {assemblyPaths.Count} game assemblies");
    }
    
    private bool TryNativeParse()
    {
        var parser = NativeAssetParser.Instance;
        if (!parser.Parse(_gamePath!))
        {
            return false;
        }
        
        var extractor = GameDataExtractor.Instance;
        extractor.Extract(_gamePath!);
        extractor.SaveToFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "GameData"));
        
        foreach (var scene in extractor.Scenes.Values)
        {
            if (!Scenes.ContainsKey(scene.SceneId))
            {
                Scenes[scene.SceneId] = new SceneData
                {
                    SceneId = scene.SceneId,
                    BuildIndex = scene.BuildIndex
                };
            }
        }
        
        foreach (var item in parser.Items.Values)
        {
            if (!Items.ContainsKey(item.TypeId))
            {
                Items[item.TypeId] = new ItemData
                {
                    TypeId = item.TypeId,
                    Name = item.Name,
                    DisplayName = item.DisplayName,
                    Category = item.Category
                };
            }
        }
        
        foreach (var ai in parser.AITypes.Values)
        {
            if (!AITypes.ContainsKey(ai.TypeName))
            {
                AITypes[ai.TypeName] = new AITypeData
                {
                    TypeName = ai.TypeName,
                    MaxHealth = 100f,
                    MoveSpeed = 3.5f,
                    AttackDamage = 10f
                };
            }
        }
        
        parser.SaveToJson(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Parsed"));
        
        return Items.Count > 0;
    }
    
    private bool LoadExportedData()
    {
        var exportPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", "Team Soda", "Escape from Duckov", "ServerData"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Exported"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData")
        };
        
        string? exportPath = null;
        foreach (var path in exportPaths)
        {
            if (Directory.Exists(path))
            {
                exportPath = path;
                break;
            }
        }
        
        if (exportPath == null)
        {
            Log.Debug("Export paths checked:");
            foreach (var p in exportPaths)
            {
                Log.Debug($"  - {p}");
            }
            return false;
        }
        
        Log.Info($"Loading exported data from: {exportPath}");
        
        var itemsFile = Path.Combine(exportPath, "items.json");
        if (File.Exists(itemsFile))
        {
            LoadItemsFromJson(itemsFile);
        }
        
        var scenesDir = Path.Combine(exportPath, "scenes");
        if (Directory.Exists(scenesDir))
        {
            foreach (var sceneFile in Directory.GetFiles(scenesDir, "*.json"))
            {
                LoadSceneFromJson(sceneFile);
            }
        }
        
        return Items.Count > 0 || Scenes.Count > 0;
    }
    
    private void LoadItemsFromJson(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var items = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ExportedItemData>>(json);
            
            if (items != null)
            {
                foreach (var item in items)
                {
                    Items[item.TypeId] = new ItemData
                    {
                        TypeId = item.TypeId,
                        Name = item.Name ?? "",
                        DisplayName = item.DisplayName ?? item.Name ?? "",
                        Category = item.Category ?? "Unknown",
                        MaxStack = item.StackSize > 0 ? item.StackSize : 1,
                        Weight = item.Weight
                    };
                }
                Log.Debug($"Loaded {items.Count} items from JSON");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load items JSON: {ex.Message}");
        }
    }
    
    private void LoadSceneFromJson(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var scene = Newtonsoft.Json.JsonConvert.DeserializeObject<ExportedSceneData>(json);
            
            if (scene != null && !string.IsNullOrEmpty(scene.SceneId))
            {
                var sceneData = new SceneData
                {
                    SceneId = scene.SceneId,
                    BuildIndex = scene.BuildIndex
                };
                
                if (scene.AISpawns != null)
                {
                    foreach (var spawn in scene.AISpawns)
                    {
                        sceneData.AISpawns.Add(new AISpawnData
                        {
                            SpawnerId = spawn.SpawnerId,
                            Position = new Vector3Data
                            {
                                X = spawn.Position?.Length > 0 ? spawn.Position[0] : 0,
                                Y = spawn.Position?.Length > 1 ? spawn.Position[1] : 0,
                                Z = spawn.Position?.Length > 2 ? spawn.Position[2] : 0
                            },
                            AIType = spawn.SpawnerName ?? ""
                        });
                    }
                }
                
                if (scene.LootSpawns != null)
                {
                    foreach (var spawn in scene.LootSpawns)
                    {
                        sceneData.LootSpawns.Add(new LootSpawnData
                        {
                            ContainerId = spawn.ContainerId,
                            Position = new Vector3Data
                            {
                                X = spawn.Position?.Length > 0 ? spawn.Position[0] : 0,
                                Y = spawn.Position?.Length > 1 ? spawn.Position[1] : 0,
                                Z = spawn.Position?.Length > 2 ? spawn.Position[2] : 0
                            },
                            ContainerType = spawn.ContainerName ?? ""
                        });
                    }
                }
                
                Scenes[scene.SceneId] = sceneData;
                Log.Debug($"Loaded scene: {scene.SceneId} ({sceneData.AISpawns.Count} AI, {sceneData.LootSpawns.Count} loot)");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load scene JSON: {ex.Message}");
        }
    }
    
    private void LoadLocalization()
    {
        var locPath = Path.Combine(_gamePath!, "Duckov_Data", "StreamingAssets", "Localization", "English.csv");
        if (!File.Exists(locPath))
        {
            Log.Debug("Localization file not found");
            return;
        }
        
        try
        {
            var lines = File.ReadAllLines(locPath, Encoding.UTF8);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim().Trim('"').Replace("\\", "");
                    if (!string.IsNullOrEmpty(key))
                    {
                        Localization[key] = value;
                    }
                }
            }
            Log.Debug($"Loaded {Localization.Count} localization entries");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load localization: {ex.Message}");
        }
    }
    
    private void LoadGameAssets()
    {
        try
        {
            var ggmPath = Path.Combine(_gamePath!, "Duckov_Data", "globalgamemanagers");
            if (!File.Exists(ggmPath))
            {
                Log.Debug("globalgamemanagers not found");
                return;
            }
            
            var ggmAssetsPath = Path.Combine(_gamePath!, "Duckov_Data", "globalgamemanagers.assets");
            if (File.Exists(ggmAssetsPath))
            {
                LoadAssetsFile(ggmAssetsPath);
            }
            
            var resourcesPath = Path.Combine(_gamePath!, "Duckov_Data", "resources.assets");
            if (File.Exists(resourcesPath))
            {
                Log.Debug("Loading resources.assets...");
                LoadAssetsFile(resourcesPath);
            }
            
            for (int i = 0; i <= 49; i++)
            {
                var sharedPath = Path.Combine(_gamePath!, "Duckov_Data", $"sharedassets{i}.assets");
                if (File.Exists(sharedPath))
                {
                    Log.Debug($"Loading sharedassets{i}.assets...");
                    LoadAssetsFile(sharedPath);
                }
            }
            
            for (int i = 0; i <= 49; i++)
            {
                var levelPath = Path.Combine(_gamePath!, "Duckov_Data", $"level{i}");
                if (File.Exists(levelPath))
                {
                    LoadLevelFile(levelPath, i);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load assets: {ex.Message}");
        }
    }
    
    private void LoadAssetsFile(string path)
    {
        try
        {
            var assetsFileInst = _assetsManager!.LoadAssetsFile(path, true);
            var assetsFile = assetsFileInst.file;
            
            int monoCount = 0;
            int goCount = 0;
            var scriptNames = new HashSet<string>();
            var assetNames = new HashSet<string>();
            
            bool firstMono = true;
            foreach (var info in assetsFile.Metadata.AssetInfos)
            {
                try
                {
                    if (info.TypeId == (int)AssetClassID.MonoBehaviour)
                    {
                        monoCount++;
                        
                        var baseField = _assetsManager!.GetBaseField(assetsFileInst, info);
                        
                        if (firstMono && Path.GetFileName(path) == "resources.assets")
                        {
                            firstMono = false;
                            Log.Debug($"MonoBehaviour fields: {string.Join(", ", baseField.Children.Take(10).Select(c => c.FieldName))}");
                        }
                        
                        var name = baseField["m_Name"]?.AsString ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            assetNames.Add(name);
                        }
                        ProcessMonoBehaviour(assetsFileInst, info, scriptNames);
                    }
                    else if (info.TypeId == (int)AssetClassID.GameObject)
                    {
                        goCount++;
                        ProcessGameObject(assetsFileInst, info);
                    }
                }
                catch (Exception ex)
                {
                    if (firstMono && info.TypeId == (int)AssetClassID.MonoBehaviour)
                    {
                        Log.Debug($"MonoBehaviour read error: {ex.Message}");
                        firstMono = false;
                    }
                }
            }
            
            if (monoCount > 0 && assetNames.Count > 0)
            {
                var sampleNames = assetNames.Take(5);
                Log.Debug($"{Path.GetFileName(path)}: {monoCount} MonoBehaviours, samples: {string.Join(", ", sampleNames)}");
            }
            
            if (scriptNames.Count > 0)
            {
                var relevantScripts = scriptNames.Where(s => 
                    s.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
                    s.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
                    s.Contains("Loot", StringComparison.OrdinalIgnoreCase) ||
                    s.Contains("Weapon", StringComparison.OrdinalIgnoreCase) ||
                    s.Contains("Character", StringComparison.OrdinalIgnoreCase)
                ).Take(10);
                
                if (relevantScripts.Any())
                {
                    Log.Debug($"Found scripts: {string.Join(", ", relevantScripts)}");
                }
            }
            
            _assetsManager.UnloadAssetsFile(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load {path}: {ex.Message}");
        }
    }
    
    private void LoadLevelFile(string path, int levelIndex)
    {
        try
        {
            var assetsFileInst = _assetsManager!.LoadAssetsFile(path, true);
            var assetsFile = assetsFileInst.file;
            
            string sceneName = $"Level_{levelIndex}";
            var sceneData = new SceneData
            {
                SceneId = sceneName,
                BuildIndex = levelIndex,
                AISpawns = new List<AISpawnData>(),
                LootSpawns = new List<LootSpawnData>(),
                PlayerSpawns = new List<Vector3Data>()
            };
            
            foreach (var info in assetsFile.Metadata.AssetInfos)
            {
                try
                {
                    if (info.TypeId == (int)AssetClassID.GameObject)
                    {
                        var baseField = _assetsManager.GetBaseField(assetsFileInst, info);
                        var name = baseField["m_Name"].AsString;
                        
                        if (name.Contains("Spawner") || name.Contains("spawn", StringComparison.OrdinalIgnoreCase))
                        {
                            var transform = FindTransformData(assetsFileInst, baseField);
                            if (transform != null)
                            {
                                if (name.Contains("AI") || name.Contains("Character") || name.Contains("Enemy"))
                                {
                                    sceneData.AISpawns.Add(new AISpawnData
                                    {
                                        SpawnerId = info.PathId.GetHashCode(),
                                        Position = transform,
                                        AIType = ExtractAIType(name)
                                    });
                                }
                                else if (name.Contains("Player"))
                                {
                                    sceneData.PlayerSpawns.Add(transform);
                                }
                            }
                        }
                        else if (name.Contains("Loot") || name.Contains("Container") || name.Contains("Box"))
                        {
                            var transform = FindTransformData(assetsFileInst, baseField);
                            if (transform != null)
                            {
                                sceneData.LootSpawns.Add(new LootSpawnData
                                {
                                    ContainerId = info.PathId.GetHashCode(),
                                    Position = transform,
                                    ContainerType = ExtractContainerType(name)
                                });
                            }
                        }
                    }
                }
                catch { /* Ignore parse errors */ }
            }
            
            if (sceneData.AISpawns.Count > 0 || sceneData.LootSpawns.Count > 0 || sceneData.PlayerSpawns.Count > 0)
            {
                Scenes[sceneName] = sceneData;
            }
            
            _assetsManager.UnloadAssetsFile(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load level {levelIndex}: {ex.Message}");
        }
    }
    
    private void ProcessMonoBehaviour(AssetsFileInstance inst, AssetFileInfo info, HashSet<string>? scriptNames = null)
    {
        try
        {
            var baseField = _assetsManager!.GetBaseField(inst, info);
            var name = baseField["m_Name"]?.AsString ?? "";
            
            var scriptName = "";
            var scriptRef = baseField["m_Script"];
            
            if (scriptRef != null)
            {
                scriptName = GetScriptName(inst, scriptRef);
            }
            
            if (string.IsNullOrEmpty(scriptName) && !string.IsNullOrEmpty(name))
            {
                scriptName = InferScriptNameFromAssetName(name);
            }
            
            if (!string.IsNullOrEmpty(scriptName))
            {
                scriptNames?.Add(scriptName);
                
                var typeInfo = _typeAnalyzer?.GetTypeByScriptName(scriptName);
                if (typeInfo != null && (typeInfo.IsScriptableObject || typeInfo.IsMonoBehaviour))
                {
                    ExtractScriptableObject(baseField, info.PathId, scriptName, typeInfo);
                }
            }
            
            if (!string.IsNullOrEmpty(name))
            {
                ExtractFromAssetName(baseField, info.PathId, name, scriptName);
            }
        }
        catch { /* Ignore parse errors */ }
    }
    
    private string InferScriptNameFromAssetName(string assetName)
    {
        var patterns = new Dictionary<string, string[]>
        {
            { "Item", new[] { "Item_", "item_", "Weapon_", "weapon_", "Armor_", "armor_", "Consumable_", "Ammo_" } },
            { "AIData", new[] { "AI_", "ai_", "Enemy_", "enemy_", "NPC_", "npc_", "Character_", "Scav_", "PMC_" } },
            { "LootTable", new[] { "Loot_", "loot_", "Drop_", "drop_", "Spawn_", "spawn_" } }
        };
        
        foreach (var kvp in patterns)
        {
            if (kvp.Value.Any(p => assetName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                return kvp.Key;
            }
        }
        
        if (assetName.Contains("Item", StringComparison.OrdinalIgnoreCase)) return "Item";
        if (assetName.Contains("Weapon", StringComparison.OrdinalIgnoreCase)) return "Item";
        if (assetName.Contains("AI", StringComparison.OrdinalIgnoreCase)) return "AIData";
        if (assetName.Contains("Enemy", StringComparison.OrdinalIgnoreCase)) return "AIData";
        if (assetName.Contains("Loot", StringComparison.OrdinalIgnoreCase)) return "LootTable";
        
        return "";
    }
    
    private void ExtractFromAssetName(AssetTypeValueField baseField, long pathId, string name, string scriptName)
    {
        if (name.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Weapon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Armor", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ammo", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Medicine", StringComparison.OrdinalIgnoreCase))
        {
            ExtractItemData(baseField, pathId, string.IsNullOrEmpty(scriptName) ? "Item" : scriptName);
        }
        else if (name.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Enemy", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Scav", StringComparison.OrdinalIgnoreCase))
        {
            ExtractAITypeData(baseField, string.IsNullOrEmpty(scriptName) ? name : scriptName);
        }
        else if (name.Contains("Loot", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Drop", StringComparison.OrdinalIgnoreCase))
        {
            ExtractLootTableData(baseField);
        }
    }
    
    private void ExtractScriptableObject(AssetTypeValueField baseField, long pathId, string scriptName, GameTypeInfo typeInfo)
    {
        try
        {
            var soData = new ScriptableObjectData
            {
                PathId = pathId,
                ScriptName = scriptName,
                Name = baseField["m_Name"]?.AsString ?? "",
                TypeName = typeInfo.FullName
            };
            
            foreach (var field in baseField.Children)
            {
                var fieldName = field.FieldName;
                if (fieldName.StartsWith("m_") && fieldName != "m_Name" && fieldName != "m_Script")
                    continue;
                    
                var value = ExtractFieldValue(field);
                if (value != null)
                {
                    soData.Fields[fieldName] = value;
                }
            }
            
            ScriptableObjects[pathId] = soData;
        }
        catch { /* Ignore parse errors */ }
    }
    
    private object? ExtractFieldValue(AssetTypeValueField field)
    {
        try
        {
            var valueType = field.Value?.ValueType;
            
            if (valueType == null)
            {
                if (field.Children.Count > 0)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var child in field.Children)
                    {
                        dict[child.FieldName] = ExtractFieldValue(child);
                    }
                    return dict;
                }
                return null;
            }
            
            return valueType switch
            {
                AssetValueType.Bool => field.AsBool,
                AssetValueType.Int8 or AssetValueType.Int16 or AssetValueType.Int32 => field.AsInt,
                AssetValueType.Int64 => field.AsLong,
                AssetValueType.UInt8 or AssetValueType.UInt16 or AssetValueType.UInt32 => field.AsUInt,
                AssetValueType.UInt64 => field.AsULong,
                AssetValueType.Float => field.AsFloat,
                AssetValueType.Double => field.AsDouble,
                AssetValueType.String => field.AsString,
                AssetValueType.Array => ExtractArray(field),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
    
    private object? ExtractArray(AssetTypeValueField field)
    {
        try
        {
            var list = new List<object?>();
            foreach (var item in field.Children)
            {
                list.Add(ExtractFieldValue(item));
            }
            return list;
        }
        catch
        {
            return null;
        }
    }
    
    private void ProcessExtractedData()
    {
        foreach (var kvp in ScriptableObjects)
        {
            var so = kvp.Value;
            
            if (_typeAnalyzer?.ItemTypes.ContainsKey(so.TypeName) == true ||
                so.ScriptName.Contains("Item", StringComparison.OrdinalIgnoreCase))
            {
                ConvertToItemData(so);
            }
            else if (_typeAnalyzer?.AITypes.ContainsKey(so.TypeName) == true ||
                     so.ScriptName.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
                     so.ScriptName.Contains("Character", StringComparison.OrdinalIgnoreCase))
            {
                ConvertToAIData(so);
            }
            else if (_typeAnalyzer?.LootTypes.ContainsKey(so.TypeName) == true ||
                     so.ScriptName.Contains("Loot", StringComparison.OrdinalIgnoreCase))
            {
                ConvertToLootData(so);
            }
        }
    }
    
    private void ConvertToItemData(ScriptableObjectData so)
    {
        var itemId = (int)so.PathId;
        
        if (so.Fields.TryGetValue("id", out var idObj) && idObj is int id)
            itemId = id;
        else if (so.Fields.TryGetValue("itemId", out idObj) && idObj is int itemIdVal)
            itemId = itemIdVal;
        else if (so.Fields.TryGetValue("typeId", out idObj) && idObj is int typeId)
            itemId = typeId;
            
        var name = so.Name;
        var displayName = "";
        var maxStack = 1;
        var weight = 0.1f;
        
        if (so.Fields.TryGetValue("displayName", out var dnObj) && dnObj is string dn)
            displayName = dn;
        if (so.Fields.TryGetValue("maxStack", out var msObj) && msObj is int ms)
            maxStack = Math.Max(1, ms);
        if (so.Fields.TryGetValue("stackSize", out msObj) && msObj is int ss)
            maxStack = Math.Max(1, ss);
        if (so.Fields.TryGetValue("weight", out var wObj) && wObj is float w)
            weight = w;
        if (so.Fields.TryGetValue("mass", out wObj) && wObj is float m)
            weight = m;
            
        if (!Items.ContainsKey(itemId))
        {
            Items[itemId] = new ItemData
            {
                TypeId = itemId,
                Name = name,
                DisplayName = string.IsNullOrEmpty(displayName) ? GetLocalizedName(name) : displayName,
                Category = so.ScriptName,
                MaxStack = maxStack,
                Weight = weight
            };
        }
    }
    
    private void ConvertToAIData(ScriptableObjectData so)
    {
        var aiData = new AITypeData
        {
            TypeName = so.Name,
            MaxHealth = 100f,
            MoveSpeed = 3.5f,
            AttackDamage = 10f
        };
        
        if (so.Fields.TryGetValue("health", out var hObj) && hObj is float h)
            aiData.MaxHealth = h;
        if (so.Fields.TryGetValue("maxHealth", out hObj) && hObj is float mh)
            aiData.MaxHealth = mh;
        if (so.Fields.TryGetValue("speed", out var sObj) && sObj is float s)
            aiData.MoveSpeed = s;
        if (so.Fields.TryGetValue("moveSpeed", out sObj) && sObj is float ms)
            aiData.MoveSpeed = ms;
        if (so.Fields.TryGetValue("damage", out var dObj) && dObj is float d)
            aiData.AttackDamage = d;
        if (so.Fields.TryGetValue("attackDamage", out dObj) && dObj is float ad)
            aiData.AttackDamage = ad;
            
        if (!AITypes.ContainsKey(so.Name))
        {
            AITypes[so.Name] = aiData;
        }
    }
    
    private void ConvertToLootData(ScriptableObjectData so)
    {
        if (!LootTables.ContainsKey(so.Name))
        {
            LootTables[so.Name] = new LootTableData
            {
                TableName = so.Name,
                Items = new List<LootTableEntry>()
            };
        }
    }
    
    private void ProcessGameObject(AssetsFileInstance inst, AssetFileInfo info)
    {
    }
    
    private void ExtractItemData(AssetTypeValueField baseField, long pathId, string scriptName = "")
    {
        try
        {
            var typeId = 0;
            var name = baseField["m_Name"]?.AsString ?? "";
            var displayName = "";
            var maxStack = 1;
            var weight = 0.1f;
            var category = scriptName;
            
            foreach (var child in baseField.Children)
            {
                var fieldName = child.FieldName.ToLower();
                if (fieldName.Contains("typeid") || fieldName == "id" || fieldName == "itemid")
                    typeId = child.AsInt;
                else if (fieldName == "itemname" || fieldName == "displayname")
                    displayName = child.AsString ?? "";
                else if (fieldName.Contains("stack") || fieldName.Contains("max"))
                    maxStack = Math.Max(1, child.AsInt);
                else if (fieldName.Contains("weight") || fieldName.Contains("mass"))
                    weight = child.AsFloat;
                else if (fieldName.Contains("category") || fieldName.Contains("type"))
                    category = child.AsString ?? category;
            }
            
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(displayName))
                return;
            
            var itemId = typeId > 0 ? typeId : Math.Abs((int)pathId);
            
            if (!Items.ContainsKey(itemId))
            {
                Items[itemId] = new ItemData
                {
                    TypeId = itemId,
                    Name = name,
                    DisplayName = string.IsNullOrEmpty(displayName) ? GetLocalizedName(name) : displayName,
                    Category = category,
                    MaxStack = maxStack,
                    Weight = weight
                };
            }
        }
        catch { /* Ignore parse errors */ }
    }
    
    private void ExtractAITypeData(AssetTypeValueField baseField, string typeName)
    {
        try
        {
            var aiData = new AITypeData
            {
                TypeName = typeName,
                MaxHealth = 100f,
                MoveSpeed = 3.5f,
                AttackDamage = 10f
            };
            
            foreach (var child in baseField.Children)
            {
                var fieldName = child.FieldName.ToLower();
                if (fieldName.Contains("health") || fieldName.Contains("hp"))
                    aiData.MaxHealth = child.AsFloat;
                else if (fieldName.Contains("speed"))
                    aiData.MoveSpeed = child.AsFloat;
                else if (fieldName.Contains("damage") || fieldName.Contains("attack"))
                    aiData.AttackDamage = child.AsFloat;
            }
            
            AITypes[typeName] = aiData;
        }
        catch { /* Ignore parse errors */ }
    }
    
    private void ExtractLootTableData(AssetTypeValueField baseField)
    {
        try
        {
            var name = baseField["m_Name"]?.AsString ?? "";
            if (string.IsNullOrEmpty(name)) return;
            
            LootTables[name] = new LootTableData
            {
                TableName = name,
                Items = new List<LootTableEntry>()
            };
        }
        catch { /* Ignore parse errors */ }
    }
    
    private string GetScriptName(AssetsFileInstance inst, AssetTypeValueField scriptRef)
    {
        try
        {
            var fileId = scriptRef["m_FileID"].AsInt;
            var pathId = scriptRef["m_PathID"].AsLong;
            
            if (pathId == 0) return "";
            
            if (fileId == 0)
            {
                var scriptInfo = inst.file.Metadata.GetAssetInfo(pathId);
                if (scriptInfo != null)
                {
                    var scriptBase = _assetsManager!.GetBaseField(inst, scriptInfo);
                    return scriptBase["m_Name"]?.AsString ?? "";
                }
            }
            else
            {
                var deps = inst.file.Metadata.Externals;
                if (fileId > 0 && fileId <= deps.Count)
                {
                    var depPath = deps[fileId - 1].PathName;
                    var fullPath = Path.Combine(Path.GetDirectoryName(inst.path)!, depPath);
                    if (File.Exists(fullPath))
                    {
                        var depInst = _assetsManager!.LoadAssetsFile(fullPath, true);
                        var scriptInfo = depInst.file.Metadata.GetAssetInfo(pathId);
                        if (scriptInfo != null)
                        {
                            var scriptBase = _assetsManager.GetBaseField(depInst, scriptInfo);
                            return scriptBase["m_Name"]?.AsString ?? "";
                        }
                    }
                }
            }
            return "";
        }
        catch
        {
            return "";
        }
    }
    
    private Vector3Data? FindTransformData(AssetsFileInstance inst, AssetTypeValueField goField)
    {
        try
        {
            var components = goField["m_Component"];
            if (components == null) return null;
            
            foreach (var comp in components.Children)
            {
                var compRef = comp["component"];
                var pathId = compRef["m_PathID"].AsLong;
                if (pathId == 0) continue;
                
                var compInfo = inst.file.Metadata.GetAssetInfo(pathId);
                if (compInfo == null) continue;
                
                if (compInfo.TypeId == (int)AssetClassID.Transform)
                {
                    var transformField = _assetsManager!.GetBaseField(inst, compInfo);
                    var localPos = transformField["m_LocalPosition"];
                    
                    return new Vector3Data
                    {
                        X = localPos["x"].AsFloat,
                        Y = localPos["y"].AsFloat,
                        Z = localPos["z"].AsFloat
                    };
                }
            }
        }
        catch { /* Ignore parse errors */ }
        
        return null;
    }
    
    private string ExtractAIType(string name)
    {
        if (name.Contains("Scav")) return "Scav";
        if (name.Contains("PMC")) return "PMC";
        if (name.Contains("Boss")) return "Boss";
        if (name.Contains("Guard")) return "Guard";
        return "Default";
    }
    
    private string ExtractContainerType(string name)
    {
        if (name.Contains("Weapon")) return "Weapon";
        if (name.Contains("Medical")) return "Medical";
        if (name.Contains("Ammo")) return "Ammo";
        if (name.Contains("Rare")) return "Rare";
        return "Generic";
    }
    
    private string GetLocalizedName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (Localization.TryGetValue(key, out var value)) return value;
        if (Localization.TryGetValue($"Item_{key}", out value)) return value;
        return key;
    }
    
    public void SaveExtractedData(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        
        var itemsJson = Newtonsoft.Json.JsonConvert.SerializeObject(Items.Values.ToList(), Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(Path.Combine(outputPath, "items.json"), itemsJson);
        
        var scenesDir = Path.Combine(outputPath, "scenes");
        Directory.CreateDirectory(scenesDir);
        foreach (var scene in Scenes.Values)
        {
            var sceneJson = Newtonsoft.Json.JsonConvert.SerializeObject(scene, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Path.Combine(scenesDir, $"{scene.SceneId}.json"), sceneJson);
        }
        
        var aiTypesJson = Newtonsoft.Json.JsonConvert.SerializeObject(AITypes.Values.ToList(), Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(Path.Combine(outputPath, "ai_types.json"), aiTypesJson);
        
        var parsedDir = Path.Combine(outputPath, "Parsed");
        Directory.CreateDirectory(parsedDir);
        NativeAssetParser.Instance.SaveToJson(parsedDir);
        
        Log.Info($"Saved extracted data to: {outputPath}");
    }
}

public class ItemData
{
    public int TypeId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "Unknown";
    public int MaxStack { get; set; } = 1;
    public float Weight { get; set; } = 0.1f;
    public int Value { get; set; } = 100;
}

public class SceneData
{
    public string SceneId { get; set; } = "";
    public int BuildIndex { get; set; }
    public List<AISpawnData> AISpawns { get; set; } = new();
    public List<LootSpawnData> LootSpawns { get; set; } = new();
    public List<Vector3Data> PlayerSpawns { get; set; } = new();
}

public class AISpawnData
{
    public int SpawnerId { get; set; }
    public Vector3Data Position { get; set; } = new();
    public string AIType { get; set; } = "";
}

public class LootSpawnData
{
    public int ContainerId { get; set; }
    public Vector3Data Position { get; set; } = new();
    public string ContainerType { get; set; } = "";
}

public class Vector3Data
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public class AITypeData
{
    public string TypeName { get; set; } = "";
    public float MaxHealth { get; set; }
    public float MoveSpeed { get; set; }
    public float AttackDamage { get; set; }
}

public class LootTableData
{
    public string TableName { get; set; } = "";
    public List<LootTableEntry> Items { get; set; } = new();
}

public class LootTableEntry
{
    public int ItemId { get; set; }
    public float Weight { get; set; }
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
}

public class ScriptableObjectData
{
    public long PathId { get; set; }
    public string ScriptName { get; set; } = "";
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public Dictionary<string, object?> Fields { get; set; } = new();
}

public class ExportedItemData
{
    public int TypeId { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Category { get; set; }
    public int StackSize { get; set; }
    public float Weight { get; set; }
    public int Value { get; set; }
    public float Durability { get; set; }
}

public class ExportedSceneData
{
    public string? SceneId { get; set; }
    public int BuildIndex { get; set; }
    public List<ExportedAISpawnData>? AISpawns { get; set; }
    public List<ExportedLootSpawnData>? LootSpawns { get; set; }
    public List<float[]>? PlayerSpawns { get; set; }
}

public class ExportedAISpawnData
{
    public int SpawnerId { get; set; }
    public float[]? Position { get; set; }
    public string? SpawnerName { get; set; }
}

public class ExportedLootSpawnData
{
    public int ContainerId { get; set; }
    public float[]? Position { get; set; }
    public string? ContainerName { get; set; }
    public int Capacity { get; set; }
}

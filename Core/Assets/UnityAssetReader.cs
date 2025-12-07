using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;

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
            Console.WriteLine($"[AssetReader] Game path not found: {gamePath}");
            return false;
        }
        
        var dataPath = Path.Combine(gamePath, "Duckov_Data");
        if (!Directory.Exists(dataPath))
        {
            Console.WriteLine($"[AssetReader] Data path not found: {dataPath}");
            return false;
        }
        
        _typeAnalyzer = GameTypeAnalyzer.Instance;
        _typeAnalyzer.Initialize(gamePath);
        
        _assetsManager = new AssetsManager();
        
        LoadLocalization();
        LoadGameAssets();
        
        ProcessExtractedData();
        
        Console.WriteLine($"[AssetReader] Loaded: {Items.Count} items, {Scenes.Count} scenes, {AITypes.Count} AI types");
        Console.WriteLine($"[AssetReader] ScriptableObjects extracted: {ScriptableObjects.Count}");
        return true;
    }
    
    private void LoadLocalization()
    {
        var locPath = Path.Combine(_gamePath!, "Duckov_Data", "StreamingAssets", "Localization", "English.csv");
        if (!File.Exists(locPath))
        {
            Console.WriteLine("[AssetReader] Localization file not found");
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
            Console.WriteLine($"[AssetReader] Loaded {Localization.Count} localization entries");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetReader] Failed to load localization: {ex.Message}");
        }
    }
    
    private void LoadGameAssets()
    {
        try
        {
            var ggmPath = Path.Combine(_gamePath!, "Duckov_Data", "globalgamemanagers");
            if (!File.Exists(ggmPath))
            {
                Console.WriteLine("[AssetReader] globalgamemanagers not found");
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
                Console.WriteLine("[AssetReader] Loading resources.assets...");
                LoadAssetsFile(resourcesPath);
            }
            
            for (int i = 0; i <= 49; i++)
            {
                var sharedPath = Path.Combine(_gamePath!, "Duckov_Data", $"sharedassets{i}.assets");
                if (File.Exists(sharedPath))
                {
                    Console.WriteLine($"[AssetReader] Loading sharedassets{i}.assets...");
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
            Console.WriteLine($"[AssetReader] Failed to load assets: {ex.Message}");
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
            
            foreach (var info in assetsFile.Metadata.AssetInfos)
            {
                try
                {
                    if (info.TypeId == (int)AssetClassID.MonoBehaviour)
                    {
                        monoCount++;
                        ProcessMonoBehaviour(assetsFileInst, info, scriptNames);
                    }
                    else if (info.TypeId == (int)AssetClassID.GameObject)
                    {
                        goCount++;
                        ProcessGameObject(assetsFileInst, info);
                    }
                }
                catch { }
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
                    Console.WriteLine($"[AssetReader] Found scripts: {string.Join(", ", relevantScripts)}");
                }
            }
            
            _assetsManager.UnloadAssetsFile(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetReader] Failed to load {path}: {ex.Message}");
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
                catch { }
            }
            
            if (sceneData.AISpawns.Count > 0 || sceneData.LootSpawns.Count > 0 || sceneData.PlayerSpawns.Count > 0)
            {
                Scenes[sceneName] = sceneData;
            }
            
            _assetsManager.UnloadAssetsFile(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetReader] Failed to load level {levelIndex}: {ex.Message}");
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
        catch { }
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
        catch { }
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
        catch { }
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
        catch { }
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
        catch { }
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
        catch { }
        
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
        
        Console.WriteLine($"[AssetReader] Saved extracted data to: {outputPath}");
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

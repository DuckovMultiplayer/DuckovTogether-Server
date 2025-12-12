using System.Text;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Assets;

public class GameDataExtractor
{
    private static GameDataExtractor? _instance;
    public static GameDataExtractor Instance => _instance ??= new GameDataExtractor();
    
    public Dictionary<string, SceneInfo> Scenes { get; } = new();
    public Dictionary<string, PrefabInfo> Prefabs { get; } = new();
    public Dictionary<int, WeaponInfo> Weapons { get; } = new();
    public Dictionary<int, ArmorInfo> Armors { get; } = new();
    public Dictionary<string, SpawnPointInfo> SpawnPoints { get; } = new();
    public Dictionary<string, ExtractPointInfo> ExtractPoints { get; } = new();
    public Dictionary<string, LootContainerInfo> LootContainers { get; } = new();
    public Dictionary<string, DoorInfo> Doors { get; } = new();
    public Dictionary<string, QuestInfo> Quests { get; } = new();
    
    private readonly HashSet<string> _sceneKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "level", "scene", "map", "area", "zone", "region", "location"
    };
    
    private readonly HashSet<string> _weaponKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "gun", "rifle", "pistol", "shotgun", "smg", "sniper", "ak", "m4", "mp5", "glock", "ar15"
    };
    
    private readonly HashSet<string> _armorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "armor", "vest", "helmet", "plate", "kevlar", "rig", "carrier"
    };
    
    public void LoadKnownScenes(List<string> knownScenes)
    {
        if (knownScenes == null || knownScenes.Count == 0) return;
        
        foreach (var sceneName in knownScenes)
        {
            if (!string.IsNullOrWhiteSpace(sceneName) && !Scenes.ContainsKey(sceneName))
            {
                Scenes[sceneName] = new SceneInfo
                {
                    SceneId = sceneName,
                    BuildIndex = -1
                };
            }
        }
        Console.WriteLine($"[DataExtractor] Loaded {knownScenes.Count} known scenes from config");
    }
    
    public bool Extract(string gamePath)
    {
        var dataPath = Path.Combine(gamePath, "Duckov_Data");
        if (!Directory.Exists(dataPath)) return false;
        
        Console.WriteLine("[DataExtractor] Starting comprehensive game data extraction...");
        
        ExtractFromResources(Path.Combine(dataPath, "resources.assets"));
        ExtractFromSharedAssets(dataPath);
        ExtractSceneList(dataPath);
        ExtractFromLocalization(gamePath);
        
        Console.WriteLine($"[DataExtractor] Extracted:");
        Console.WriteLine($"  - Scenes: {Scenes.Count}");
        Console.WriteLine($"  - Prefabs: {Prefabs.Count}");
        Console.WriteLine($"  - Weapons: {Weapons.Count}");
        Console.WriteLine($"  - Armors: {Armors.Count}");
        Console.WriteLine($"  - SpawnPoints: {SpawnPoints.Count}");
        Console.WriteLine($"  - ExtractPoints: {ExtractPoints.Count}");
        Console.WriteLine($"  - LootContainers: {LootContainers.Count}");
        Console.WriteLine($"  - Doors: {Doors.Count}");
        Console.WriteLine($"  - Quests: {Quests.Count}");
        
        return true;
    }
    
    private void ExtractSceneList(string dataPath)
    {
        for (int i = 0; i <= 49; i++)
        {
            var levelPath = Path.Combine(dataPath, $"level{i}");
            if (File.Exists(levelPath))
            {
                var sceneName = $"Level_{i}";
                
                try
                {
                    var bytes = File.ReadAllBytes(levelPath);
                    var strings = ExtractStrings(bytes, 5, 80);
                    
                    var levelName = strings.FirstOrDefault(s => IsValidSceneName(s));
                    
                    if (!string.IsNullOrEmpty(levelName))
                    {
                        sceneName = levelName;
                    }
                }
                catch { /* Ignore extraction errors */ }
                
                Scenes[sceneName] = new SceneInfo
                {
                    SceneId = sceneName,
                    BuildIndex = i,
                    FilePath = levelPath
                };
            }
        }
        
        var globalgm = Path.Combine(dataPath, "globalgamemanagers");
        if (File.Exists(globalgm))
        {
            try
            {
                var bytes = File.ReadAllBytes(globalgm);
                var strings = ExtractStrings(bytes, 5, 100);
                
                foreach (var str in strings)
                {
                    if (IsValidSceneName(str) && !Scenes.ContainsKey(str))
                    {
                        Scenes[str] = new SceneInfo
                        {
                            SceneId = str,
                            BuildIndex = -1
                        };
                    }
                }
            }
            catch { /* Ignore extraction errors */ }
        }
    }
    
    private void ExtractFromResources(string path)
    {
        if (!File.Exists(path)) return;
        
        try
        {
            var bytes = File.ReadAllBytes(path);
            var strings = ExtractStrings(bytes, 4, 100);
            
            foreach (var str in strings)
            {
                ClassifyAndStore(str);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DataExtractor] Error reading resources: {ex.Message}");
        }
    }
    
    private void ExtractFromSharedAssets(string dataPath)
    {
        for (int i = 0; i <= 49; i++)
        {
            var sharedPath = Path.Combine(dataPath, $"sharedassets{i}.assets");
            if (!File.Exists(sharedPath)) continue;
            
            try
            {
                var bytes = File.ReadAllBytes(sharedPath);
                var strings = ExtractStrings(bytes, 4, 100);
                
                foreach (var str in strings)
                {
                    ClassifyAndStore(str, $"sharedassets{i}");
                }
            }
            catch { /* Ignore extraction errors */ }
        }
    }
    
    private void ExtractFromLocalization(string gamePath)
    {
        var locPath = Path.Combine(gamePath, "Duckov_Data", "StreamingAssets", "Localization", "English.csv");
        if (!File.Exists(locPath)) return;
        
        try
        {
            var lines = File.ReadAllLines(locPath, Encoding.UTF8);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    
                    if (key.StartsWith("Quest_") || key.StartsWith("Task_"))
                    {
                        var questId = key.Split('_')[0] + "_" + key.Split('_')[1];
                        if (!Quests.ContainsKey(questId))
                        {
                            Quests[questId] = new QuestInfo { QuestId = questId };
                        }
                        
                        if (key.Contains("_Name") || key.Contains("_Title"))
                        {
                            Quests[questId].Name = value;
                        }
                        else if (key.Contains("_Desc"))
                        {
                            Quests[questId].Description = value;
                        }
                    }
                    else if (key.StartsWith("Location_") || key.StartsWith("Extract_"))
                    {
                        var pointId = key;
                        if (!ExtractPoints.ContainsKey(pointId))
                        {
                            ExtractPoints[pointId] = new ExtractPointInfo
                            {
                                PointId = pointId,
                                DisplayName = value
                            };
                        }
                    }
                }
            }
        }
        catch { /* Ignore extraction errors */ }
    }
    
    private void ClassifyAndStore(string name, string source = "")
    {
        if (!IsValidName(name)) return;
        
        var lower = name.ToLower();
        
        if (IsWeaponName(lower))
        {
            var id = GenerateId(name);
            if (!Weapons.ContainsKey(id))
            {
                Weapons[id] = new WeaponInfo
                {
                    WeaponId = id,
                    Name = name,
                    Category = DetectWeaponCategory(lower),
                    Source = source
                };
            }
        }
        else if (IsArmorName(lower))
        {
            var id = GenerateId(name);
            if (!Armors.ContainsKey(id))
            {
                Armors[id] = new ArmorInfo
                {
                    ArmorId = id,
                    Name = name,
                    Category = DetectArmorCategory(lower),
                    Source = source
                };
            }
        }
        else if (IsSpawnPoint(lower))
        {
            if (!SpawnPoints.ContainsKey(name))
            {
                SpawnPoints[name] = new SpawnPointInfo
                {
                    PointId = name,
                    PointType = DetectSpawnType(lower),
                    Source = source
                };
            }
        }
        else if (IsLootContainer(lower))
        {
            if (!LootContainers.ContainsKey(name))
            {
                LootContainers[name] = new LootContainerInfo
                {
                    ContainerId = name,
                    ContainerType = DetectContainerType(lower),
                    Source = source
                };
            }
        }
        else if (IsDoor(lower))
        {
            if (!Doors.ContainsKey(name))
            {
                Doors[name] = new DoorInfo
                {
                    DoorId = name,
                    RequiresKey = lower.Contains("key") || lower.Contains("lock"),
                    Source = source
                };
            }
        }
        else if (IsPrefab(lower))
        {
            if (!Prefabs.ContainsKey(name))
            {
                Prefabs[name] = new PrefabInfo
                {
                    PrefabId = name,
                    Category = DetectPrefabCategory(lower),
                    Source = source
                };
            }
        }
    }
    
    private bool IsValidName(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        if (str.Length < 4 || str.Length > 80) return false;
        if (str.Contains("\\") || str.Contains("/")) return false;
        if (str.All(char.IsDigit)) return false;
        if (str.StartsWith("m_") || str.StartsWith("k__") || str.StartsWith("_")) return false;
        if (str.Contains("<") || str.Contains(">")) return false;
        
        var lower = str.ToLower();
        var excludes = new[] { "material", "shader", "atlas", "texture", "font", "sprite", 
                               "script", "handler", "manager", "controller", "fx", "effect" };
        return !excludes.Any(e => lower.Contains(e));
    }
    
    private bool IsValidSceneName(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        if (str.Length < 4 || str.Length > 60) return false;
        if (str.Contains(".") || str.Contains("/") || str.Contains("\\")) return false;
        if (str.Contains("<") || str.Contains(">") || str.Contains("(")) return false;
        if (str.All(char.IsDigit)) return false;
        if (str.StartsWith("m_") || str.StartsWith("k__") || str.StartsWith("_")) return false;
        
        var lower = str.ToLower();
        
        var scenePatterns = new[] { "level", "scene", "map", "base_", "area", "zone", 
                                     "startup", "mainmenu", "loading", "intro" };
        if (!scenePatterns.Any(p => lower.Contains(p))) return false;
        
        var excludes = new[] { "material", "shader", "texture", "script", "prefab", 
                               "controller", "manager", "handler", "component", "asset" };
        if (excludes.Any(e => lower.Contains(e))) return false;
        
        return true;
    }
    
    private bool IsWeaponName(string lower) =>
        _weaponKeywords.Any(k => lower.Contains(k)) || 
        lower.Contains("weapon") || lower.Contains("firearm");
    
    private bool IsArmorName(string lower) =>
        _armorKeywords.Any(k => lower.Contains(k));
    
    private bool IsSpawnPoint(string lower) =>
        lower.Contains("spawn") || lower.Contains("spawnpoint") || 
        lower.Contains("playerstart") || lower.Contains("startpoint");
    
    private bool IsLootContainer(string lower) =>
        lower.Contains("lootbox") || lower.Contains("container") || 
        lower.Contains("crate") || lower.Contains("chest") || lower.Contains("stash");
    
    private bool IsDoor(string lower) =>
        lower.Contains("door") || lower.Contains("gate") || lower.Contains("entrance");
    
    private bool IsPrefab(string lower) =>
        lower.Contains("prefab") || lower.Contains("prop") || 
        lower.Contains("object") || lower.Contains("entity");
    
    private string DetectWeaponCategory(string lower)
    {
        if (lower.Contains("rifle") || lower.Contains("ak") || lower.Contains("m4") || lower.Contains("ar")) return "AssaultRifle";
        if (lower.Contains("pistol") || lower.Contains("glock") || lower.Contains("handgun")) return "Pistol";
        if (lower.Contains("shotgun")) return "Shotgun";
        if (lower.Contains("smg") || lower.Contains("mp5") || lower.Contains("mp7")) return "SMG";
        if (lower.Contains("sniper") || lower.Contains("bolt")) return "SniperRifle";
        if (lower.Contains("melee") || lower.Contains("knife") || lower.Contains("axe")) return "Melee";
        return "Other";
    }
    
    private string DetectArmorCategory(string lower)
    {
        if (lower.Contains("helmet") || lower.Contains("head")) return "Helmet";
        if (lower.Contains("vest") || lower.Contains("carrier") || lower.Contains("plate")) return "BodyArmor";
        if (lower.Contains("rig")) return "TacticalRig";
        return "Other";
    }
    
    private string DetectSpawnType(string lower)
    {
        if (lower.Contains("player")) return "Player";
        if (lower.Contains("scav") || lower.Contains("enemy") || lower.Contains("ai")) return "AI";
        if (lower.Contains("loot")) return "Loot";
        return "Generic";
    }
    
    private string DetectContainerType(string lower)
    {
        if (lower.Contains("weapon")) return "Weapon";
        if (lower.Contains("medical") || lower.Contains("med")) return "Medical";
        if (lower.Contains("ammo")) return "Ammo";
        if (lower.Contains("rare") || lower.Contains("valuable")) return "Rare";
        if (lower.Contains("food")) return "Food";
        return "Generic";
    }
    
    private string DetectPrefabCategory(string lower)
    {
        if (lower.Contains("character") || lower.Contains("player")) return "Character";
        if (lower.Contains("weapon") || lower.Contains("gun")) return "Weapon";
        if (lower.Contains("vehicle") || lower.Contains("car")) return "Vehicle";
        if (lower.Contains("building") || lower.Contains("structure")) return "Building";
        return "Prop";
    }
    
    private List<string> ExtractStrings(byte[] data, int minLength, int maxLength)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] >= 32 && data[i] < 127)
            {
                sb.Append((char)data[i]);
            }
            else
            {
                if (sb.Length >= minLength && sb.Length <= maxLength)
                {
                    result.Add(sb.ToString());
                }
                sb.Clear();
            }
        }
        
        return result.Distinct().ToList();
    }
    
    private int GenerateId(string name)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in name) hash = hash * 31 + c;
            return Math.Abs(hash);
        }
    }
    
    public void SaveToFolder(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        
        SaveJson(Path.Combine(outputPath, "scenes.json"), Scenes.Values.ToList());
        SaveJson(Path.Combine(outputPath, "prefabs.json"), Prefabs.Values.ToList());
        SaveJson(Path.Combine(outputPath, "weapons.json"), Weapons.Values.ToList());
        SaveJson(Path.Combine(outputPath, "armors.json"), Armors.Values.ToList());
        SaveJson(Path.Combine(outputPath, "spawn_points.json"), SpawnPoints.Values.ToList());
        SaveJson(Path.Combine(outputPath, "extract_points.json"), ExtractPoints.Values.ToList());
        SaveJson(Path.Combine(outputPath, "loot_containers.json"), LootContainers.Values.ToList());
        SaveJson(Path.Combine(outputPath, "doors.json"), Doors.Values.ToList());
        SaveJson(Path.Combine(outputPath, "quests.json"), Quests.Values.ToList());
        
        var summary = new GameDataSummary
        {
            ExtractedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            SceneCount = Scenes.Count,
            PrefabCount = Prefabs.Count,
            WeaponCount = Weapons.Count,
            ArmorCount = Armors.Count,
            SpawnPointCount = SpawnPoints.Count,
            ExtractPointCount = ExtractPoints.Count,
            LootContainerCount = LootContainers.Count,
            DoorCount = Doors.Count,
            QuestCount = Quests.Count
        };
        SaveJson(Path.Combine(outputPath, "summary.json"), summary);
        
        Console.WriteLine($"[DataExtractor] Saved all game data to: {outputPath}");
    }
    
    private void SaveJson<T>(string path, T data)
    {
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        File.WriteAllText(path, json);
    }
}

public class SceneInfo
{
    public string SceneId { get; set; } = "";
    public int BuildIndex { get; set; }
    public string FilePath { get; set; } = "";
    public List<string> SpawnPoints { get; set; } = new();
    public List<string> ExtractPoints { get; set; } = new();
}

public class PrefabInfo
{
    public string PrefabId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Source { get; set; } = "";
}

public class WeaponInfo
{
    public int WeaponId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Source { get; set; } = "";
    public float Damage { get; set; }
    public float FireRate { get; set; }
    public int MagazineSize { get; set; }
}

public class ArmorInfo
{
    public int ArmorId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Source { get; set; } = "";
    public float Protection { get; set; }
    public float Durability { get; set; }
}

public class SpawnPointInfo
{
    public string PointId { get; set; } = "";
    public string PointType { get; set; } = "";
    public string Source { get; set; } = "";
    public float[] Position { get; set; } = Array.Empty<float>();
}

public class ExtractPointInfo
{
    public string PointId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string[] Requirements { get; set; } = Array.Empty<string>();
    public float ExtractTime { get; set; }
}

public class LootContainerInfo
{
    public string ContainerId { get; set; } = "";
    public string ContainerType { get; set; } = "";
    public string Source { get; set; } = "";
    public int Capacity { get; set; }
}

public class DoorInfo
{
    public string DoorId { get; set; } = "";
    public bool RequiresKey { get; set; }
    public string KeyId { get; set; } = "";
    public string Source { get; set; } = "";
}

public class QuestInfo
{
    public string QuestId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] Objectives { get; set; } = Array.Empty<string>();
}

public class GameDataSummary
{
    public string ExtractedAt { get; set; } = "";
    public int SceneCount { get; set; }
    public int PrefabCount { get; set; }
    public int WeaponCount { get; set; }
    public int ArmorCount { get; set; }
    public int SpawnPointCount { get; set; }
    public int ExtractPointCount { get; set; }
    public int LootContainerCount { get; set; }
    public int DoorCount { get; set; }
    public int QuestCount { get; set; }
}

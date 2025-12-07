using Newtonsoft.Json;

namespace DuckovTogether.Core.Assets;

public class SceneDataManager
{
    private static SceneDataManager? _instance;
    public static SceneDataManager Instance => _instance ??= new SceneDataManager();
    
    private Dictionary<string, ParsedSceneData> _scenes = new();
    private Dictionary<string, ExtractPointData> _extractPoints = new();
    private Dictionary<string, DoorData> _doors = new();
    private Dictionary<string, LootContainerData> _lootContainers = new();
    private Dictionary<int, WeaponData> _weapons = new();
    private Dictionary<int, ParsedItemData> _items = new();
    private List<AIData> _aiTypes = new();
    private List<QuestData> _quests = new();
    
    public IReadOnlyDictionary<string, ParsedSceneData> Scenes => _scenes;
    public IReadOnlyDictionary<string, ExtractPointData> ExtractPoints => _extractPoints;
    public IReadOnlyDictionary<string, DoorData> Doors => _doors;
    public IReadOnlyDictionary<int, WeaponData> Weapons => _weapons;
    public IReadOnlyDictionary<int, ParsedItemData> Items => _items;
    public IReadOnlyList<AIData> AITypes => _aiTypes;
    
    public void LoadFromDirectory(string dataPath)
    {
        var gameDataPath = Path.Combine(dataPath, "GameData");
        var parsedPath = Path.Combine(dataPath, "Parsed");
        
        LoadScenes(Path.Combine(gameDataPath, "scenes.json"));
        LoadExtractPoints(Path.Combine(gameDataPath, "extract_points.json"));
        LoadDoors(Path.Combine(gameDataPath, "doors.json"));
        LoadLootContainers(Path.Combine(gameDataPath, "loot_containers.json"));
        LoadWeapons(Path.Combine(gameDataPath, "weapons.json"));
        LoadQuests(Path.Combine(gameDataPath, "quests.json"));
        LoadItems(Path.Combine(parsedPath, "parsed_items.json"));
        LoadAI(Path.Combine(parsedPath, "parsed_ai.json"));
        
        Console.WriteLine($"[SceneData] Loaded: {_scenes.Count} scenes, {_extractPoints.Count} extracts, {_doors.Count} doors, {_weapons.Count} weapons, {_items.Count} items, {_aiTypes.Count} AI types");
    }
    
    public ParsedSceneData? GetScene(string sceneId) => _scenes.GetValueOrDefault(sceneId);
    public ExtractPointData? GetExtract(string pointId) => _extractPoints.GetValueOrDefault(pointId);
    public DoorData? GetDoor(string doorId) => _doors.GetValueOrDefault(doorId);
    public WeaponData? GetWeapon(int weaponId) => _weapons.GetValueOrDefault(weaponId);
    public ParsedItemData? GetItem(int typeId) => _items.GetValueOrDefault(typeId);
    
    public bool IsValidScene(string sceneId) => _scenes.ContainsKey(sceneId);
    public bool IsValidExtract(string pointId) => _extractPoints.ContainsKey(pointId);
    public bool IsValidWeapon(int weaponId) => _weapons.ContainsKey(weaponId);
    public bool IsValidItem(int typeId) => _items.ContainsKey(typeId);
    
    private void LoadScenes(string path)
    {
        if (!File.Exists(path)) return;
        var list = JsonConvert.DeserializeObject<List<ParsedSceneData>>(File.ReadAllText(path));
        if (list == null) return;
        foreach (var s in list) _scenes[s.SceneId] = s;
    }
    
    private void LoadExtractPoints(string path)
    {
        if (!File.Exists(path)) return;
        var list = JsonConvert.DeserializeObject<List<ExtractPointData>>(File.ReadAllText(path));
        if (list == null) return;
        foreach (var e in list) _extractPoints[e.PointId] = e;
    }
    
    private void LoadDoors(string path)
    {
        if (!File.Exists(path)) return;
        var list = JsonConvert.DeserializeObject<List<DoorData>>(File.ReadAllText(path));
        if (list == null) return;
        foreach (var d in list) _doors[d.DoorId] = d;
    }
    
    private void LoadLootContainers(string path)
    {
        if (!File.Exists(path)) return;
        var list = JsonConvert.DeserializeObject<List<LootContainerData>>(File.ReadAllText(path));
        if (list == null) return;
        foreach (var c in list) _lootContainers[c.ContainerId] = c;
    }
    
    private void LoadWeapons(string path)
    {
        if (!File.Exists(path)) return;
        var list = JsonConvert.DeserializeObject<List<WeaponData>>(File.ReadAllText(path));
        if (list == null) return;
        foreach (var w in list) _weapons[w.WeaponId] = w;
    }
    
    private void LoadQuests(string path)
    {
        if (!File.Exists(path)) return;
        _quests = JsonConvert.DeserializeObject<List<QuestData>>(File.ReadAllText(path)) ?? new();
    }
    
    private void LoadItems(string path)
    {
        if (!File.Exists(path)) return;
        var list = JsonConvert.DeserializeObject<List<ParsedItemData>>(File.ReadAllText(path));
        if (list == null) return;
        foreach (var i in list) _items[i.TypeId] = i;
    }
    
    private void LoadAI(string path)
    {
        if (!File.Exists(path)) return;
        _aiTypes = JsonConvert.DeserializeObject<List<AIData>>(File.ReadAllText(path)) ?? new();
    }
}

public class ExtractPointData
{
    public string PointId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Requirements { get; set; } = new();
    public float ExtractTime { get; set; }
}

public class DoorData
{
    public string DoorId { get; set; } = "";
    public bool RequiresKey { get; set; }
    public string KeyId { get; set; } = "";
    public string Source { get; set; } = "";
}

public class LootContainerData
{
    public string ContainerId { get; set; } = "";
    public string ContainerType { get; set; } = "";
    public string Source { get; set; } = "";
    public int Capacity { get; set; }
}

public class WeaponData
{
    public int WeaponId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Source { get; set; } = "";
    public float Damage { get; set; }
    public float FireRate { get; set; }
    public int MagazineSize { get; set; }
}

public class AIData
{
    public string TypeName { get; set; } = "";
    public string Category { get; set; } = "";
}

public class QuestData
{
    public string QuestId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Objectives { get; set; } = new();
}

using Newtonsoft.Json;

namespace DuckovTogether.Core.Assets;

public class BuildingDataExtractor
{
    private static BuildingDataExtractor? _instance;
    public static BuildingDataExtractor Instance => _instance ??= new BuildingDataExtractor();
    
    public Dictionary<string, BuildingDefinition> Buildings { get; } = new();
    
    public bool ExtractFromItems(string itemsJsonPath)
    {
        if (!File.Exists(itemsJsonPath))
        {
            Console.WriteLine($"[BuildingExtractor] Items file not found: {itemsJsonPath}");
            return false;
        }
        
        try
        {
            var json = File.ReadAllText(itemsJsonPath);
            var items = JsonConvert.DeserializeObject<List<ItemEntry>>(json);
            
            if (items == null)
            {
                Console.WriteLine("[BuildingExtractor] Failed to parse items.json");
                return false;
            }
            
            foreach (var item in items)
            {
                if (IsBuildingItem(item.Name))
                {
                    var buildingId = ExtractBuildingId(item.Name);
                    if (string.IsNullOrEmpty(buildingId)) continue;
                    
                    if (!Buildings.ContainsKey(buildingId))
                    {
                        Buildings[buildingId] = new BuildingDefinition
                        {
                            BuildingId = buildingId,
                            TypeId = item.TypeId,
                            Name = CleanBuildingName(item.Name),
                            DisplayName = CleanDisplayName(item.DisplayName),
                            Category = DetectBuildingCategory(item.Name),
                            SourceCategory = item.Category
                        };
                    }
                    else if (item.Name.Contains("_Desc"))
                    {
                        Buildings[buildingId].Description = ExtractDescription(item.Name, item.DisplayName);
                    }
                }
            }
            
            Console.WriteLine($"[BuildingExtractor] Extracted {Buildings.Count} building definitions");
            return Buildings.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BuildingExtractor] Error: {ex.Message}");
            return false;
        }
    }
    
    private bool IsBuildingItem(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.StartsWith("Building_", StringComparison.OrdinalIgnoreCase);
    }
    
    private string ExtractBuildingId(string name)
    {
        if (!name.StartsWith("Building_")) return "";
        
        var parts = name.Split(',');
        var baseName = parts[0].Trim();
        
        if (baseName.EndsWith("_Desc"))
        {
            baseName = baseName.Substring(0, baseName.Length - 5);
        }
        
        return baseName;
    }
    
    private string CleanBuildingName(string name)
    {
        var parts = name.Split(',');
        return parts[0].Trim().Replace("_Desc", "");
    }
    
    private string CleanDisplayName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return "";
        
        var parts = displayName.Split(',');
        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            return parts[1].Trim();
        }
        
        return parts[0].Replace("Building ", "").Replace("building ", "").Trim();
    }
    
    private string ExtractDescription(string name, string displayName)
    {
        var parts = displayName.Split(',');
        if (parts.Length >= 2)
        {
            return parts[1].Trim();
        }
        return "";
    }
    
    private string DetectBuildingCategory(string name)
    {
        var lower = name.ToLower();
        
        if (lower.Contains("workbench") || lower.Contains("craft")) return "Crafting";
        if (lower.Contains("merchant") || lower.Contains("shop") || lower.Contains("trader")) return "Merchant";
        if (lower.Contains("foodtable") || lower.Contains("dining") || lower.Contains("kitchen")) return "Food";
        if (lower.Contains("questgiver") || lower.Contains("billboard")) return "Quest";
        if (lower.Contains("masterkey") || lower.Contains("key")) return "Utility";
        if (lower.Contains("weapon")) return "WeaponStation";
        if (lower.Contains("equipment") || lower.Contains("armor")) return "ArmorStation";
        if (lower.Contains("medical") || lower.Contains("heal")) return "Medical";
        if (lower.Contains("storage") || lower.Contains("stash")) return "Storage";
        if (lower.Contains("generator") || lower.Contains("power")) return "Power";
        
        return "Generic";
    }
    
    public void SaveToJson(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? outputPath);
        
        var json = JsonConvert.SerializeObject(Buildings.Values.ToList(), Formatting.Indented);
        File.WriteAllText(outputPath, json);
        
        Console.WriteLine($"[BuildingExtractor] Saved {Buildings.Count} buildings to: {outputPath}");
    }
    
    private class ItemEntry
    {
        public int TypeId { get; set; }
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public int MaxStack { get; set; }
        public float Weight { get; set; }
        public int Value { get; set; }
    }
}

public class BuildingDefinition
{
    public string BuildingId { get; set; } = "";
    public int TypeId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Generic";
    public string SourceCategory { get; set; } = "";
    public int MaxLevel { get; set; } = 3;
    public List<BuildingRequirement> Requirements { get; set; } = new();
    public List<BuildingUpgrade> Upgrades { get; set; } = new();
}

public class BuildingRequirement
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; } = 1;
}

public class BuildingUpgrade
{
    public int Level { get; set; }
    public List<BuildingRequirement> Requirements { get; set; } = new();
    public string Effect { get; set; } = "";
}

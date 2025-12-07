using System.Text;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Assets;

public class NativeAssetParser
{
    private static NativeAssetParser? _instance;
    public static NativeAssetParser Instance => _instance ??= new NativeAssetParser();
    
    public Dictionary<int, ParsedItemData> Items { get; } = new();
    public Dictionary<string, ParsedSceneData> Scenes { get; } = new();
    public Dictionary<string, ParsedAIData> AITypes { get; } = new();
    
    private readonly HashSet<string> _itemKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "item", "weapon", "armor", "ammo", "medicine", "food", "key", "quest",
        "equipment", "consumable", "material", "tool", "grenade", "attachment"
    };
    
    private readonly HashSet<string> _aiKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "scav", "pmc", "boss", "guard", "enemy", "character", "npc", "ai"
    };
    
    public bool Parse(string gamePath)
    {
        var dataPath = Path.Combine(gamePath, "Duckov_Data");
        if (!Directory.Exists(dataPath))
        {
            Console.WriteLine($"[NativeParser] Data path not found: {dataPath}");
            return false;
        }
        
        Console.WriteLine("[NativeParser] Starting native asset parsing...");
        
        ParseResourcesAsset(Path.Combine(dataPath, "resources.assets"));
        
        for (int i = 0; i <= 49; i++)
        {
            var sharedPath = Path.Combine(dataPath, $"sharedassets{i}.assets");
            if (File.Exists(sharedPath))
            {
                ParseSharedAsset(sharedPath, i);
            }
        }
        
        Console.WriteLine($"[NativeParser] Parsed: {Items.Count} items, {Scenes.Count} scenes, {AITypes.Count} AI types");
        return Items.Count > 0 || Scenes.Count > 0;
    }
    
    private void ParseResourcesAsset(string path)
    {
        if (!File.Exists(path)) return;
        
        try
        {
            var bytes = File.ReadAllBytes(path);
            var strings = ExtractStrings(bytes, 4, 100);
            
            int itemCount = 0;
            int aiCount = 0;
            
            foreach (var str in strings)
            {
                if (IsItemName(str))
                {
                    var id = GenerateStableId(str);
                    if (!Items.ContainsKey(id))
                    {
                        Items[id] = new ParsedItemData
                        {
                            TypeId = id,
                            Name = str,
                            DisplayName = CleanDisplayName(str),
                            Category = DetectItemCategory(str)
                        };
                        itemCount++;
                    }
                }
                else if (IsAIName(str))
                {
                    if (!AITypes.ContainsKey(str))
                    {
                        AITypes[str] = new ParsedAIData
                        {
                            TypeName = str,
                            Category = DetectAICategory(str)
                        };
                        aiCount++;
                    }
                }
            }
            
            Console.WriteLine($"[NativeParser] resources.assets: {itemCount} items, {aiCount} AI types");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeParser] Error parsing resources: {ex.Message}");
        }
    }
    
    private void ParseSharedAsset(string path, int index)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var strings = ExtractStrings(bytes, 4, 80);
            
            int itemCount = 0;
            foreach (var str in strings)
            {
                if (IsItemName(str))
                {
                    var id = GenerateStableId(str);
                    if (!Items.ContainsKey(id))
                    {
                        Items[id] = new ParsedItemData
                        {
                            TypeId = id,
                            Name = str,
                            DisplayName = CleanDisplayName(str),
                            Category = DetectItemCategory(str),
                            SourceFile = $"sharedassets{index}"
                        };
                        itemCount++;
                    }
                }
            }
            
            if (itemCount > 0)
            {
                Console.WriteLine($"[NativeParser] sharedassets{index}: {itemCount} items");
            }
        }
        catch { }
    }
    
    private List<string> ExtractStrings(byte[] data, int minLength, int maxLength)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] >= 32 && data[i] < 127)
            {
                sb.Append((char)data[i]);
            }
            else
            {
                if (sb.Length >= minLength && sb.Length <= maxLength)
                {
                    var str = sb.ToString();
                    if (IsValidAssetName(str))
                    {
                        result.Add(str);
                    }
                }
                sb.Clear();
            }
        }
        
        return result.Distinct().ToList();
    }
    
    private bool IsValidAssetName(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        if (str.Contains("\\") || str.Contains("/")) return false;
        if (str.All(char.IsDigit)) return false;
        if (str.Contains("  ")) return false;
        if (str.StartsWith("m_") || str.StartsWith("k__")) return false;
        if (str.Contains("<") || str.Contains(">")) return false;
        return true;
    }
    
    private bool IsExcludedName(string str)
    {
        if (str.StartsWith("_")) return true;
        
        var lower = str.ToLower();
        var excludePatterns = new[] {
            "material", "shader", "atlas", "texture", "font", "sprite",
            ".mat", ".shader", "mat_", "_mat", "script", "handler",
            "manager", "controller", "system", "service", "fx", "effect",
            "smoke", "spark", "trail", "particle", "vfx", "sfx"
        };
        
        return excludePatterns.Any(p => lower.Contains(p));
    }
    
    private bool IsItemName(string str)
    {
        if (str.Length < 4 || str.Length > 60) return false;
        if (IsExcludedName(str)) return false;
        
        var lower = str.ToLower();
        
        foreach (var keyword in _itemKeywords)
        {
            if (lower.Contains(keyword))
            {
                return true;
            }
        }
        
        if (str.Contains("_") && !str.Contains(" "))
        {
            var parts = str.Split('_');
            if (parts.Length >= 2 && parts.Any(p => _itemKeywords.Contains(p.ToLower())))
            {
                return true;
            }
        }
        
        return false;
    }
    
    private bool IsAIName(string str)
    {
        if (str.Length < 3 || str.Length > 40) return false;
        if (str.StartsWith("_") || str.Contains("_") && str.Split('_').Length > 3) return false;
        
        var lower = str.ToLower();
        
        var excludePatterns = new[] { 
            "script", "handler", "manager", "controller", "system", "service",
            "fx", "effect", "smoke", "spark", "trail", "shot", "speedy",
            "mat", "material", "shader", "texture", "atlas"
        };
        
        if (excludePatterns.Any(p => lower.Contains(p))) return false;
        
        var aiPatterns = new[] { "duck boss", "scav", "pmc", "guard", "enemy", "bandit", "raider" };
        
        foreach (var pattern in aiPatterns)
        {
            if (lower.Contains(pattern))
            {
                return true;
            }
        }
        
        return false;
    }
    
    private string CleanDisplayName(string name)
    {
        var result = name
            .Replace("_", " ")
            .Replace("Item", "")
            .Replace("Weapon", "")
            .Trim();
            
        if (string.IsNullOrEmpty(result)) result = name;
        
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(result.ToLower());
    }
    
    private string DetectItemCategory(string name)
    {
        var lower = name.ToLower();
        
        if (lower.Contains("weapon") || lower.Contains("gun") || lower.Contains("rifle") || 
            lower.Contains("pistol") || lower.Contains("shotgun") || lower.Contains("smg"))
            return "Weapon";
        if (lower.Contains("ammo") || lower.Contains("bullet") || lower.Contains("magazine"))
            return "Ammo";
        if (lower.Contains("armor") || lower.Contains("vest") || lower.Contains("helmet"))
            return "Armor";
        if (lower.Contains("medicine") || lower.Contains("medkit") || lower.Contains("bandage") || lower.Contains("health"))
            return "Medicine";
        if (lower.Contains("food") || lower.Contains("drink") || lower.Contains("water"))
            return "Consumable";
        if (lower.Contains("key") || lower.Contains("keycard"))
            return "Key";
        if (lower.Contains("quest") || lower.Contains("mission"))
            return "Quest";
        if (lower.Contains("attachment") || lower.Contains("scope") || lower.Contains("grip"))
            return "Attachment";
        if (lower.Contains("grenade") || lower.Contains("explosive"))
            return "Throwable";
            
        return "Misc";
    }
    
    private string DetectAICategory(string name)
    {
        var lower = name.ToLower();
        
        if (lower.Contains("boss")) return "Boss";
        if (lower.Contains("pmc")) return "PMC";
        if (lower.Contains("scav")) return "Scav";
        if (lower.Contains("guard")) return "Guard";
        
        return "Default";
    }
    
    private int GenerateStableId(string name)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in name)
            {
                hash = hash * 31 + c;
            }
            return Math.Abs(hash);
        }
    }
    
    public void SaveToJson(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        
        var itemsJson = JsonConvert.SerializeObject(Items.Values.ToList(), Formatting.Indented);
        File.WriteAllText(Path.Combine(outputPath, "parsed_items.json"), itemsJson);
        
        var aiJson = JsonConvert.SerializeObject(AITypes.Values.ToList(), Formatting.Indented);
        File.WriteAllText(Path.Combine(outputPath, "parsed_ai.json"), aiJson);
        
        Console.WriteLine($"[NativeParser] Saved to: {outputPath}");
    }
}

public class ParsedItemData
{
    public int TypeId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "Misc";
    public string SourceFile { get; set; } = "";
}

public class ParsedSceneData
{
    public string SceneId { get; set; } = "";
    public int BuildIndex { get; set; }
}

public class ParsedAIData
{
    public string TypeName { get; set; } = "";
    public string Category { get; set; } = "Default";
}

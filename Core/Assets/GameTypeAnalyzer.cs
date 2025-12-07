using Mono.Cecil;
using System.Text.RegularExpressions;

namespace DuckovTogether.Core.Assets;

public class GameTypeInfo
{
    public string FullName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string BaseType { get; set; } = "";
    public List<GameFieldInfo> Fields { get; set; } = new();
    public bool IsScriptableObject { get; set; }
    public bool IsMonoBehaviour { get; set; }
}

public class GameFieldInfo
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public bool IsPublic { get; set; }
    public bool IsSerialized { get; set; }
}

public class GameTypeAnalyzer
{
    private static GameTypeAnalyzer? _instance;
    public static GameTypeAnalyzer Instance => _instance ??= new GameTypeAnalyzer();
    
    public Dictionary<string, GameTypeInfo> Types { get; } = new();
    public Dictionary<string, GameTypeInfo> ItemTypes { get; } = new();
    public Dictionary<string, GameTypeInfo> AITypes { get; } = new();
    public Dictionary<string, GameTypeInfo> LootTypes { get; } = new();
    public Dictionary<string, GameTypeInfo> SceneTypes { get; } = new();
    
    private readonly HashSet<string> _scriptableObjectBases = new()
    {
        "UnityEngine.ScriptableObject",
        "ScriptableObject"
    };
    
    private readonly HashSet<string> _monoBehaviourBases = new()
    {
        "UnityEngine.MonoBehaviour",
        "MonoBehaviour"
    };
    
    public bool Initialize(string gamePath)
    {
        var managedPath = Path.Combine(gamePath, "Duckov_Data", "Managed");
        if (!Directory.Exists(managedPath))
        {
            Console.WriteLine($"[TypeAnalyzer] Managed folder not found: {managedPath}");
            return false;
        }
        
        var targetDlls = new[]
        {
            "Assembly-CSharp.dll",
            "TeamSoda.Duckov.Core.dll",
            "TeamSoda.Duckov.Utilities.dll",
            "ItemStatsSystem.dll"
        };
        
        foreach (var dllName in targetDlls)
        {
            var dllPath = Path.Combine(managedPath, dllName);
            if (File.Exists(dllPath))
            {
                Console.WriteLine($"[TypeAnalyzer] Analyzing: {dllName}");
                AnalyzeDll(dllPath);
            }
        }
        
        CategorizeTypes();
        
        Console.WriteLine($"[TypeAnalyzer] Found {Types.Count} types total");
        Console.WriteLine($"[TypeAnalyzer] Item types: {ItemTypes.Count}");
        Console.WriteLine($"[TypeAnalyzer] AI types: {AITypes.Count}");
        Console.WriteLine($"[TypeAnalyzer] Loot types: {LootTypes.Count}");
        
        return true;
    }
    
    private void AnalyzeDll(string dllPath)
    {
        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(dllPath)!);
            
            var readerParams = new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = false
            };
            
            using var assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParams);
            
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    if (type.IsPublic || type.IsNestedPublic)
                    {
                        AnalyzeType(type);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TypeAnalyzer] Error analyzing {dllPath}: {ex.Message}");
        }
    }
    
    private void AnalyzeType(TypeDefinition type)
    {
        var typeInfo = new GameTypeInfo
        {
            FullName = type.FullName,
            Name = type.Name,
            Namespace = type.Namespace,
            BaseType = type.BaseType?.FullName ?? ""
        };
        
        typeInfo.IsScriptableObject = IsInheritedFrom(type, _scriptableObjectBases);
        typeInfo.IsMonoBehaviour = IsInheritedFrom(type, _monoBehaviourBases);
        
        foreach (var field in type.Fields)
        {
            if (IsSerializableField(field))
            {
                typeInfo.Fields.Add(new GameFieldInfo
                {
                    Name = field.Name,
                    TypeName = field.FieldType.FullName,
                    IsPublic = field.IsPublic,
                    IsSerialized = true
                });
            }
        }
        
        foreach (var prop in type.Properties)
        {
            if (prop.GetMethod != null && prop.SetMethod != null)
            {
                var backingField = type.Fields.FirstOrDefault(f => 
                    f.Name == $"<{prop.Name}>k__BackingField");
                    
                if (backingField != null && IsSerializableField(backingField))
                {
                    if (!typeInfo.Fields.Any(f => f.Name == prop.Name))
                    {
                        typeInfo.Fields.Add(new GameFieldInfo
                        {
                            Name = prop.Name,
                            TypeName = prop.PropertyType.FullName,
                            IsPublic = prop.GetMethod.IsPublic,
                            IsSerialized = true
                        });
                    }
                }
            }
        }
        
        Types[type.FullName] = typeInfo;
    }
    
    private bool IsInheritedFrom(TypeDefinition type, HashSet<string> baseTypes)
    {
        var current = type;
        int depth = 0;
        
        while (current != null && depth < 20)
        {
            if (baseTypes.Contains(current.FullName) || baseTypes.Contains(current.Name))
                return true;
                
            if (current.BaseType == null)
                break;
                
            try
            {
                current = current.BaseType.Resolve();
            }
            catch
            {
                if (baseTypes.Contains(current.BaseType.FullName) || baseTypes.Contains(current.BaseType.Name))
                    return true;
                break;
            }
            
            depth++;
        }
        
        return false;
    }
    
    private bool IsSerializableField(FieldDefinition field)
    {
        if (field.IsStatic || field.IsLiteral)
            return false;
            
        if (field.IsPublic)
            return !HasAttribute(field, "NonSerializedAttribute");
            
        return HasAttribute(field, "SerializeField");
    }
    
    private bool HasAttribute(FieldDefinition field, string attributeName)
    {
        return field.CustomAttributes.Any(a => 
            a.AttributeType.Name == attributeName || 
            a.AttributeType.Name == attributeName.Replace("Attribute", ""));
    }
    
    private void CategorizeTypes()
    {
        var itemPatterns = new[] { "Item", "Weapon", "Armor", "Equipment", "Consumable", "Ammo", "Medicine" };
        var aiPatterns = new[] { "AI", "Enemy", "NPC", "Character", "Bot", "Scav", "PMC" };
        var lootPatterns = new[] { "Loot", "Drop", "Spawn", "Container", "Chest", "Box" };
        var scenePatterns = new[] { "Scene", "Level", "Map", "Zone", "Area" };
        
        foreach (var kvp in Types)
        {
            var type = kvp.Value;
            var name = type.Name;
            
            if (!type.IsScriptableObject && !type.IsMonoBehaviour)
                continue;
            
            if (MatchesPatterns(name, itemPatterns))
                ItemTypes[kvp.Key] = type;
            else if (MatchesPatterns(name, aiPatterns))
                AITypes[kvp.Key] = type;
            else if (MatchesPatterns(name, lootPatterns))
                LootTypes[kvp.Key] = type;
            else if (MatchesPatterns(name, scenePatterns))
                SceneTypes[kvp.Key] = type;
        }
    }
    
    private bool MatchesPatterns(string name, string[] patterns)
    {
        return patterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
    
    public GameTypeInfo? GetTypeByScriptName(string scriptName)
    {
        if (Types.TryGetValue(scriptName, out var type))
            return type;
            
        return Types.Values.FirstOrDefault(t => t.Name == scriptName);
    }
    
    public void PrintItemTypeFields()
    {
        Console.WriteLine("\n=== Item Type Fields ===");
        foreach (var kvp in ItemTypes.Take(10))
        {
            Console.WriteLine($"\n{kvp.Value.Name}:");
            foreach (var field in kvp.Value.Fields.Take(10))
            {
                Console.WriteLine($"  - {field.Name}: {field.TypeName}");
            }
        }
    }
}

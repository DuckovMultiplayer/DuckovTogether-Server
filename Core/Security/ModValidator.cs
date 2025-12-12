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

namespace DuckovTogether.Core.Security;

public class WorkshopModInfo
{
    public string WorkshopId { get; set; } = "";
    public string ModName { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastUpdated { get; set; }
    public List<string> Tags { get; set; } = new();
    public string ManifestHash { get; set; } = "";
}

public class ModValidationResult
{
    public bool IsValid { get; set; } = true;
    public bool IsCompatible { get; set; } = true;
    public string Reason { get; set; } = "";
    public ModRiskLevel RiskLevel { get; set; } = ModRiskLevel.Safe;
}

public enum ModRiskLevel
{
    Safe = 0,
    Unknown = 1,
    Suspicious = 2,
    Dangerous = 3,
    Banned = 4
}

public static class ModValidator
{
    private static readonly Dictionary<string, ModRiskLevel> _knownMods = new();
    private static readonly HashSet<string> _bannedModIds = new();
    private static readonly HashSet<string> _bannedKeywords = new()
    {
        "cheat", "hack", "godmode", "infinite", "noclip", "wallhack",
        "aimbot", "esp", "speedhack", "flyhack", "teleport", "trainer",
        "exploit", "bypass", "injector", "unlockall", "maxstats"
    };
    
    private static readonly HashSet<string> _safeKeywords = new()
    {
        "texture", "sound", "music", "language", "translation", "ui",
        "hud", "quality", "optimization", "fix", "patch", "cosmetic"
    };
    
    private const string WORKSHOP_CONTENT_PATH = @"steam\steamapps\workshop\content\3167020";
    
    public static void Initialize()
    {
        LoadBannedMods();
        Log.Info("ModValidator initialized");
    }
    
    private static void LoadBannedMods()
    {
        var bannedPath = Path.Combine(AppContext.BaseDirectory, "data", "banned_mods.json");
        if (File.Exists(bannedPath))
        {
            try
            {
                var json = File.ReadAllText(bannedPath);
                var banned = JsonConvert.DeserializeObject<List<string>>(json);
                if (banned != null)
                {
                    foreach (var id in banned)
                        _bannedModIds.Add(id);
                }
                Log.Debug($"Loaded {_bannedModIds.Count} banned mod IDs");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load banned mods: {ex.Message}");
            }
        }
    }
    
    public static ModValidationResult ValidateMod(WorkshopModInfo mod)
    {
        var result = new ModValidationResult();
        
        if (_bannedModIds.Contains(mod.WorkshopId))
        {
            result.IsValid = false;
            result.IsCompatible = false;
            result.RiskLevel = ModRiskLevel.Banned;
            result.Reason = "This mod has been banned from multiplayer";
            return result;
        }
        
        var nameLower = mod.ModName.ToLower();
        var authorLower = mod.Author.ToLower();
        
        foreach (var keyword in _bannedKeywords)
        {
            if (nameLower.Contains(keyword) || authorLower.Contains(keyword))
            {
                result.IsValid = false;
                result.IsCompatible = false;
                result.RiskLevel = ModRiskLevel.Dangerous;
                result.Reason = $"Mod name or author contains banned keyword: {keyword}";
                return result;
            }
        }
        
        foreach (var tag in mod.Tags)
        {
            var tagLower = tag.ToLower();
            foreach (var keyword in _bannedKeywords)
            {
                if (tagLower.Contains(keyword))
                {
                    result.IsValid = false;
                    result.IsCompatible = false;
                    result.RiskLevel = ModRiskLevel.Suspicious;
                    result.Reason = $"Mod tag contains suspicious keyword: {keyword}";
                    return result;
                }
            }
        }
        
        bool hasSafeKeyword = false;
        foreach (var keyword in _safeKeywords)
        {
            if (nameLower.Contains(keyword))
            {
                hasSafeKeyword = true;
                break;
            }
        }
        
        if (!hasSafeKeyword && !_knownMods.ContainsKey(mod.WorkshopId))
        {
            result.RiskLevel = ModRiskLevel.Unknown;
            result.Reason = "Unknown mod - will be monitored";
        }
        
        return result;
    }
    
    public static List<WorkshopModInfo> ParseModReport(string jsonData)
    {
        try
        {
            var mods = JsonConvert.DeserializeObject<List<WorkshopModInfo>>(jsonData);
            return mods ?? new List<WorkshopModInfo>();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to parse mod report: {ex.Message}");
            return new List<WorkshopModInfo>();
        }
    }
    
    public static void ReportSuspiciousMod(int peerId, string playerName, WorkshopModInfo mod, ModValidationResult result)
    {
        Log.Warn($"Suspicious mod detected - Player: {playerName}, Mod: {mod.ModName} ({mod.WorkshopId}), Risk: {result.RiskLevel}, Reason: {result.Reason}");
        
        var modInfo = new ModInfo
        {
            ModId = mod.WorkshopId,
            ModName = mod.ModName,
            Version = mod.Version,
            WorkshopId = mod.WorkshopId,
            IsCompatible = result.IsCompatible,
            IncompatibilityReason = result.Reason
        };
        
        if (result.RiskLevel >= ModRiskLevel.Dangerous)
        {
            AntiCheatAlert.BroadcastIncompatibleMod(peerId, playerName, modInfo);
        }
    }
    
    public static void AddBannedMod(string workshopId)
    {
        _bannedModIds.Add(workshopId);
        SaveBannedMods();
    }
    
    private static void SaveBannedMods()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        
        var bannedPath = Path.Combine(dataDir, "banned_mods.json");
        var json = JsonConvert.SerializeObject(_bannedModIds.ToList(), Formatting.Indented);
        File.WriteAllText(bannedPath, json);
    }
}

// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using DuckovTogether.Net;
using DuckovNet;
using DuckovTogetherServer.Core.Logging;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Security;

public enum AlertType
{
    CheatDetected = 1,
    SyncError = 2,
    IncompatibleMod = 3,
    ConnectionError = 4,
    ServerError = 5,
    PlayerKicked = 6,
    PlayerBanned = 7
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public class AlertColor
{
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public float A { get; set; } = 0.85f;
    
    public static AlertColor Red => new() { R = 200, G = 20, B = 20, A = 0.9f };
    public static AlertColor Orange => new() { R = 230, G = 120, B = 20, A = 0.85f };
    public static AlertColor Yellow => new() { R = 220, G = 200, B = 30, A = 0.8f };
    public static AlertColor Purple => new() { R = 150, G = 50, B = 200, A = 0.85f };
    public static AlertColor Blue => new() { R = 30, G = 100, B = 200, A = 0.8f };
}

public class FullScreenAlert
{
    public string type { get; set; } = "fullScreenAlert";
    public AlertType alertType { get; set; }
    public AlertSeverity severity { get; set; }
    public string title { get; set; } = "";
    public string message { get; set; } = "";
    public string playerName { get; set; } = "";
    public int playerId { get; set; }
    public string violation { get; set; } = "";
    public AlertColor color { get; set; } = AlertColor.Red;
    public bool requireAcknowledge { get; set; } = true;
    public bool forceDisconnect { get; set; } = false;
    public int disconnectDelayMs { get; set; } = 5000;
    public long timestamp { get; set; }
}

public class ModInfo
{
    public string ModId { get; set; } = "";
    public string ModName { get; set; } = "";
    public string Version { get; set; } = "";
    public string WorkshopId { get; set; } = "";
    public bool IsCompatible { get; set; } = true;
    public string IncompatibilityReason { get; set; } = "";
}

public class PlayerModReport
{
    public string type { get; set; } = "modReport";
    public int playerId { get; set; }
    public string playerName { get; set; } = "";
    public List<ModInfo> mods { get; set; } = new();
    public long timestamp { get; set; }
}

public static class AntiCheatAlert
{
    private static HeadlessNetService? _netService;
    private static readonly NetDataWriter _writer = new();
    private static readonly HashSet<string> _incompatibleMods = new()
    {
        "cheat_mod", "god_mode", "infinite_ammo", "speed_hack", "fly_mod",
        "wallhack", "aimbot", "esp_mod", "noclip", "unlimited_health"
    };
    
    private static readonly HashSet<string> _knownCompatibleMods = new()
    {
        "quality_of_life", "ui_improvements", "sound_pack", "texture_pack",
        "language_pack", "custom_crosshair", "minimap_mod"
    };
    
    public static void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Log.Info("AntiCheatAlert system initialized");
    }
    
    public static void BroadcastCheatAlert(int cheaterPeerId, string cheaterName, string violationType, string details)
    {
        var alert = new FullScreenAlert
        {
            alertType = AlertType.CheatDetected,
            severity = AlertSeverity.Critical,
            title = "⚠ 作弊检测 / CHEAT DETECTED ⚠",
            message = $"玩家 {cheaterName} 被检测到作弊行为\nPlayer {cheaterName} has been detected cheating",
            playerName = cheaterName,
            playerId = cheaterPeerId,
            violation = $"{violationType}: {details}",
            color = AlertColor.Red,
            requireAcknowledge = true,
            forceDisconnect = false,
            disconnectDelayMs = 0,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastAlert(alert);
        Log.Warn($"CHEAT ALERT: Player {cheaterName} (ID: {cheaterPeerId}) - {violationType}: {details}");
    }
    
    public static void BroadcastSyncError(int playerId, string playerName, string errorType, string details)
    {
        var alert = new FullScreenAlert
        {
            alertType = AlertType.SyncError,
            severity = AlertSeverity.Error,
            title = "⚡ 同步错误 / SYNC ERROR ⚡",
            message = $"检测到同步异常，游戏将进行结算\nSync anomaly detected, game will settle",
            playerName = playerName,
            playerId = playerId,
            violation = $"{errorType}: {details}",
            color = AlertColor.Orange,
            requireAcknowledge = true,
            forceDisconnect = true,
            disconnectDelayMs = 5000,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastAlert(alert);
        Log.Warn($"SYNC ERROR: Player {playerName} (ID: {playerId}) - {errorType}: {details}");
    }
    
    public static void BroadcastIncompatibleMod(int playerId, string playerName, ModInfo mod)
    {
        var alert = new FullScreenAlert
        {
            alertType = AlertType.IncompatibleMod,
            severity = AlertSeverity.Error,
            title = "🔧 不兼容Mod / INCOMPATIBLE MOD 🔧",
            message = $"玩家 {playerName} 安装了不兼容的Mod\nPlayer {playerName} has incompatible mod installed\n\nMod: {mod.ModName}",
            playerName = playerName,
            playerId = playerId,
            violation = $"Mod: {mod.ModName} ({mod.WorkshopId}) - {mod.IncompatibilityReason}",
            color = AlertColor.Purple,
            requireAcknowledge = true,
            forceDisconnect = true,
            disconnectDelayMs = 10000,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastAlert(alert);
        Log.Warn($"INCOMPATIBLE MOD: Player {playerName} (ID: {playerId}) - {mod.ModName} ({mod.WorkshopId})");
    }
    
    public static void SendPlayerKickedAlert(int kickedPeerId, string kickedName, string reason)
    {
        var alert = new FullScreenAlert
        {
            alertType = AlertType.PlayerKicked,
            severity = AlertSeverity.Warning,
            title = "👢 玩家被踢出 / PLAYER KICKED 👢",
            message = $"玩家 {kickedName} 已被踢出游戏\nPlayer {kickedName} has been kicked",
            playerName = kickedName,
            playerId = kickedPeerId,
            violation = reason,
            color = AlertColor.Yellow,
            requireAcknowledge = true,
            forceDisconnect = false,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastAlert(alert);
        Log.Info($"PLAYER KICKED: {kickedName} (ID: {kickedPeerId}) - {reason}");
    }
    
    public static void SendPlayerBannedAlert(int bannedPeerId, string bannedName, string reason)
    {
        var alert = new FullScreenAlert
        {
            alertType = AlertType.PlayerBanned,
            severity = AlertSeverity.Critical,
            title = "🚫 玩家被封禁 / PLAYER BANNED 🚫",
            message = $"玩家 {bannedName} 因作弊被永久封禁\nPlayer {bannedName} has been permanently banned for cheating",
            playerName = bannedName,
            playerId = bannedPeerId,
            violation = reason,
            color = AlertColor.Red,
            requireAcknowledge = true,
            forceDisconnect = true,
            disconnectDelayMs = 3000,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastAlert(alert);
        Log.Warn($"PLAYER BANNED: {bannedName} (ID: {bannedPeerId}) - {reason}");
    }
    
    public static void ProcessModReport(int peerId, string playerName, List<ModInfo> mods)
    {
        foreach (var mod in mods)
        {
            var modNameLower = mod.ModName.ToLower();
            var modIdLower = mod.ModId.ToLower();
            
            foreach (var cheatMod in _incompatibleMods)
            {
                if (modNameLower.Contains(cheatMod) || modIdLower.Contains(cheatMod))
                {
                    mod.IsCompatible = false;
                    mod.IncompatibilityReason = "Suspected cheat mod";
                    BroadcastIncompatibleMod(peerId, playerName, mod);
                    return;
                }
            }
        }
        
        Log.Debug($"Mod report from {playerName}: {mods.Count} mods verified");
    }
    
    public static bool IsModCompatible(string modName, string modId)
    {
        var nameLower = modName.ToLower();
        var idLower = modId.ToLower();
        
        foreach (var cheatMod in _incompatibleMods)
        {
            if (nameLower.Contains(cheatMod) || idLower.Contains(cheatMod))
                return false;
        }
        
        return true;
    }
    
    public static void OnPlayerAcknowledgeAlert(int peerId, AlertType alertType)
    {
        Log.Debug($"Player {peerId} acknowledged alert: {alertType}");
        
        switch (alertType)
        {
            case AlertType.CheatDetected:
                KickPlayer(peerId, "Cheat detection acknowledged");
                break;
            case AlertType.SyncError:
            case AlertType.IncompatibleMod:
                break;
        }
    }
    
    private static void BroadcastAlert(FullScreenAlert alert)
    {
        if (_netService?.Server == null) return;
        
        var json = JsonConvert.SerializeObject(alert);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        
        _netService.SendToAll(_writer, DeliveryMethod.ReliableOrdered);
    }
    
    private static void SendAlertToPlayer(int peerId, FullScreenAlert alert)
    {
        if (_netService?.Server == null) return;
        
        var json = JsonConvert.SerializeObject(alert);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        
        _netService.Server.Send(peerId, _writer.CopyData(), DeliveryMode.Reliable);
    }
    
    private static void KickPlayer(int peerId, string reason)
    {
        if (_netService?.Server == null) return;
        
        Log.Info($"Kicking player {peerId}: {reason}");
        _netService.Server.Disconnect(peerId, reason);
    }
}

// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System.Runtime.InteropServices;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Core.Security;

public enum ViolationType : uint
{
    None = 0,
    SpeedHack = 1,
    DamageHack = 2,
    PositionHack = 3,
    HealthHack = 4,
    SequenceHack = 5,
    SignatureInvalid = 6,
    TimestampInvalid = 7,
    RateLimit = 8
}

[StructLayout(LayoutKind.Sequential)]
public struct PacketHeader
{
    public uint PlayerId;
    public uint Sequence;
    public ulong Timestamp;
    public uint Checksum;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] Signature;
}

[StructLayout(LayoutKind.Sequential)]
public struct GameAction
{
    public int EntityId;
    public float PosX, PosY, PosZ;
    public float Health;
    public float Damage;
    public uint ActionType;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct ViolationReport
{
    public uint PlayerId;
    public uint ViolationType;
    public uint Severity;
    public ulong Timestamp;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Details;
}

public static class DuckovGuard
{
    private const string DLL_NAME = "duckov_guard";
    private static bool _initialized;
    private static bool _available;
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_init([MarshalAs(UnmanagedType.LPStr)] string serverKey, uint keyLen);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dg_shutdown();
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_register_player(uint playerId, byte[] sessionKey, uint keyLen);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dg_unregister_player(uint playerId);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_packet(uint playerId, byte[] data, uint len, out PacketHeader header);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_sign_packet(uint playerId, byte[] data, uint len, ref PacketHeader header);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_position(uint playerId, float x, float y, float z, float deltaTime);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_damage(uint playerId, int targetId, float damage, float distance);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_health(uint playerId, float oldHealth, float newHealth, float maxHealth);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_action(uint playerId, ref GameAction action, out ViolationReport report);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dg_update_player_position(uint playerId, float x, float y, float z);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dg_update_player_health(uint playerId, float health);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint dg_get_violation_count(uint playerId);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_get_last_violation(uint playerId, out ViolationReport report);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dg_clear_violations(uint playerId);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint dg_compute_checksum(byte[] data, uint len);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_weapon_fire(uint playerId, int weaponId, float fireRate);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_item_pickup(uint playerId, int itemId, float distance);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dg_set_player_state(uint playerId, int stateFlags);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_should_ban_player(uint playerId);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dg_validate_teleport(uint playerId, float fromX, float fromY, float fromZ,
                                                    float toX, float toY, float toZ, int isAllowed);
    
    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void dg_set_max_health(uint playerId, float maxHealth);
    
    private static readonly Dictionary<uint, string> _playerNames = new();
    
    public static bool Initialize(string serverKey)
    {
        if (_initialized) return _available;
        
        try
        {
            var result = dg_init(serverKey, (uint)serverKey.Length);
            _available = result != 0;
            _initialized = true;
            Log.Info($"DuckovGuard initialized: {_available}");
        }
        catch (DllNotFoundException)
        {
            Log.Warn("DuckovGuard native library not found, using fallback validation");
            _available = false;
            _initialized = true;
        }
        catch (Exception ex)
        {
            Log.Error($"DuckovGuard init error: {ex.Message}");
            _available = false;
            _initialized = true;
        }
        
        return _available;
    }
    
    public static void Shutdown()
    {
        if (_available)
        {
            try { dg_shutdown(); } catch (Exception ex) { Log.Error($"DuckovGuard shutdown error: {ex.Message}"); }
        }
        _initialized = false;
        _available = false;
    }
    
    public static bool RegisterPlayer(uint playerId, byte[]? sessionKey = null)
    {
        if (!_available) return true;
        
        try
        {
            return dg_register_player(playerId, sessionKey ?? Array.Empty<byte>(), (uint)(sessionKey?.Length ?? 0)) != 0;
        }
        catch { return true; }
    }
    
    public static void UnregisterPlayer(uint playerId)
    {
        if (!_available) return;
        try { dg_unregister_player(playerId); } catch (Exception ex) { Log.Error($"DuckovGuard error: {ex.Message}"); }
    }
    
    public static bool ValidatePosition(uint playerId, float x, float y, float z, float deltaTime = 0.016f)
    {
        if (!_available) return true;
        
        try
        {
            return dg_validate_position(playerId, x, y, z, deltaTime) != 0;
        }
        catch { return true; }
    }
    
    public static bool ValidateDamage(uint playerId, int targetId, float damage, float distance)
    {
        if (!_available) return true;
        
        try
        {
            return dg_validate_damage(playerId, targetId, damage, distance) != 0;
        }
        catch { return true; }
    }
    
    public static bool ValidateHealth(uint playerId, float oldHealth, float newHealth, float maxHealth)
    {
        if (!_available) return true;
        
        try
        {
            return dg_validate_health(playerId, oldHealth, newHealth, maxHealth) != 0;
        }
        catch { return true; }
    }
    
    public static bool ValidateAction(uint playerId, GameAction action, out ViolationReport? report)
    {
        report = null;
        if (!_available) return true;
        
        try
        {
            var result = dg_validate_action(playerId, ref action, out var rep);
            if (result == 0)
            {
                report = rep;
                return false;
            }
            return true;
        }
        catch { return true; }
    }
    
    public static void UpdatePlayerPosition(uint playerId, float x, float y, float z)
    {
        if (!_available) return;
        try { dg_update_player_position(playerId, x, y, z); } catch (Exception ex) { Log.Error($"DuckovGuard error: {ex.Message}"); }
    }
    
    public static void UpdatePlayerHealth(uint playerId, float health)
    {
        if (!_available) return;
        try { dg_update_player_health(playerId, health); } catch (Exception ex) { Log.Error($"DuckovGuard error: {ex.Message}"); }
    }
    
    public static uint GetViolationCount(uint playerId)
    {
        if (!_available) return 0;
        try { return dg_get_violation_count(playerId); } catch { return 0; }
    }
    
    public static ViolationReport? GetLastViolation(uint playerId)
    {
        if (!_available) return null;
        
        try
        {
            if (dg_get_last_violation(playerId, out var report) != 0)
                return report;
        }
        catch (Exception ex) { Log.Error($"DuckovGuard error: {ex.Message}"); }
        
        return null;
    }
    
    public static void ClearViolations(uint playerId)
    {
        if (!_available) return;
        try { dg_clear_violations(playerId); } catch (Exception ex) { Log.Error($"DuckovGuard error: {ex.Message}"); }
    }
    
    public static uint ComputeChecksum(byte[] data)
    {
        if (!_available)
        {
            uint hash = 2166136261;
            foreach (var b in data)
            {
                hash ^= b;
                hash *= 16777619;
            }
            return hash;
        }
        
        try { return dg_compute_checksum(data, (uint)data.Length); }
        catch { return 0; }
    }
    
    public static void RegisterPlayerWithName(uint playerId, string playerName, byte[]? sessionKey = null)
    {
        _playerNames[playerId] = playerName;
        RegisterPlayer(playerId, sessionKey);
    }
    
    public static bool ValidateWeaponFire(uint playerId, int weaponId, float fireRate)
    {
        if (!_available) return true;
        
        try
        {
            var result = dg_validate_weapon_fire(playerId, weaponId, fireRate);
            if (result == 0)
            {
                var violation = GetLastViolation(playerId);
                if (violation.HasValue)
                {
                    TriggerCheatAlert(playerId, "WeaponFireRateHack", violation.Value.Details);
                }
                return false;
            }
            return true;
        }
        catch { return true; }
    }
    
    public static bool ValidateItemPickup(uint playerId, int itemId, float distance)
    {
        if (!_available) return true;
        
        try
        {
            var result = dg_validate_item_pickup(playerId, itemId, distance);
            if (result == 0)
            {
                var violation = GetLastViolation(playerId);
                if (violation.HasValue)
                {
                    TriggerCheatAlert(playerId, "ItemPickupHack", violation.Value.Details);
                }
                return false;
            }
            return true;
        }
        catch { return true; }
    }
    
    public static void SetPlayerState(uint playerId, bool isSprinting, bool isInVehicle, bool isDead)
    {
        if (!_available) return;
        
        int flags = 0;
        if (isSprinting) flags |= 1;
        if (isInVehicle) flags |= 2;
        if (isDead) flags |= 4;
        
        try { dg_set_player_state(playerId, flags); }
        catch (Exception ex) { Log.Error($"DuckovGuard error: {ex.Message}"); }
    }
    
    public static bool ShouldBanPlayer(uint playerId)
    {
        if (!_available) return false;
        
        try { return dg_should_ban_player(playerId) != 0; }
        catch { return false; }
    }
    
    public static bool ValidateTeleport(uint playerId, float fromX, float fromY, float fromZ,
                                         float toX, float toY, float toZ, bool isAllowed = false)
    {
        if (!_available) return true;
        
        try
        {
            var result = dg_validate_teleport(playerId, fromX, fromY, fromZ, toX, toY, toZ, isAllowed ? 1 : 0);
            if (result == 0)
            {
                var violation = GetLastViolation(playerId);
                if (violation.HasValue)
                {
                    TriggerCheatAlert(playerId, "TeleportHack", violation.Value.Details);
                }
                return false;
            }
            return true;
        }
        catch { return true; }
    }
    
    public static void SetMaxHealth(uint playerId, float maxHealth)
    {
        if (!_available) return;
        try { dg_set_max_health(playerId, maxHealth); }
        catch (Exception ex) { Log.Error($"DuckovGuard error: {ex.Message}"); }
    }
    
    public static bool ValidatePositionWithAlert(uint playerId, float x, float y, float z, float deltaTime)
    {
        var result = ValidatePosition(playerId, x, y, z, deltaTime);
        if (!result)
        {
            var violation = GetLastViolation(playerId);
            if (violation.HasValue)
            {
                TriggerCheatAlert(playerId, "SpeedHack", violation.Value.Details);
            }
        }
        return result;
    }
    
    public static bool ValidateDamageWithAlert(uint playerId, int targetId, float damage, float distance)
    {
        var result = ValidateDamage(playerId, targetId, damage, distance);
        if (!result)
        {
            var violation = GetLastViolation(playerId);
            if (violation.HasValue)
            {
                TriggerCheatAlert(playerId, "DamageHack", violation.Value.Details);
            }
        }
        return result;
    }
    
    public static bool ValidateHealthWithAlert(uint playerId, float oldHealth, float newHealth, float maxHealth)
    {
        var result = ValidateHealth(playerId, oldHealth, newHealth, maxHealth);
        if (!result)
        {
            var violation = GetLastViolation(playerId);
            if (violation.HasValue)
            {
                TriggerCheatAlert(playerId, "HealthHack", violation.Value.Details);
            }
        }
        return result;
    }
    
    private static void TriggerCheatAlert(uint playerId, string violationType, string details)
    {
        var playerName = _playerNames.TryGetValue(playerId, out var name) ? name : $"Player_{playerId}";
        AntiCheatAlert.BroadcastCheatAlert((int)playerId, playerName, violationType, details);
        
        if (ShouldBanPlayer(playerId))
        {
            AntiCheatAlert.SendPlayerBannedAlert((int)playerId, playerName, $"Multiple violations: {violationType}");
        }
    }
    
    public static string GetViolationTypeName(uint type)
    {
        return (ViolationType)type switch
        {
            ViolationType.SpeedHack => "速度作弊 / Speed Hack",
            ViolationType.DamageHack => "伤害作弊 / Damage Hack",
            ViolationType.PositionHack => "位置作弊 / Position Hack",
            ViolationType.HealthHack => "生命值作弊 / Health Hack",
            ViolationType.SequenceHack => "序列号篡改 / Sequence Hack",
            ViolationType.SignatureInvalid => "签名无效 / Invalid Signature",
            ViolationType.TimestampInvalid => "时间戳异常 / Invalid Timestamp",
            ViolationType.RateLimit => "频率限制 / Rate Limit",
            _ => "未知违规 / Unknown Violation"
        };
    }
}

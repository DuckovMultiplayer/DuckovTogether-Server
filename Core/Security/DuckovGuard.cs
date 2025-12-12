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
    
    public static bool Initialize(string serverKey)
    {
        if (_initialized) return _available;
        
        try
        {
            var result = dg_init(serverKey, (uint)serverKey.Length);
            _available = result != 0;
            _initialized = true;
            Console.WriteLine($"[DuckovGuard] Initialized: {_available}");
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[DuckovGuard] Native library not found, using fallback validation");
            _available = false;
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DuckovGuard] Init error: {ex.Message}");
            _available = false;
            _initialized = true;
        }
        
        return _available;
    }
    
    public static void Shutdown()
    {
        if (_available)
        {
            try { dg_shutdown(); } catch (Exception ex) { Console.WriteLine($"[DuckovGuard] Error: {ex.Message}"); }
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
        try { dg_unregister_player(playerId); } catch (Exception ex) { Console.WriteLine($"[DuckovGuard] Error: {ex.Message}"); }
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
        try { dg_update_player_position(playerId, x, y, z); } catch (Exception ex) { Console.WriteLine($"[DuckovGuard] Error: {ex.Message}"); }
    }
    
    public static void UpdatePlayerHealth(uint playerId, float health)
    {
        if (!_available) return;
        try { dg_update_player_health(playerId, health); } catch (Exception ex) { Console.WriteLine($"[DuckovGuard] Error: {ex.Message}"); }
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
        catch (Exception ex) { Console.WriteLine($"[DuckovGuard] Error: {ex.Message}"); }
        
        return null;
    }
    
    public static void ClearViolations(uint playerId)
    {
        if (!_available) return;
        try { dg_clear_violations(playerId); } catch (Exception ex) { Console.WriteLine($"[DuckovGuard] Error: {ex.Message}"); }
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
}

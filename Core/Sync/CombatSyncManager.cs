using System.Numerics;
using DuckovTogether.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class CombatSyncManager
{
    private static CombatSyncManager? _instance;
    public static CombatSyncManager Instance => _instance ??= new CombatSyncManager();
    
    private HeadlessNetService? _netService;
    private readonly NetDataWriter _writer = new();
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Console.WriteLine("[CombatSync] Initialized");
    }
    
    public void OnWeaponFire(int shooterId, int weaponId, Vector3 origin, Vector3 direction, int ammoType)
    {
        var data = new WeaponFireSync
        {
            type = "weaponFire",
            shooterId = shooterId,
            weaponId = weaponId,
            origin = new Vec3 { x = origin.X, y = origin.Y, z = origin.Z },
            direction = new Vec3 { x = direction.X, y = direction.Y, z = direction.Z },
            ammoType = ammoType,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJsonExcept(shooterId, data);
    }
    
    public void OnPlayerDamage(int targetId, int attackerId, float damage, string damageType, Vector3 hitPoint)
    {
        var data = new DamageSync
        {
            type = "playerDamage",
            targetId = targetId,
            attackerId = attackerId,
            damage = damage,
            damageType = damageType,
            hitPoint = new Vec3 { x = hitPoint.X, y = hitPoint.Y, z = hitPoint.Z },
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
        
        PlayerSyncManager.Instance.UpdatePlayerHealth(targetId, 
            Math.Max(0, GetPlayerHealth(targetId) - damage), 100f);
    }
    
    public void OnAIDamage(int aiId, int attackerId, float damage, string damageType, Vector3 hitPoint)
    {
        var ai = GameLogic.AIManager.Instance.GetEntity(aiId);
        if (ai == null) return;
        
        ai.TakeDamage(damage, attackerId);
        
        var data = new DamageSync
        {
            type = "aiDamage",
            targetId = aiId,
            attackerId = attackerId,
            damage = damage,
            damageType = damageType,
            hitPoint = new Vec3 { x = hitPoint.X, y = hitPoint.Y, z = hitPoint.Z },
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnGrenadeThrow(int throwerId, int grenadeType, Vector3 origin, Vector3 velocity)
    {
        var data = new GrenadeThrowSync
        {
            type = "grenadeThrow",
            throwerId = throwerId,
            grenadeType = grenadeType,
            origin = new Vec3 { x = origin.X, y = origin.Y, z = origin.Z },
            velocity = new Vec3 { x = velocity.X, y = velocity.Y, z = velocity.Z },
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJsonExcept(throwerId, data);
    }
    
    public void OnGrenadeExplode(int grenadeId, Vector3 position, float radius, float damage)
    {
        var data = new GrenadeExplodeSync
        {
            type = "grenadeExplode",
            grenadeId = grenadeId,
            position = new Vec3 { x = position.X, y = position.Y, z = position.Z },
            radius = radius,
            damage = damage,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnMeleeAttack(int attackerId, int weaponId, Vector3 direction)
    {
        var data = new MeleeAttackSync
        {
            type = "meleeAttack",
            attackerId = attackerId,
            weaponId = weaponId,
            direction = new Vec3 { x = direction.X, y = direction.Y, z = direction.Z },
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJsonExcept(attackerId, data);
    }
    
    public void OnReload(int playerId, int weaponId, int ammoCount)
    {
        var data = new ReloadSync
        {
            type = "reload",
            playerId = playerId,
            weaponId = weaponId,
            ammoCount = ammoCount,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJsonExcept(playerId, data);
    }
    
    public void OnWeaponSwitch(int playerId, int newWeaponId, int slot)
    {
        var data = new WeaponSwitchSync
        {
            type = "weaponSwitch",
            playerId = playerId,
            weaponId = newWeaponId,
            slot = slot,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJsonExcept(playerId, data);
    }
    
    private float GetPlayerHealth(int peerId)
    {
        return 100f;
    }
    
    private void BroadcastJson(object data)
    {
        if (_netService?.NetManager == null) return;
        
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }
    }
    
    private void BroadcastJsonExcept(int excludePeerId, object data)
    {
        if (_netService?.NetManager == null) return;
        
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        
        foreach (var peer in _netService.NetManager.ConnectedPeerList)
        {
            if (peer.Id != excludePeerId)
            {
                peer.Send(_writer, DeliveryMethod.ReliableOrdered);
            }
        }
    }
}

public class WeaponFireSync
{
    public string type { get; set; } = "weaponFire";
    public int shooterId { get; set; }
    public int weaponId { get; set; }
    public Vec3 origin { get; set; } = new();
    public Vec3 direction { get; set; } = new();
    public int ammoType { get; set; }
    public long timestamp { get; set; }
}

public class DamageSync
{
    public string type { get; set; } = "";
    public int targetId { get; set; }
    public int attackerId { get; set; }
    public float damage { get; set; }
    public string damageType { get; set; } = "";
    public Vec3 hitPoint { get; set; } = new();
    public long timestamp { get; set; }
}

public class GrenadeThrowSync
{
    public string type { get; set; } = "grenadeThrow";
    public int throwerId { get; set; }
    public int grenadeType { get; set; }
    public Vec3 origin { get; set; } = new();
    public Vec3 velocity { get; set; } = new();
    public long timestamp { get; set; }
}

public class GrenadeExplodeSync
{
    public string type { get; set; } = "grenadeExplode";
    public int grenadeId { get; set; }
    public Vec3 position { get; set; } = new();
    public float radius { get; set; }
    public float damage { get; set; }
    public long timestamp { get; set; }
}

public class MeleeAttackSync
{
    public string type { get; set; } = "meleeAttack";
    public int attackerId { get; set; }
    public int weaponId { get; set; }
    public Vec3 direction { get; set; } = new();
    public long timestamp { get; set; }
}

public class ReloadSync
{
    public string type { get; set; } = "reload";
    public int playerId { get; set; }
    public int weaponId { get; set; }
    public int ammoCount { get; set; }
    public long timestamp { get; set; }
}

public class WeaponSwitchSync
{
    public string type { get; set; } = "weaponSwitch";
    public int playerId { get; set; }
    public int weaponId { get; set; }
    public int slot { get; set; }
    public long timestamp { get; set; }
}

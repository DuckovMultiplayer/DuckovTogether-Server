// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using System.Numerics;
using DuckovTogether.Net;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class DeltaSyncManager
{
    private static DeltaSyncManager? _instance;
    public static DeltaSyncManager Instance => _instance ??= new DeltaSyncManager();
    
    private readonly Dictionary<int, PlayerDeltaState> _playerDeltas = new();
    private readonly Dictionary<int, AIDeltaState> _aiDeltas = new();
    
    private const float POSITION_THRESHOLD = 0.01f;
    private const float ROTATION_THRESHOLD = 1f;
    private const float VELOCITY_THRESHOLD = 0.1f;
    private const float HEALTH_THRESHOLD = 0.1f;
    
    public void UpdatePlayerState(int peerId, Vector3 position, Vector3 rotation, Vector3 velocity, 
        float health, int weaponId, float speed, float dirX, float dirY, int handState, 
        bool gunReady, bool dashing, bool reloading)
    {
        if (!_playerDeltas.TryGetValue(peerId, out var delta))
        {
            delta = new PlayerDeltaState { PeerId = peerId };
            _playerDeltas[peerId] = delta;
        }
        
        delta.CurrentPosition = position;
        delta.CurrentRotation = rotation;
        delta.CurrentVelocity = velocity;
        delta.CurrentHealth = health;
        delta.CurrentWeaponId = weaponId;
        delta.CurrentSpeed = speed;
        delta.CurrentDirX = dirX;
        delta.CurrentDirY = dirY;
        delta.CurrentHandState = handState;
        delta.CurrentGunReady = gunReady;
        delta.CurrentDashing = dashing;
        delta.CurrentReloading = reloading;
    }
    
    public List<PlayerDeltaPacket> GetDirtyPlayers()
    {
        var result = new List<PlayerDeltaPacket>();
        
        foreach (var kvp in _playerDeltas)
        {
            var delta = kvp.Value;
            var packet = new PlayerDeltaPacket { peerId = delta.PeerId };
            bool isDirty = false;
            
            if (Vector3.Distance(delta.CurrentPosition, delta.LastSentPosition) > POSITION_THRESHOLD)
            {
                packet.position = new Vec3 { x = delta.CurrentPosition.X, y = delta.CurrentPosition.Y, z = delta.CurrentPosition.Z };
                packet.hasPosition = true;
                isDirty = true;
            }
            
            if (Vector3.Distance(delta.CurrentRotation, delta.LastSentRotation) > ROTATION_THRESHOLD)
            {
                packet.rotation = new Vec3 { x = delta.CurrentRotation.X, y = delta.CurrentRotation.Y, z = delta.CurrentRotation.Z };
                packet.hasRotation = true;
                isDirty = true;
            }
            
            if (Vector3.Distance(delta.CurrentVelocity, delta.LastSentVelocity) > VELOCITY_THRESHOLD)
            {
                packet.velocity = new Vec3 { x = delta.CurrentVelocity.X, y = delta.CurrentVelocity.Y, z = delta.CurrentVelocity.Z };
                packet.hasVelocity = true;
                isDirty = true;
            }
            
            if (Math.Abs(delta.CurrentHealth - delta.LastSentHealth) > HEALTH_THRESHOLD)
            {
                packet.health = delta.CurrentHealth;
                packet.hasHealth = true;
                isDirty = true;
            }
            
            if (delta.CurrentWeaponId != delta.LastSentWeaponId)
            {
                packet.weaponId = delta.CurrentWeaponId;
                packet.hasWeapon = true;
                isDirty = true;
            }
            
            bool animChanged = Math.Abs(delta.CurrentSpeed - delta.LastSentSpeed) > 0.1f ||
                              Math.Abs(delta.CurrentDirX - delta.LastSentDirX) > 0.1f ||
                              Math.Abs(delta.CurrentDirY - delta.LastSentDirY) > 0.1f ||
                              delta.CurrentHandState != delta.LastSentHandState ||
                              delta.CurrentGunReady != delta.LastSentGunReady ||
                              delta.CurrentDashing != delta.LastSentDashing ||
                              delta.CurrentReloading != delta.LastSentReloading;
            
            if (animChanged)
            {
                packet.speed = delta.CurrentSpeed;
                packet.dirX = delta.CurrentDirX;
                packet.dirY = delta.CurrentDirY;
                packet.handState = delta.CurrentHandState;
                packet.gunReady = delta.CurrentGunReady;
                packet.dashing = delta.CurrentDashing;
                packet.reloading = delta.CurrentReloading;
                packet.hasAnim = true;
                isDirty = true;
            }
            
            if (isDirty)
            {
                delta.LastSentPosition = delta.CurrentPosition;
                delta.LastSentRotation = delta.CurrentRotation;
                delta.LastSentVelocity = delta.CurrentVelocity;
                delta.LastSentHealth = delta.CurrentHealth;
                delta.LastSentWeaponId = delta.CurrentWeaponId;
                delta.LastSentSpeed = delta.CurrentSpeed;
                delta.LastSentDirX = delta.CurrentDirX;
                delta.LastSentDirY = delta.CurrentDirY;
                delta.LastSentHandState = delta.CurrentHandState;
                delta.LastSentGunReady = delta.CurrentGunReady;
                delta.LastSentDashing = delta.CurrentDashing;
                delta.LastSentReloading = delta.CurrentReloading;
                
                result.Add(packet);
            }
        }
        
        return result;
    }
    
    public void UpdateAIState(int entityId, Vector3 position, Vector3 rotation, float health, int state)
    {
        if (!_aiDeltas.TryGetValue(entityId, out var delta))
        {
            delta = new AIDeltaState { EntityId = entityId };
            _aiDeltas[entityId] = delta;
        }
        
        delta.CurrentPosition = position;
        delta.CurrentRotation = rotation;
        delta.CurrentHealth = health;
        delta.CurrentState = state;
    }
    
    public List<AIDeltaPacket> GetDirtyAI()
    {
        var result = new List<AIDeltaPacket>();
        
        foreach (var kvp in _aiDeltas)
        {
            var delta = kvp.Value;
            var packet = new AIDeltaPacket { entityId = delta.EntityId };
            bool isDirty = false;
            
            if (Vector3.Distance(delta.CurrentPosition, delta.LastSentPosition) > POSITION_THRESHOLD)
            {
                packet.position = new Vec3 { x = delta.CurrentPosition.X, y = delta.CurrentPosition.Y, z = delta.CurrentPosition.Z };
                packet.hasPosition = true;
                isDirty = true;
            }
            
            if (Vector3.Distance(delta.CurrentRotation, delta.LastSentRotation) > ROTATION_THRESHOLD)
            {
                packet.rotation = new Vec3 { x = delta.CurrentRotation.X, y = delta.CurrentRotation.Y, z = delta.CurrentRotation.Z };
                packet.hasRotation = true;
                isDirty = true;
            }
            
            if (Math.Abs(delta.CurrentHealth - delta.LastSentHealth) > HEALTH_THRESHOLD)
            {
                packet.health = delta.CurrentHealth;
                packet.hasHealth = true;
                isDirty = true;
            }
            
            if (delta.CurrentState != delta.LastSentState)
            {
                packet.state = delta.CurrentState;
                packet.hasState = true;
                isDirty = true;
            }
            
            if (isDirty)
            {
                delta.LastSentPosition = delta.CurrentPosition;
                delta.LastSentRotation = delta.CurrentRotation;
                delta.LastSentHealth = delta.CurrentHealth;
                delta.LastSentState = delta.CurrentState;
                
                result.Add(packet);
            }
        }
        
        return result;
    }
    
    public void RemovePlayer(int peerId) => _playerDeltas.Remove(peerId);
    public void RemoveAI(int entityId) => _aiDeltas.Remove(entityId);
    public void Clear()
    {
        _playerDeltas.Clear();
        _aiDeltas.Clear();
    }
}

public class PlayerDeltaState
{
    public int PeerId { get; set; }
    public Vector3 CurrentPosition { get; set; }
    public Vector3 CurrentRotation { get; set; }
    public Vector3 CurrentVelocity { get; set; }
    public float CurrentHealth { get; set; }
    public int CurrentWeaponId { get; set; }
    public float CurrentSpeed { get; set; }
    public float CurrentDirX { get; set; }
    public float CurrentDirY { get; set; }
    public int CurrentHandState { get; set; }
    public bool CurrentGunReady { get; set; }
    public bool CurrentDashing { get; set; }
    public bool CurrentReloading { get; set; }
    
    public Vector3 LastSentPosition { get; set; }
    public Vector3 LastSentRotation { get; set; }
    public Vector3 LastSentVelocity { get; set; }
    public float LastSentHealth { get; set; }
    public int LastSentWeaponId { get; set; }
    public float LastSentSpeed { get; set; }
    public float LastSentDirX { get; set; }
    public float LastSentDirY { get; set; }
    public int LastSentHandState { get; set; }
    public bool LastSentGunReady { get; set; }
    public bool LastSentDashing { get; set; }
    public bool LastSentReloading { get; set; }
}

public class AIDeltaState
{
    public int EntityId { get; set; }
    public Vector3 CurrentPosition { get; set; }
    public Vector3 CurrentRotation { get; set; }
    public float CurrentHealth { get; set; }
    public int CurrentState { get; set; }
    
    public Vector3 LastSentPosition { get; set; }
    public Vector3 LastSentRotation { get; set; }
    public float LastSentHealth { get; set; }
    public int LastSentState { get; set; }
}

public class PlayerDeltaPacket
{
    public string type { get; set; } = "player_delta";
    public int peerId { get; set; }
    public bool hasPosition { get; set; }
    public Vec3? position { get; set; }
    public bool hasRotation { get; set; }
    public Vec3? rotation { get; set; }
    public bool hasVelocity { get; set; }
    public Vec3? velocity { get; set; }
    public bool hasHealth { get; set; }
    public float health { get; set; }
    public bool hasWeapon { get; set; }
    public int weaponId { get; set; }
    public bool hasAnim { get; set; }
    public float speed { get; set; }
    public float dirX { get; set; }
    public float dirY { get; set; }
    public int handState { get; set; }
    public bool gunReady { get; set; }
    public bool dashing { get; set; }
    public bool reloading { get; set; }
}

public class AIDeltaPacket
{
    public string type { get; set; } = "ai_delta";
    public int entityId { get; set; }
    public bool hasPosition { get; set; }
    public Vec3? position { get; set; }
    public bool hasRotation { get; set; }
    public Vec3? rotation { get; set; }
    public bool hasHealth { get; set; }
    public float health { get; set; }
    public bool hasState { get; set; }
    public int state { get; set; }
}

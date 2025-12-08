// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using DuckovTogether.Core.GameLogic;
using System.Numerics;

namespace DuckovTogether.Core.Security;

public class ValidationService
{
    private static ValidationService? _instance;
    public static ValidationService Instance => _instance ??= new ValidationService();
    
    private readonly Dictionary<int, PlayerValidationState> _playerStates = new();
    private readonly object _lock = new();
    
    public event Action<int, ViolationType, string>? OnViolationDetected;
    
    public void Initialize(string serverKey)
    {
        DuckovGuard.Initialize(serverKey);
        Console.WriteLine("[ValidationService] Initialized");
    }
    
    public void Shutdown()
    {
        DuckovGuard.Shutdown();
    }
    
    public void OnPlayerJoin(int peerId)
    {
        lock (_lock)
        {
            _playerStates[peerId] = new PlayerValidationState
            {
                PeerId = peerId,
                JoinTime = DateTime.Now
            };
        }
        
        DuckovGuard.RegisterPlayer((uint)peerId);
        Console.WriteLine($"[Validation] Player {peerId} registered");
    }
    
    public void OnPlayerLeave(int peerId)
    {
        lock (_lock)
        {
            _playerStates.Remove(peerId);
        }
        
        DuckovGuard.UnregisterPlayer((uint)peerId);
    }
    
    public bool ValidatePositionUpdate(int peerId, Vector3 newPos, float deltaTime = 0.016f)
    {
        PlayerValidationState? state;
        lock (_lock)
        {
            if (!_playerStates.TryGetValue(peerId, out state))
                return true;
        }
        
        var distance = Vector3.Distance(state.LastPosition, newPos);
        var maxDistance = state.MaxSpeed * deltaTime * 1.5f;
        
        if (distance > maxDistance && distance > 1.0f && state.PositionUpdateCount > 10)
        {
            var speed = distance / deltaTime;
            ReportViolation(peerId, ViolationType.SpeedHack, $"Speed: {speed:F1}, Max: {state.MaxSpeed}");
            return false;
        }
        
        var nativeValid = DuckovGuard.ValidatePosition((uint)peerId, newPos.X, newPos.Y, newPos.Z, deltaTime);
        
        lock (_lock)
        {
            state.LastPosition = newPos;
            state.LastPositionTime = DateTime.Now;
            state.PositionUpdateCount++;
        }
        
        return nativeValid;
    }
    
    public bool ValidateDamageReport(int peerId, int targetId, float damage, Vector3 attackerPos, Vector3 targetPos)
    {
        PlayerValidationState? state;
        lock (_lock)
        {
            if (!_playerStates.TryGetValue(peerId, out state))
                return true;
        }
        
        if (damage < 0 || damage > state.MaxDamage)
        {
            ReportViolation(peerId, ViolationType.DamageHack, $"Damage: {damage}, Max: {state.MaxDamage}");
            return false;
        }
        
        var distance = Vector3.Distance(attackerPos, targetPos);
        if (distance > state.MaxAttackRange)
        {
            ReportViolation(peerId, ViolationType.PositionHack, $"Attack range: {distance:F1}, Max: {state.MaxAttackRange}");
            return false;
        }
        
        return DuckovGuard.ValidateDamage((uint)peerId, targetId, damage, distance);
    }
    
    public bool ValidateHealthUpdate(int peerId, float oldHealth, float newHealth, float maxHealth)
    {
        if (newHealth > maxHealth + 0.1f)
        {
            ReportViolation(peerId, ViolationType.HealthHack, $"Health {newHealth} > Max {maxHealth}");
            return false;
        }
        
        if (newHealth > oldHealth + 50f && oldHealth > 0)
        {
            ReportViolation(peerId, ViolationType.HealthHack, $"Health jump: {oldHealth} -> {newHealth}");
            return false;
        }
        
        return DuckovGuard.ValidateHealth((uint)peerId, oldHealth, newHealth, maxHealth);
    }
    
    public bool ValidateItemPickup(int peerId, string itemId, Vector3 playerPos, Vector3 itemPos)
    {
        var distance = Vector3.Distance(playerPos, itemPos);
        const float MAX_PICKUP_RANGE = 5f;
        
        if (distance > MAX_PICKUP_RANGE)
        {
            ReportViolation(peerId, ViolationType.PositionHack, $"Pickup range: {distance:F1}");
            return false;
        }
        
        return true;
    }
    
    public bool ValidateRateLimit(int peerId, string actionType)
    {
        PlayerValidationState? state;
        lock (_lock)
        {
            if (!_playerStates.TryGetValue(peerId, out state))
                return true;
        }
        
        var now = DateTime.Now;
        if ((now - state.ActionWindowStart).TotalSeconds > 1)
        {
            state.ActionCountInWindow = 0;
            state.ActionWindowStart = now;
        }
        
        state.ActionCountInWindow++;
        
        if (state.ActionCountInWindow > state.RateLimit)
        {
            ReportViolation(peerId, ViolationType.RateLimit, $"Action: {actionType}, Count: {state.ActionCountInWindow}");
            return false;
        }
        
        return true;
    }
    
    private void ReportViolation(int peerId, ViolationType type, string details)
    {
        PlayerValidationState? state;
        lock (_lock)
        {
            if (!_playerStates.TryGetValue(peerId, out state))
                return;
            
            state.ViolationCount++;
        }
        
        Console.WriteLine($"[Violation] Player {peerId}: {type} - {details}");
        OnViolationDetected?.Invoke(peerId, type, details);
        
        if (state.ViolationCount >= 10)
        {
            Console.WriteLine($"[Validation] Player {peerId} has {state.ViolationCount} violations - consider kicking");
        }
    }
    
    public int GetViolationCount(int peerId)
    {
        lock (_lock)
        {
            return _playerStates.TryGetValue(peerId, out var state) ? state.ViolationCount : 0;
        }
    }
}

public class PlayerValidationState
{
    public int PeerId { get; set; }
    public DateTime JoinTime { get; set; }
    
    public Vector3 LastPosition { get; set; }
    public DateTime LastPositionTime { get; set; }
    public int PositionUpdateCount { get; set; }
    
    public float LastHealth { get; set; } = 100f;
    
    public int ViolationCount { get; set; }
    
    public DateTime ActionWindowStart { get; set; } = DateTime.Now;
    public int ActionCountInWindow { get; set; }
    
    public float MaxSpeed { get; set; } = 15f;
    public float MaxDamage { get; set; } = 500f;
    public float MaxAttackRange { get; set; } = 100f;
    public int RateLimit { get; set; } = 100;
}

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

namespace DuckovTogether.Core.GameLogic;

public enum AIBehaviorType
{
    Passive,
    Defensive,
    Aggressive,
    Boss
}

public class AIStateMachine
{
    public int EntityId { get; }
    public AIState CurrentState { get; private set; } = AIState.Idle;
    public AIBehaviorType BehaviorType { get; set; } = AIBehaviorType.Defensive;
    
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Vector3 Velocity { get; private set; }
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    
    public int? TargetPlayerId { get; private set; }
    public Vector3? TargetPosition { get; private set; }
    public Vector3? LastKnownPlayerPosition { get; private set; }
    
    public float DetectRange { get; set; } = 20f;
    public float AttackRange { get; set; } = 15f;
    public float MoveSpeed { get; set; } = 3f;
    public float AttackCooldown { get; set; } = 1f;
    public float Damage { get; set; } = 10f;
    
    private float _stateTimer;
    private float _attackTimer;
    private float _alertTimer;
    private readonly List<Vector3> _patrolPoints = new();
    private int _currentPatrolIndex;
    private readonly Random _random = new();
    
    public event Action<int, AIState, AIState>? OnStateChanged;
    public event Action<int, int, float>? OnAttack;
    public event Action<int>? OnDeath;
    
    public AIStateMachine(int entityId, float maxHealth)
    {
        EntityId = entityId;
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
    }
    
    public void SetPatrolPoints(IEnumerable<Vector3> points)
    {
        _patrolPoints.Clear();
        _patrolPoints.AddRange(points);
    }
    
    public void Update(float deltaTime, Dictionary<int, PlayerState> players)
    {
        if (CurrentState == AIState.Dead) return;
        
        _stateTimer += deltaTime;
        _attackTimer -= deltaTime;
        
        var nearestPlayer = FindNearestPlayer(players);
        
        switch (CurrentState)
        {
            case AIState.Idle:
                UpdateIdle(deltaTime, nearestPlayer);
                break;
            case AIState.Patrol:
                UpdatePatrol(deltaTime, nearestPlayer);
                break;
            case AIState.Alert:
                UpdateAlert(deltaTime, nearestPlayer);
                break;
            case AIState.Chase:
                UpdateChase(deltaTime, nearestPlayer);
                break;
            case AIState.Attack:
                UpdateAttack(deltaTime, nearestPlayer);
                break;
            case AIState.Cover:
                UpdateCover(deltaTime, nearestPlayer);
                break;
            case AIState.Flee:
                UpdateFlee(deltaTime, nearestPlayer);
                break;
            case AIState.Stunned:
                UpdateStunned(deltaTime);
                break;
        }
    }
    
    private (int playerId, Vector3 position, float distance)? FindNearestPlayer(Dictionary<int, PlayerState> players)
    {
        float nearestDist = float.MaxValue;
        (int playerId, Vector3 position, float distance)? nearest = null;
        
        foreach (var kvp in players)
        {
            if (!kvp.Value.IsInGame) continue;
            
            var dist = Vector3.Distance(Position, kvp.Value.Position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = (kvp.Key, kvp.Value.Position, dist);
            }
        }
        
        return nearest;
    }
    
    private void UpdateIdle(float deltaTime, (int playerId, Vector3 position, float distance)? nearestPlayer)
    {
        if (nearestPlayer.HasValue && nearestPlayer.Value.distance <= DetectRange)
        {
            TargetPlayerId = nearestPlayer.Value.playerId;
            LastKnownPlayerPosition = nearestPlayer.Value.position;
            TransitionTo(AIState.Alert);
            return;
        }
        
        if (_stateTimer > 3f + _random.NextSingle() * 2f && _patrolPoints.Count > 0)
        {
            TransitionTo(AIState.Patrol);
        }
    }
    
    private void UpdatePatrol(float deltaTime, (int playerId, Vector3 position, float distance)? nearestPlayer)
    {
        if (nearestPlayer.HasValue && nearestPlayer.Value.distance <= DetectRange)
        {
            TargetPlayerId = nearestPlayer.Value.playerId;
            LastKnownPlayerPosition = nearestPlayer.Value.position;
            TransitionTo(AIState.Alert);
            return;
        }
        
        if (_patrolPoints.Count == 0)
        {
            TransitionTo(AIState.Idle);
            return;
        }
        
        var target = _patrolPoints[_currentPatrolIndex];
        var direction = Vector3.Normalize(target - Position);
        Velocity = direction * MoveSpeed * 0.5f;
        Position += Velocity * deltaTime;
        
        if (Vector3.Distance(Position, target) < 1f)
        {
            _currentPatrolIndex = (_currentPatrolIndex + 1) % _patrolPoints.Count;
            if (_currentPatrolIndex == 0 && _random.NextSingle() > 0.5f)
            {
                TransitionTo(AIState.Idle);
            }
        }
    }
    
    private void UpdateAlert(float deltaTime, (int playerId, Vector3 position, float distance)? nearestPlayer)
    {
        _alertTimer += deltaTime;
        
        if (nearestPlayer.HasValue && nearestPlayer.Value.distance <= DetectRange)
        {
            LastKnownPlayerPosition = nearestPlayer.Value.position;
            TargetPlayerId = nearestPlayer.Value.playerId;
            
            if (_alertTimer > 1f)
            {
                if (nearestPlayer.Value.distance <= AttackRange)
                {
                    TransitionTo(AIState.Attack);
                }
                else
                {
                    TransitionTo(AIState.Chase);
                }
            }
        }
        else
        {
            if (_alertTimer > 5f)
            {
                TargetPlayerId = null;
                TransitionTo(AIState.Patrol);
            }
        }
        
        if (LastKnownPlayerPosition.HasValue)
        {
            var lookDir = LastKnownPlayerPosition.Value - Position;
            if (lookDir.LengthSquared() > 0.01f)
            {
                Rotation = new Vector3(0, MathF.Atan2(lookDir.X, lookDir.Z) * 57.2958f, 0);
            }
        }
    }
    
    private void UpdateChase(float deltaTime, (int playerId, Vector3 position, float distance)? nearestPlayer)
    {
        if (!nearestPlayer.HasValue || nearestPlayer.Value.distance > DetectRange * 1.5f)
        {
            if (LastKnownPlayerPosition.HasValue)
            {
                var distToLast = Vector3.Distance(Position, LastKnownPlayerPosition.Value);
                if (distToLast < 2f)
                {
                    LastKnownPlayerPosition = null;
                    TransitionTo(AIState.Alert);
                    return;
                }
                
                var direction = Vector3.Normalize(LastKnownPlayerPosition.Value - Position);
                Velocity = direction * MoveSpeed;
                Position += Velocity * deltaTime;
            }
            else
            {
                TransitionTo(AIState.Alert);
            }
            return;
        }
        
        LastKnownPlayerPosition = nearestPlayer.Value.position;
        TargetPlayerId = nearestPlayer.Value.playerId;
        
        if (nearestPlayer.Value.distance <= AttackRange)
        {
            TransitionTo(AIState.Attack);
            return;
        }
        
        var dir = Vector3.Normalize(nearestPlayer.Value.position - Position);
        Velocity = dir * MoveSpeed;
        Position += Velocity * deltaTime;
        Rotation = new Vector3(0, MathF.Atan2(dir.X, dir.Z) * 57.2958f, 0);
    }
    
    private void UpdateAttack(float deltaTime, (int playerId, Vector3 position, float distance)? nearestPlayer)
    {
        Velocity = Vector3.Zero;
        
        if (!nearestPlayer.HasValue || nearestPlayer.Value.distance > AttackRange * 1.2f)
        {
            TransitionTo(AIState.Chase);
            return;
        }
        
        LastKnownPlayerPosition = nearestPlayer.Value.position;
        var lookDir = nearestPlayer.Value.position - Position;
        if (lookDir.LengthSquared() > 0.01f)
        {
            Rotation = new Vector3(0, MathF.Atan2(lookDir.X, lookDir.Z) * 57.2958f, 0);
        }
        
        if (_attackTimer <= 0)
        {
            OnAttack?.Invoke(EntityId, nearestPlayer.Value.playerId, Damage);
            _attackTimer = AttackCooldown;
        }
        
        if (CurrentHealth < MaxHealth * 0.3f && BehaviorType != AIBehaviorType.Boss)
        {
            if (_random.NextSingle() > 0.7f)
            {
                TransitionTo(AIState.Cover);
            }
        }
    }
    
    private void UpdateCover(float deltaTime, (int playerId, Vector3 position, float distance)? nearestPlayer)
    {
        if (_stateTimer > 3f)
        {
            if (CurrentHealth > MaxHealth * 0.5f || BehaviorType == AIBehaviorType.Aggressive)
            {
                TransitionTo(AIState.Chase);
            }
            else if (nearestPlayer.HasValue && nearestPlayer.Value.distance > DetectRange)
            {
                TransitionTo(AIState.Patrol);
            }
        }
    }
    
    private void UpdateFlee(float deltaTime, (int playerId, Vector3 position, float distance)? nearestPlayer)
    {
        if (nearestPlayer.HasValue)
        {
            var awayDir = Vector3.Normalize(Position - nearestPlayer.Value.position);
            Velocity = awayDir * MoveSpeed * 1.2f;
            Position += Velocity * deltaTime;
            
            if (nearestPlayer.Value.distance > DetectRange * 2f)
            {
                TransitionTo(AIState.Idle);
            }
        }
        else
        {
            TransitionTo(AIState.Idle);
        }
    }
    
    private void UpdateStunned(float deltaTime)
    {
        Velocity = Vector3.Zero;
        if (_stateTimer > 2f)
        {
            TransitionTo(AIState.Alert);
        }
    }
    
    public void TakeDamage(float damage, int attackerId)
    {
        CurrentHealth -= damage;
        
        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            TransitionTo(AIState.Dead);
            OnDeath?.Invoke(EntityId);
            return;
        }
        
        if (CurrentState == AIState.Idle || CurrentState == AIState.Patrol)
        {
            TargetPlayerId = attackerId;
            TransitionTo(AIState.Alert);
        }
        
        if (CurrentHealth < MaxHealth * 0.2f && BehaviorType == AIBehaviorType.Passive)
        {
            TransitionTo(AIState.Flee);
        }
    }
    
    public void Stun(float duration = 2f)
    {
        if (CurrentState != AIState.Dead)
        {
            TransitionTo(AIState.Stunned);
        }
    }
    
    private void TransitionTo(AIState newState)
    {
        if (CurrentState == newState) return;
        
        var oldState = CurrentState;
        CurrentState = newState;
        _stateTimer = 0;
        _alertTimer = 0;
        Velocity = Vector3.Zero;
        
        OnStateChanged?.Invoke(EntityId, oldState, newState);
    }
}

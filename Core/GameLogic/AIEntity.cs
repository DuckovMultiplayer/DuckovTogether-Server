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

public enum AIState
{
    Idle = 0,
    Patrol = 1,
    Alert = 2,
    Chase = 3,
    Attack = 4,
    Cover = 5,
    Flee = 6,
    Dead = 7,
    Stunned = 8
}

public enum AIType
{
    Normal,
    Elite,
    Boss
}

public class AIEntity
{
    public int EntityId { get; set; }
    public string TypeName { get; set; } = "";
    public AIType Type { get; set; } = AIType.Normal;
    public AIState State { get; set; } = AIState.Idle;
    
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; set; } = Vector3.UnitZ;
    public float MoveSpeed { get; set; } = 3.5f;
    
    public float MaxHealth { get; set; } = 100f;
    public float CurrentHealth { get; set; } = 100f;
    public bool IsDead => CurrentHealth <= 0;
    
    public float DetectRange { get; set; } = 15f;
    public float AttackRange { get; set; } = 2f;
    public float AttackDamage { get; set; } = 10f;
    public float AttackCooldown { get; set; } = 1.5f;
    
    public string SceneId { get; set; } = "";
    public int SpawnerId { get; set; }
    
    public int? TargetPlayerId { get; set; }
    public Vector3? PatrolTarget { get; set; }
    public List<Vector3> PatrolPath { get; set; } = new();
    public int PatrolIndex { get; set; }
    
    public DateTime SpawnTime { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public DateTime LastAttackTime { get; set; }
    public DateTime LastStateChange { get; set; }
    
    public float Speed { get; set; }
    public float DirX { get; set; }
    public float DirY { get; set; }
    public int HandState { get; set; }
    public bool GunReady { get; set; }
    public bool Dashing { get; set; }
    
    public void TakeDamage(float damage, int fromPlayerId)
    {
        if (IsDead) return;
        
        CurrentHealth = Math.Max(0, CurrentHealth - damage);
        LastUpdateTime = DateTime.Now;
        
        if (CurrentHealth <= 0)
        {
            State = AIState.Dead;
            LastStateChange = DateTime.Now;
        }
        else if (State == AIState.Idle || State == AIState.Patrol)
        {
            TargetPlayerId = fromPlayerId;
            State = AIState.Chase;
            LastStateChange = DateTime.Now;
        }
    }
    
    public void MoveTo(Vector3 target, float deltaTime)
    {
        var direction = Vector3.Normalize(target - Position);
        var distance = Vector3.Distance(Position, target);
        var moveDistance = MoveSpeed * deltaTime;
        
        if (moveDistance >= distance)
        {
            Position = target;
        }
        else
        {
            Position += direction * moveDistance;
        }
        
        Forward = direction;
        LastUpdateTime = DateTime.Now;
    }
}

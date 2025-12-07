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
using LiteNetLib;

namespace DuckovTogether.Core;

public interface INetService
{
    bool IsRunning { get; }
    int PlayerCount { get; }
    void Start(int port, string key);
    void Stop();
    void Poll();
    void SendToAll(byte[] data, DeliveryMethod method);
    void SendToPeer(int peerId, byte[] data, DeliveryMethod method);
    void DisconnectPeer(int peerId, string reason);
    NetPeer? GetPeer(int peerId);
    IEnumerable<int> GetAllPeerIds();
}

public interface IPlayerSyncManager
{
    void UpdatePlayerPosition(int peerId, Vector3 position, Vector3 rotation);
    void UpdatePlayerAnimation(int peerId, float speed, float dirX, float dirY, int hand, bool gunReady, bool dashing, bool reloading);
    void UpdatePlayerEquipment(int peerId, int weaponId, int armorId, int helmetId, int[] hotbar);
    void UpdatePlayerHealth(int peerId, float health, float maxHealth);
    void OnPlayerJoin(int peerId, string name);
    void OnPlayerLeave(int peerId);
    void BroadcastPlayerState(int peerId);
    PlayerState? GetPlayer(int peerId);
    IEnumerable<PlayerState> GetAllPlayers();
}

public interface ICombatSyncManager
{
    void OnWeaponFire(int peerId, int weaponId, Vector3 origin, Vector3 direction, int ammoType);
    void OnPlayerDamage(int targetPeerId, int attackerPeerId, float damage, int damageType, Vector3 hitPoint);
    void OnAIDamage(int aiId, int attackerPeerId, float damage, int damageType, Vector3 hitPoint);
    void OnPlayerDeath(int peerId, int killerId);
    void OnAIDeath(int aiId, int killerId);
}

public interface IItemSyncManager
{
    void OnItemPickup(int peerId, string containerId, int slotIndex, int itemTypeId, int count);
    void OnItemDrop(int peerId, int itemTypeId, int count, Vector3 position);
    void OnLootContainerOpen(int peerId, string containerId);
    void SyncLootContainer(int peerId, string containerId);
}

public interface IWorldSyncManager
{
    void OnDoorInteract(string doorId, bool isOpen, int peerId);
    void OnDestructibleDestroy(string objectId, int peerId);
    void OnSceneChange(string sceneId);
    void SyncWorldState(int peerId);
}

public interface IAIManager
{
    void SpawnAI(int aiId, string aiType, Vector3 position, Vector3 rotation);
    void UpdateAIPosition(int aiId, Vector3 position, Vector3 rotation);
    void UpdateAIState(int aiId, int state);
    void UpdateAIHealth(int aiId, float health);
    void RemoveAI(int aiId);
    void Update(float deltaTime);
}

public interface IGameServer
{
    bool IsRunning { get; }
    string CurrentScene { get; }
    float GameTime { get; }
    void Start();
    void Stop();
    void Update(float deltaTime);
    void ChangeScene(string sceneId);
}

public interface ILogger
{
    void Log(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogError(string message, Exception ex);
}

public class ConsoleLogger : ILogger
{
    public void Log(string message) => Console.WriteLine($"[Info] {message}");
    public void LogWarning(string message) => Console.WriteLine($"[Warning] {message}");
    public void LogError(string message) => Console.WriteLine($"[Error] {message}");
    public void LogError(string message, Exception ex) => Console.WriteLine($"[Error] {message}: {ex.Message}");
}

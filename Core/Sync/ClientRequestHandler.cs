using DuckovTogether.Core.GameLogic;
using DuckovTogether.Core.World;
using DuckovNet;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class ClientRequestHandler
{
    private static ClientRequestHandler? _instance;
    public static ClientRequestHandler Instance => _instance ??= new ClientRequestHandler();
    
    public void HandleJsonRequest(int peerId, string json, NetPeer peer)
    {
        try
        {
            var baseMsg = JsonConvert.DeserializeObject<BaseRequest>(json);
            if (baseMsg == null) return;
            
            switch (baseMsg.type)
            {
                case "sceneVoteRequest":
                    HandleSceneVoteRequest(peerId, json);
                    break;
                    
                case "sceneVoteReady":
                    HandleSceneVoteReady(peerId, json);
                    break;
                    
                case "updateClientStatus":
                case "clientStatus":
                    HandleClientStatus(peerId, json);
                    break;
                    
                case "lootRequest":
                    HandleLootRequest(peerId, json, peer);
                    break;
                    
                case "itemDropRequest":
                    HandleItemDropRequest(peerId, json);
                    break;
                    
                case "itemPickupRequest":
                    HandleItemPickupRequest(peerId, json);
                    break;
                    
                case "damageReport":
                    HandleDamageReport(peerId, json);
                    break;
                    
                case "ai_health_report":
                    HandleAIHealthReport(peerId, json);
                    break;
                    
                default:
                    Console.WriteLine($"[RequestHandler] Unknown request type: {baseMsg.type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RequestHandler] Error: {ex.Message}");
        }
    }
    
    private void HandleSceneVoteRequest(int peerId, string json)
    {
        var req = JsonConvert.DeserializeObject<SceneVoteRequest>(json);
        if (req == null) return;
        
        var player = GameServer.Instance.Saves.PlayerSaves.Values
            .FirstOrDefault(p => p.PlayerId.GetHashCode() == peerId);
        
        WorldStateManager.Instance.StartVote(req.targetScene, player?.PlayerId ?? peerId.ToString());
        Console.WriteLine($"[Vote] Player {peerId} requested vote for: {req.targetScene}");
    }
    
    private void HandleSceneVoteReady(int peerId, string json)
    {
        var req = JsonConvert.DeserializeObject<SceneVoteReady>(json);
        if (req == null) return;
        
        WorldStateManager.Instance.SetVoteReady(peerId.ToString(), req.ready);
        Console.WriteLine($"[Vote] Player {peerId} set ready: {req.ready}");
    }
    
    private void HandleClientStatus(int peerId, string json)
    {
        var req = JsonConvert.DeserializeObject<ClientStatusUpdate>(json);
        if (req == null) return;
        
        var state = new PlayerState
        {
            PeerId = peerId,
            PlayerName = req.playerName ?? "",
            IsInGame = req.isInGame,
            SceneId = req.sceneId ?? "",
            Position = new System.Numerics.Vector3(req.posX, req.posY, req.posZ)
        };
        
        GameServer.Instance.UpdatePlayerState(peerId, state);
    }
    
    private void HandleLootRequest(int peerId, string json, NetPeer peer)
    {
        var req = JsonConvert.DeserializeObject<LootRequest>(json);
        if (req == null) return;
        
        var items = ItemManager.Instance.TryLootContainer(req.containerId, peerId);
        if (items != null)
        {
            var currentScene = GameServer.Instance.Saves.CurrentWorld.CurrentScene;
            SyncManager.Instance.SendLootFullSync(peer, currentScene);
        }
    }
    
    private void HandleItemDropRequest(int peerId, string json)
    {
        var req = JsonConvert.DeserializeObject<ItemDropRequest>(json);
        if (req == null) return;
        
        var currentScene = GameServer.Instance.Saves.CurrentWorld.CurrentScene;
        var dropId = ItemManager.Instance.DropItem(
            req.itemId,
            req.count,
            new System.Numerics.Vector3(req.posX, req.posY, req.posZ),
            currentScene,
            peerId
        );
        
        Console.WriteLine($"[Item] Player {peerId} dropped {req.itemId} x{req.count}");
    }
    
    private void HandleItemPickupRequest(int peerId, string json)
    {
        var req = JsonConvert.DeserializeObject<ItemPickupRequest>(json);
        if (req == null) return;
        
        var item = ItemManager.Instance.TryPickupItem(req.dropId, peerId);
        if (item != null)
        {
            Console.WriteLine($"[Item] Player {peerId} picked up {item.ItemId}");
        }
    }
    
    private void HandleDamageReport(int peerId, string json)
    {
        var req = JsonConvert.DeserializeObject<DamageReport>(json);
        if (req == null) return;
        
        if (req.targetType == "ai")
        {
            AIManager.Instance.DamageEntity(req.targetId, req.damage, peerId);
            
            var entity = AIManager.Instance.GetEntity(req.targetId);
            if (entity != null)
            {
                SyncManager.Instance.BroadcastAIHealth(entity.EntityId, entity.MaxHealth, entity.CurrentHealth);
                
                if (entity.IsDead)
                {
                    SyncManager.Instance.BroadcastAIDeath(entity.EntityId);
                }
            }
        }
    }
    
    private void HandleAIHealthReport(int peerId, string json)
    {
        var req = JsonConvert.DeserializeObject<AIHealthReport>(json);
        if (req == null) return;
        
        var entity = AIManager.Instance.GetEntity(req.aiId);
        if (entity != null)
        {
            entity.CurrentHealth = req.currentHealth;
            entity.MaxHealth = req.maxHealth;
            SyncManager.Instance.BroadcastAIHealth(entity.EntityId, entity.MaxHealth, entity.CurrentHealth);
        }
    }
}

public class BaseRequest
{
    public string type { get; set; } = "";
}

public class SceneVoteRequest
{
    public string type { get; set; } = "sceneVoteRequest";
    public string targetScene { get; set; } = "";
}

public class SceneVoteReady
{
    public string type { get; set; } = "sceneVoteReady";
    public bool ready { get; set; }
}

public class ClientStatusUpdate
{
    public string type { get; set; } = "updateClientStatus";
    public string? playerName { get; set; }
    public bool isInGame { get; set; }
    public string? sceneId { get; set; }
    public float posX { get; set; }
    public float posY { get; set; }
    public float posZ { get; set; }
}

public class LootRequest
{
    public string type { get; set; } = "lootRequest";
    public string containerId { get; set; } = "";
}

public class ItemDropRequest
{
    public string type { get; set; } = "itemDropRequest";
    public string itemId { get; set; } = "";
    public int count { get; set; }
    public float posX { get; set; }
    public float posY { get; set; }
    public float posZ { get; set; }
}

public class ItemPickupRequest
{
    public string type { get; set; } = "itemPickupRequest";
    public string dropId { get; set; } = "";
}

public class DamageReport
{
    public string type { get; set; } = "damageReport";
    public string targetType { get; set; } = "";
    public int targetId { get; set; }
    public float damage { get; set; }
}

public class AIHealthReport
{
    public string type { get; set; } = "ai_health_report";
    public int aiId { get; set; }
    public float maxHealth { get; set; }
    public float currentHealth { get; set; }
}

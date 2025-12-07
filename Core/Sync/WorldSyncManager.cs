using System.Numerics;
using DuckovTogether.Core.Assets;
using DuckovTogether.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace DuckovTogether.Core.Sync;

public class WorldSyncManager
{
    private static WorldSyncManager? _instance;
    public static WorldSyncManager Instance => _instance ??= new WorldSyncManager();
    
    private HeadlessNetService? _netService;
    private readonly NetDataWriter _writer = new();
    
    private float _timeOfDay = 12f;
    private int _weather = 0;
    private float _weatherIntensity = 0f;
    private string _currentScene = "";
    
    private DateTime _lastTimeSync = DateTime.Now;
    private DateTime _lastWeatherSync = DateTime.Now;
    
    private const double TIME_SYNC_INTERVAL = 5.0;
    private const double WEATHER_SYNC_INTERVAL = 10.0;
    private const float TIME_SPEED = 0.1f;
    
    public float TimeOfDay => _timeOfDay;
    public int Weather => _weather;
    public string CurrentScene => _currentScene;
    
    public void Initialize(HeadlessNetService netService)
    {
        _netService = netService;
        Console.WriteLine("[WorldSync] Initialized");
    }
    
    public void Update(float deltaTime)
    {
        _timeOfDay += deltaTime * TIME_SPEED;
        if (_timeOfDay >= 24f) _timeOfDay -= 24f;
        
        var now = DateTime.Now;
        
        if ((now - _lastTimeSync).TotalSeconds >= TIME_SYNC_INTERVAL)
        {
            BroadcastTimeSync();
            _lastTimeSync = now;
        }
        
        if ((now - _lastWeatherSync).TotalSeconds >= WEATHER_SYNC_INTERVAL)
        {
            _lastWeatherSync = now;
        }
    }
    
    public void SetTimeOfDay(float time)
    {
        _timeOfDay = time % 24f;
        BroadcastTimeSync();
    }
    
    public void SetWeather(int weatherType, float intensity)
    {
        _weather = weatherType;
        _weatherIntensity = intensity;
        BroadcastWeatherSync();
    }
    
    public void OnSceneLoad(string sceneId)
    {
        _currentScene = sceneId;
        
        var data = new SceneLoadSync
        {
            type = "sceneLoad",
            sceneId = sceneId,
            timeOfDay = _timeOfDay,
            weather = _weather,
            weatherIntensity = _weatherIntensity,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        BroadcastJson(data);
        Console.WriteLine($"[WorldSync] Scene loaded: {sceneId}");
    }
    
    public void OnDoorInteract(int doorId, bool isOpen, int playerId)
    {
        var data = new DoorInteractSync
        {
            type = "doorInteract",
            doorId = doorId,
            isOpen = isOpen,
            playerId = playerId,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnSwitchInteract(int switchId, bool isOn, int playerId)
    {
        var data = new SwitchInteractSync
        {
            type = "switchInteract",
            switchId = switchId,
            isOn = isOn,
            playerId = playerId,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    private readonly Dictionary<int, ExtractingPlayerState> _extractingPlayers = new();
    
    public bool TryStartExtract(string extractPointId, int playerId, out string? error)
    {
        error = null;
        
        var validation = GameDataValidator.Instance.ValidateExtractPoint(extractPointId, _currentScene);
        if (!validation.IsValid)
        {
            error = validation.ErrorMessage;
            Console.WriteLine($"[WorldSync] Extract validation failed for player {playerId}: {error}");
            return false;
        }
        
        var extractData = GameDataValidator.Instance.GetExtractData(extractPointId);
        float extractTime = extractData?.ExtractTime ?? 10f;
        
        _extractingPlayers[playerId] = new ExtractingPlayerState
        {
            PlayerId = playerId,
            ExtractPointId = extractPointId,
            StartTime = DateTime.Now,
            RequiredTime = extractTime
        };
        
        var data = new ExtractSync
        {
            type = "extractStart",
            extractId = extractPointId.GetHashCode(),
            extractPointId = extractPointId,
            playerId = playerId,
            extractTime = extractTime,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
        Console.WriteLine($"[WorldSync] Player {playerId} started extracting at {extractPointId}");
        return true;
    }
    
    public void CancelExtract(int playerId)
    {
        if (_extractingPlayers.Remove(playerId, out var state))
        {
            var data = new ExtractSync
            {
                type = "extractCancel",
                extractId = state.ExtractPointId.GetHashCode(),
                extractPointId = state.ExtractPointId,
                playerId = playerId,
                timestamp = DateTime.Now.Ticks
            };
            BroadcastJson(data);
        }
    }
    
    public void CompleteExtract(int playerId)
    {
        if (_extractingPlayers.Remove(playerId, out var state))
        {
            var data = new ExtractSync
            {
                type = "extractComplete",
                extractId = state.ExtractPointId.GetHashCode(),
                extractPointId = state.ExtractPointId,
                playerId = playerId,
                timestamp = DateTime.Now.Ticks
            };
            BroadcastJson(data);
            Console.WriteLine($"[WorldSync] Player {playerId} extracted at {state.ExtractPointId}");
        }
    }
    
    public void OnExtractStart(int extractId, int playerId)
    {
        var data = new ExtractSync
        {
            type = "extractStart",
            extractId = extractId,
            playerId = playerId,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnExtractComplete(int extractId, int playerId)
    {
        var data = new ExtractSync
        {
            type = "extractComplete",
            extractId = extractId,
            playerId = playerId,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void OnDestructibleDestroy(int objectId, Vector3 position, int destroyerId)
    {
        var data = new DestructibleSync
        {
            type = "destructibleDestroy",
            objectId = objectId,
            position = new Vec3 { x = position.X, y = position.Y, z = position.Z },
            destroyerId = destroyerId,
            timestamp = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    public void SendFullWorldState(NetPeer peer)
    {
        var data = new WorldStateSync
        {
            type = "worldState",
            sceneId = _currentScene,
            timeOfDay = _timeOfDay,
            weather = _weather,
            weatherIntensity = _weatherIntensity,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
        
        SendJsonToPeer(peer, data);
    }
    
    private void BroadcastTimeSync()
    {
        var data = new TimeSyncMessage
        {
            type = "timeSync",
            timeOfDay = _timeOfDay,
            serverTime = DateTime.Now.Ticks
        };
        
        BroadcastJson(data);
    }
    
    private void BroadcastWeatherSync()
    {
        var data = new WeatherSyncMessage
        {
            type = "weatherSync",
            weather = _weather,
            intensity = _weatherIntensity
        };
        
        BroadcastJson(data);
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
    
    private void SendJsonToPeer(NetPeer peer, object data)
    {
        var json = JsonConvert.SerializeObject(data);
        _writer.Reset();
        _writer.Put((byte)9);
        _writer.Put(json);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
}

public class SceneLoadSync
{
    public string type { get; set; } = "sceneLoad";
    public string sceneId { get; set; } = "";
    public float timeOfDay { get; set; }
    public int weather { get; set; }
    public float weatherIntensity { get; set; }
    public string timestamp { get; set; } = "";
}

public class WorldStateSync
{
    public string type { get; set; } = "worldState";
    public string sceneId { get; set; } = "";
    public float timeOfDay { get; set; }
    public int weather { get; set; }
    public float weatherIntensity { get; set; }
    public string timestamp { get; set; } = "";
}

public class TimeSyncMessage
{
    public string type { get; set; } = "timeSync";
    public float timeOfDay { get; set; }
    public long serverTime { get; set; }
}

public class WeatherSyncMessage
{
    public string type { get; set; } = "weatherSync";
    public int weather { get; set; }
    public float intensity { get; set; }
}

public class DoorInteractSync
{
    public string type { get; set; } = "doorInteract";
    public int doorId { get; set; }
    public bool isOpen { get; set; }
    public int playerId { get; set; }
    public long timestamp { get; set; }
}

public class SwitchInteractSync
{
    public string type { get; set; } = "switchInteract";
    public int switchId { get; set; }
    public bool isOn { get; set; }
    public int playerId { get; set; }
    public long timestamp { get; set; }
}

public class ExtractSync
{
    public string type { get; set; } = "";
    public int extractId { get; set; }
    public string extractPointId { get; set; } = "";
    public int playerId { get; set; }
    public float extractTime { get; set; }
    public long timestamp { get; set; }
}

public class ExtractingPlayerState
{
    public int PlayerId { get; set; }
    public string ExtractPointId { get; set; } = "";
    public DateTime StartTime { get; set; }
    public float RequiredTime { get; set; }
}

public class DestructibleSync
{
    public string type { get; set; } = "destructibleDestroy";
    public int objectId { get; set; }
    public Vec3 position { get; set; } = new();
    public int destroyerId { get; set; }
    public long timestamp { get; set; }
}

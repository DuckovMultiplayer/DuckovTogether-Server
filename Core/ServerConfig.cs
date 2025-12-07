// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using Newtonsoft.Json;

namespace DuckovTogether.Core;

public class ServerConfig
{
    public int Port { get; set; } = 9050;
    public int MaxPlayers { get; set; } = 4;
    public string ServerName { get; set; } = "Duckov Headless Server";
    public string GameKey { get; set; } = "gameKey";
    public int TickRate { get; set; } = 60;
    public float SyncInterval { get; set; } = 0.015f;
    public bool EnableBroadcast { get; set; } = true;
    public string GamePath { get; set; } = "";
    public string ExtractedDataPath { get; set; } = "Data";
    
    public static ServerConfig Load(string path)
    {
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ServerConfig>(json) ?? new ServerConfig();
        }
        return new ServerConfig();
    }
    
    public void Save(string path)
    {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(path, json);
    }
}

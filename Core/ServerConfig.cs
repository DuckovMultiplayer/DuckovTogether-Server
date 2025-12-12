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
    public string ServerDescription { get; set; } = "Welcome to Duckov Together!";
    public string ServerIcon { get; set; } = "default";
    public string GameKey { get; set; } = "DuckovTogether_v2";
    public int TickRate { get; set; } = 60;
    public float SyncInterval { get; set; } = 0.015f;
    public bool EnableBroadcast { get; set; } = true;
    public string GamePath { get; set; } = "";
    public string ExtractedDataPath { get; set; } = "Data";
    public string CertPath { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public string DefaultScene { get; set; } = "Base_SceneV2";
    public List<string> KnownScenes { get; set; } = new()
    {
        "Startup",
        "MainMenu", 
        "Base_SceneV2",
        "Level_Factory",
        "Level_Woods",
        "Level_Customs",
        "Level_Reserve"
    };
    
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

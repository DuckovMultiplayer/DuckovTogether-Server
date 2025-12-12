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
    public string DefaultScene { get; set; } = "Level_Base";
    public List<string> KnownScenes { get; set; } = new()
    {
        "Startup",
        "MainMenu",
        "Level_Base",
        "Level_0",
        "Level_1",
        "Level_2",
        "Level_4",
        "Level_7",
        "Level_8",
        "Level_9",
        "Level_20",
        "Level_26",
        "Level_27",
        "Level_28",
        "Level_29",
        "Level_33",
        "Level_42",
        "Level_43",
        "Level_44",
        "Level_47",
        "Level_Farm_01",
        "Level_Farm_JLab_Facility",
        "Level_GroundZero_1",
        "Level_Guide_Main",
        "Level_HiddenWarehouse",
        "Level_JLab_2",
        "Level_stormZone_1",
        "Level_DemoChallenge_Main",
        "Level_GroundZero_Main"
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

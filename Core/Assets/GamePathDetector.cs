// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace DuckovTogether.Core.Assets;

public static class GamePathDetector
{
    private const string GAME_NAME = "Escape from Duckov";
    private const string STEAM_APP_ID = "2778580";
    
    public static string? DetectGamePath()
    {
        Console.WriteLine("[GamePath] Searching for game installation...");
        
        var path = TryFromSteamRegistry();
        if (!string.IsNullOrEmpty(path)) return path;
        
        path = TryFromSteamLibraryFolders();
        if (!string.IsNullOrEmpty(path)) return path;
        
        path = TryCommonPaths();
        if (!string.IsNullOrEmpty(path)) return path;
        
        Console.WriteLine("[GamePath] Could not auto-detect game path");
        return null;
    }
    
    private static string? TryFromSteamRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + STEAM_APP_ID);
            if (key != null)
            {
                var installLocation = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(installLocation) && ValidateGamePath(installLocation))
                {
                    Console.WriteLine($"[GamePath] Found via registry: {installLocation}");
                    return installLocation;
                }
            }
            
            using var key64 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + STEAM_APP_ID);
            if (key64 != null)
            {
                var installLocation = key64.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(installLocation) && ValidateGamePath(installLocation))
                {
                    Console.WriteLine($"[GamePath] Found via registry (64-bit): {installLocation}");
                    return installLocation;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GamePath] Registry search failed: {ex.Message}");
        }
        
        return null;
    }
    
    private static string? TryFromSteamLibraryFolders()
    {
        try
        {
            var steamPath = GetSteamInstallPath();
            if (string.IsNullOrEmpty(steamPath)) return null;
            
            var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                Console.WriteLine("[GamePath] libraryfolders.vdf not found");
                return null;
            }
            
            var content = File.ReadAllText(libraryFoldersPath);
            var libraryPaths = ParseLibraryFolders(content);
            
            libraryPaths.Insert(0, steamPath);
            
            foreach (var libPath in libraryPaths)
            {
                var gamePath = Path.Combine(libPath, "steamapps", "common", GAME_NAME);
                if (ValidateGamePath(gamePath))
                {
                    Console.WriteLine($"[GamePath] Found in Steam library: {gamePath}");
                    return gamePath;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GamePath] Steam library search failed: {ex.Message}");
        }
        
        return null;
    }
    
    private static string? GetSteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key != null)
            {
                var steamPath = key.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    return steamPath.Replace("/", "\\");
                }
            }
            
            using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key32 != null)
            {
                var installPath = key32.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    return installPath;
                }
            }
        }
        catch { }
        
        var defaultPaths = new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\Steam",
            @"E:\Steam",
            @"D:\SteamLibrary",
            @"E:\SteamLibrary"
        };
        
        foreach (var path in defaultPaths)
        {
            if (Directory.Exists(path))
                return path;
        }
        
        return null;
    }
    
    private static List<string> ParseLibraryFolders(string vdfContent)
    {
        var paths = new List<string>();
        
        var pathRegex = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
        var matches = pathRegex.Matches(vdfContent);
        
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }
        }
        
        return paths;
    }
    
    private static string? TryCommonPaths()
    {
        var commonPaths = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\" + GAME_NAME,
            @"C:\Program Files\Steam\steamapps\common\" + GAME_NAME,
            @"D:\Steam\steamapps\common\" + GAME_NAME,
            @"E:\Steam\steamapps\common\" + GAME_NAME,
            @"D:\SteamLibrary\steamapps\common\" + GAME_NAME,
            @"E:\SteamLibrary\steamapps\common\" + GAME_NAME,
            @"F:\SteamLibrary\steamapps\common\" + GAME_NAME,
            @"G:\SteamLibrary\steamapps\common\" + GAME_NAME,
        };
        
        foreach (var path in commonPaths)
        {
            if (ValidateGamePath(path))
            {
                Console.WriteLine($"[GamePath] Found at common path: {path}");
                return path;
            }
        }
        
        return null;
    }
    
    public static bool ValidateGamePath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;
        
        var exePath = Path.Combine(path, "Duckov.exe");
        var dataPath = Path.Combine(path, "Duckov_Data");
        
        return File.Exists(exePath) && Directory.Exists(dataPath);
    }
}

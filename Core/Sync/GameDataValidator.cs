// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

using DuckovTogether.Core.Assets;
using DuckovTogetherServer.Core.Logging;

namespace DuckovTogether.Core.Sync;

public class GameDataValidator
{
    private static GameDataValidator? _instance;
    public static GameDataValidator Instance => _instance ??= new GameDataValidator();
    
    private bool _initialized = false;
    
    public void Initialize()
    {
        _initialized = SceneDataManager.Instance.Scenes.Count > 0;
        Log.Info($"Validator initialized, data available: {_initialized}");
    }
    
    public bool IsDataAvailable => _initialized;
    
    public ValidationResult ValidateExtractPoint(string extractId, string currentScene)
    {
        if (!_initialized)
            return ValidationResult.Success();
            
        var extract = SceneDataManager.Instance.GetExtract(extractId);
        if (extract == null)
            return ValidationResult.Fail($"Unknown extract point: {extractId}");
            
        return ValidationResult.Success();
    }
    
    public ValidationResult ValidateWeapon(int weaponId)
    {
        if (!_initialized)
            return ValidationResult.Success();
            
        var weapon = SceneDataManager.Instance.GetWeapon(weaponId);
        if (weapon == null)
            return ValidationResult.Fail($"Unknown weapon ID: {weaponId}");
            
        return ValidationResult.SuccessWithData(weapon);
    }
    
    public ValidationResult ValidateItem(int itemTypeId)
    {
        if (!_initialized)
            return ValidationResult.Success();
            
        var item = SceneDataManager.Instance.GetItem(itemTypeId);
        if (item == null)
            return ValidationResult.Fail($"Unknown item type: {itemTypeId}");
            
        return ValidationResult.SuccessWithData(item);
    }
    
    public ValidationResult ValidateScene(string sceneId)
    {
        if (!_initialized)
            return ValidationResult.Success();
            
        var scene = SceneDataManager.Instance.GetScene(sceneId);
        if (scene == null)
            return ValidationResult.Fail($"Unknown scene: {sceneId}");
            
        return ValidationResult.SuccessWithData(scene);
    }
    
    public ValidationResult ValidateDoor(string doorId)
    {
        if (!_initialized)
            return ValidationResult.Success();
            
        var door = SceneDataManager.Instance.GetDoor(doorId);
        if (door == null)
            return ValidationResult.Fail($"Unknown door: {doorId}");
            
        return ValidationResult.SuccessWithData(door);
    }
    
    public WeaponData? GetWeaponData(int weaponId) => SceneDataManager.Instance.GetWeapon(weaponId);
    public ParsedItemData? GetItemData(int itemTypeId) => SceneDataManager.Instance.GetItem(itemTypeId);
    public ExtractPointData? GetExtractData(string extractId) => SceneDataManager.Instance.GetExtract(extractId);
}

public class ValidationResult
{
    public bool IsValid { get; private set; }
    public string? ErrorMessage { get; private set; }
    public object? Data { get; private set; }
    
    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult SuccessWithData(object data) => new() { IsValid = true, Data = data };
    public static ValidationResult Fail(string message) => new() { IsValid = false, ErrorMessage = message };
}

// -----------------------------------------------------------------------
// Duckov Together Server
// Copyright (c) Duckov Team. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root
// for full license information.
// 
// This software is provided "AS IS", without warranty of any kind.
// Commercial use requires explicit written permission from the authors.
// -----------------------------------------------------------------------

namespace DuckovTogether.Core;

public interface IServiceContainer
{
    void Register<TInterface, TImplementation>() where TImplementation : TInterface, new();
    void Register<TInterface>(TInterface instance);
    void Register<TInterface>(Func<TInterface> factory);
    TInterface Resolve<TInterface>();
    TInterface? TryResolve<TInterface>();
    bool IsRegistered<TInterface>();
}

public class ServiceContainer : IServiceContainer
{
    private static ServiceContainer? _instance;
    public static ServiceContainer Instance => _instance ??= new ServiceContainer();
    
    private readonly Dictionary<Type, object> _services = new();
    private readonly Dictionary<Type, Func<object>> _factories = new();
    private readonly object _lock = new();
    
    public void Register<TInterface, TImplementation>() where TImplementation : TInterface, new()
    {
        lock (_lock)
        {
            _factories[typeof(TInterface)] = () => new TImplementation()!;
        }
    }
    
    public void Register<TInterface>(TInterface instance)
    {
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        lock (_lock)
        {
            _services[typeof(TInterface)] = instance;
        }
    }
    
    public void Register<TInterface>(Func<TInterface> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        lock (_lock)
        {
            _factories[typeof(TInterface)] = () => factory()!;
        }
    }
    
    public TInterface Resolve<TInterface>()
    {
        lock (_lock)
        {
            var type = typeof(TInterface);
            
            if (_services.TryGetValue(type, out var service))
                return (TInterface)service;
            
            if (_factories.TryGetValue(type, out var factory))
            {
                var instance = (TInterface)factory();
                _services[type] = instance!;
                return instance;
            }
            
            throw new InvalidOperationException($"Service {type.Name} is not registered");
        }
    }
    
    public TInterface? TryResolve<TInterface>()
    {
        try
        {
            return Resolve<TInterface>();
        }
        catch
        {
            return default;
        }
    }
    
    public bool IsRegistered<TInterface>()
    {
        lock (_lock)
        {
            var type = typeof(TInterface);
            return _services.ContainsKey(type) || _factories.ContainsKey(type);
        }
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _services.Clear();
            _factories.Clear();
        }
    }
}

public static class Services
{
    public static IServiceContainer Container => ServiceContainer.Instance;
    
    public static TInterface Get<TInterface>() => Container.Resolve<TInterface>();
    public static TInterface? TryGet<TInterface>() => Container.TryResolve<TInterface>();
}

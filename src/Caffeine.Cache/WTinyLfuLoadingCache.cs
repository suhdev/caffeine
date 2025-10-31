// Copyright 2014 Ben Manes. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Concurrent;
using Caffeine.Cache.Stats;

namespace Caffeine.Cache;

/// <summary>
/// A loading cache implementation using the Window TinyLFU eviction policy.
/// Automatically loads values using a cache loader when keys are not present.
/// </summary>
internal sealed class WTinyLfuLoadingCache<K, V> : ICache<K, V> where K : notnull
{
    private readonly WTinyLfuCache<K, V> _cache;
    private readonly ICacheLoader<K, V> _loader;
    private readonly ConcurrentDictionary<K, object> _loadingLocks = new();
    private long _loadSuccessCount;
    private long _loadFailureCount;
    private long _totalLoadTime;
    
    public WTinyLfuLoadingCache(Caffeine<K, V> builder, ICacheLoader<K, V> loader)
    {
        _cache = new WTinyLfuCache<K, V>(builder);
        _loader = loader;
    }
    
    public V? GetIfPresent(K key)
    {
        return _cache.GetIfPresent(key);
    }
    
    public V? Get(K key)
    {
        ArgumentNullException.ThrowIfNull(key);
        
        var value = _cache.GetIfPresent(key);
        if (value != null)
            return value;
        
        // Load value
        var lockObj = _loadingLocks.GetOrAdd(key, _ => new object());
        
        try
        {
            lock (lockObj)
            {
                // Double-check after acquiring lock
                value = _cache.GetIfPresent(key);
                if (value != null)
                    return value;
                
                var startTime = DateTime.UtcNow.Ticks;
                try
                {
                    value = _loader.Load(key);
                    var loadTime = DateTime.UtcNow.Ticks - startTime;
                    
                    Interlocked.Increment(ref _loadSuccessCount);
                    Interlocked.Add(ref _totalLoadTime, loadTime);
                    
                    if (value != null)
                    {
                        _cache.Put(key, value);
                    }
                    
                    return value;
                }
                catch
                {
                    var loadTime = DateTime.UtcNow.Ticks - startTime;
                    Interlocked.Increment(ref _loadFailureCount);
                    Interlocked.Add(ref _totalLoadTime, loadTime);
                    throw;
                }
            }
        }
        finally
        {
            _loadingLocks.TryRemove(key, out _);
        }
    }
    
    public V? Get(K key, Func<K, V?> mappingFunction)
    {
        return _cache.Get(key, mappingFunction);
    }
    
    public IDictionary<K, V> GetAll(IEnumerable<K> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        
        var result = new Dictionary<K, V>();
        var missingKeys = new HashSet<K>();
        
        foreach (var key in keys)
        {
            var value = _cache.GetIfPresent(key);
            if (value != null)
            {
                result[key] = value;
            }
            else
            {
                missingKeys.Add(key);
            }
        }
        
        if (missingKeys.Count > 0)
        {
            var startTime = DateTime.UtcNow.Ticks;
            try
            {
                var loaded = _loader.LoadAll(missingKeys);
                var loadTime = DateTime.UtcNow.Ticks - startTime;
                
                Interlocked.Increment(ref _loadSuccessCount);
                Interlocked.Add(ref _totalLoadTime, loadTime);
                
                foreach (var kvp in loaded)
                {
                    _cache.Put(kvp.Key, kvp.Value);
                    result[kvp.Key] = kvp.Value;
                }
            }
            catch
            {
                var loadTime = DateTime.UtcNow.Ticks - startTime;
                Interlocked.Increment(ref _loadFailureCount);
                Interlocked.Add(ref _totalLoadTime, loadTime);
                throw;
            }
        }
        
        return result;
    }
    
    public void Put(K key, V value)
    {
        _cache.Put(key, value);
    }
    
    public void PutAll(IDictionary<K, V> map)
    {
        _cache.PutAll(map);
    }
    
    public void Invalidate(K key)
    {
        _cache.Invalidate(key);
    }
    
    public void InvalidateAll(IEnumerable<K> keys)
    {
        _cache.InvalidateAll(keys);
    }
    
    public void InvalidateAll()
    {
        _cache.InvalidateAll();
    }
    
    public long EstimatedSize() => _cache.EstimatedSize();
    
    public ICacheStats Stats()
    {
        var baseStats = _cache.Stats();
        long loadSuccess = Interlocked.Read(ref _loadSuccessCount);
        long loadFailure = Interlocked.Read(ref _loadFailureCount);
        long totalTime = Interlocked.Read(ref _totalLoadTime);
        
        return CacheStats.Of(
            baseStats.HitCount(),
            baseStats.MissCount(),
            loadSuccess,
            loadFailure,
            totalTime,
            baseStats.EvictionCount(),
            baseStats.EvictionWeight()
        );
    }
    
    public void CleanUp()
    {
        _cache.CleanUp();
    }
    
    public ConcurrentDictionary<K, V> AsMap()
    {
        return _cache.AsMap();
    }
    
    public IPolicy<K, V> Policy() => _cache.Policy();
    
    public IReadOnlyDictionary<K, V> GetAllPresent(IEnumerable<K> keys)
    {
        return _cache.GetAllPresent(keys);
    }
    
    public IReadOnlyDictionary<K, V> GetAll(IEnumerable<K> keys, Func<ISet<K>, IDictionary<K, V>> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(mappingFunction);
        
        var keySet = new HashSet<K>(keys);
        var result = new Dictionary<K, V>();
        var missingKeys = new HashSet<K>();
        
        foreach (var key in keySet)
        {
            var value = _cache.GetIfPresent(key);
            if (value != null)
            {
                result[key] = value;
            }
            else
            {
                missingKeys.Add(key);
            }
        }
        
        if (missingKeys.Count > 0)
        {
            var computed = mappingFunction(missingKeys);
            foreach (var kvp in computed)
            {
                _cache.Put(kvp.Key, kvp.Value);
                result[kvp.Key] = kvp.Value;
            }
        }
        
        return result;
    }
}

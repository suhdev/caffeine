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
/// A simple concurrent cache implementation backed by <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// This is a basic implementation without advanced features like eviction policies.
/// </summary>
internal sealed class SimpleConcurrentCache<K, V> : ICache<K, V> where K : notnull
{
    private readonly ConcurrentDictionary<K, V> _map;
    private readonly bool _recordStats;
    private long _hitCount;
    private long _missCount;

    public SimpleConcurrentCache(Caffeine<K, V> builder)
    {
        int initialCapacity = builder.GetInitialCapacity();
        _map = new ConcurrentDictionary<K, V>(
            Environment.ProcessorCount,
            initialCapacity);
        _recordStats = builder.ShouldRecordStats();
    }

    public V? GetIfPresent(K key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_map.TryGetValue(key, out var value))
        {
            if (_recordStats) Interlocked.Increment(ref _hitCount);
            return value;
        }

        if (_recordStats) Interlocked.Increment(ref _missCount);
        return default;
    }

    public V? Get(K key, Func<K, V?> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mappingFunction);

        if (_map.TryGetValue(key, out var value))
        {
            if (_recordStats) Interlocked.Increment(ref _hitCount);
            return value;
        }

        if (_recordStats) Interlocked.Increment(ref _missCount);

        var newValue = _map.GetOrAdd(key, k =>
        {
            var computed = mappingFunction(k);
            return computed ?? throw new InvalidOperationException("Mapping function returned null");
        });

        return newValue;
    }

    public IReadOnlyDictionary<K, V> GetAllPresent(IEnumerable<K> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<K, V>();
        foreach (var key in keys)
        {
            if (key == null)
            {
                throw new ArgumentException("Keys collection contains null element", nameof(keys));
            }

            if (_map.TryGetValue(key, out var value))
            {
                result[key] = value;
            }
        }

        return result;
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
            if (_map.TryGetValue(key, out var value))
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
            var loaded = mappingFunction(missingKeys);
            foreach (var entry in loaded)
            {
                _map[entry.Key] = entry.Value;
                result[entry.Key] = entry.Value;
            }
        }

        return result;
    }

    public void Put(K key, V value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        _map[key] = value;
    }

    public void PutAll(IDictionary<K, V> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        foreach (var entry in map)
        {
            if (entry.Key == null || entry.Value == null)
            {
                throw new ArgumentException("Map contains null keys or values", nameof(map));
            }
            _map[entry.Key] = entry.Value;
        }
    }

    public void Invalidate(K key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _map.TryRemove(key, out _);
    }

    public void InvalidateAll(IEnumerable<K> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            if (key == null)
            {
                throw new ArgumentException("Keys collection contains null element", nameof(keys));
            }
            _map.TryRemove(key, out _);
        }
    }

    public void InvalidateAll()
    {
        _map.Clear();
    }

    public long EstimatedSize()
    {
        return _map.Count;
    }

    public ICacheStats Stats()
    {
        if (!_recordStats)
        {
            return CacheStats.Empty();
        }

        return CacheStats.Of(
            hitCount: Interlocked.Read(ref _hitCount),
            missCount: Interlocked.Read(ref _missCount),
            loadSuccessCount: 0,
            loadFailureCount: 0,
            totalLoadTime: 0,
            evictionCount: 0,
            evictionWeight: 0);
    }

    public ConcurrentDictionary<K, V> AsMap()
    {
        return _map;
    }

    public void CleanUp()
    {
        // No-op for simple cache
    }

    public IPolicy<K, V> Policy()
    {
        return new SimplePolicy<K, V>();
    }
}

/// <summary>
/// Simple policy implementation for caches without eviction.
/// </summary>
internal sealed class SimplePolicy<K, V> : IPolicy<K, V> where K : notnull
{
    // Empty policy for simple cache
}

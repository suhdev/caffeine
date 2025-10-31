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
/// A loading cache implementation with size-based eviction using LRU policy.
/// </summary>
internal sealed class BoundedLoadingCache<K, V> : ICache<K, V> where K : notnull
{
    private readonly ConcurrentDictionary<K, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _accessOrder;
    private readonly object _evictionLock = new object();
    private readonly Func<K, V?> _loader;
    private readonly long _maximumSize;
    private readonly bool _recordStats;
    private long _hitCount;
    private long _missCount;
    private long _loadSuccessCount;
    private long _loadFailureCount;
    private long _totalLoadTimeNanos;
    private long _evictionCount;

    private sealed class CacheEntry
    {
        public K Key { get; }
        public V Value { get; }

        public CacheEntry(K key, V value)
        {
            Key = key;
            Value = value;
        }
    }

    public BoundedLoadingCache(Caffeine<K, V> builder, Func<K, V?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        int initialCapacity = builder.GetInitialCapacity();
        _maximumSize = builder.GetMaximumSize();
        _map = new ConcurrentDictionary<K, LinkedListNode<CacheEntry>>(
            Environment.ProcessorCount,
            initialCapacity);
        _accessOrder = new LinkedList<CacheEntry>();
        _loader = loader;
        _recordStats = builder.ShouldRecordStats();
    }

    public V? GetIfPresent(K key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_map.TryGetValue(key, out var node))
        {
            // Move to end (most recently used)
            lock (_evictionLock)
            {
                _accessOrder.Remove(node);
                _accessOrder.AddLast(node);
            }

            if (_recordStats) Interlocked.Increment(ref _hitCount);
            return node.Value.Value;
        }

        if (_recordStats) Interlocked.Increment(ref _missCount);
        return default;
    }

    public V? Get(K key, Func<K, V?> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mappingFunction);

        if (_map.TryGetValue(key, out var node))
        {
            // Move to end (most recently used)
            lock (_evictionLock)
            {
                _accessOrder.Remove(node);
                _accessOrder.AddLast(node);
            }

            if (_recordStats) Interlocked.Increment(ref _hitCount);
            return node.Value.Value;
        }

        if (_recordStats) Interlocked.Increment(ref _missCount);

        return LoadValue(key, mappingFunction);
    }

    private V? LoadValue(K key, Func<K, V?> mappingFunction)
    {
        long startTime = _recordStats ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        try
        {
            var newValue = mappingFunction(key);
            if (newValue == null)
            {
                if (_recordStats)
                {
                    long endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                    long elapsedNanos = (endTime - startTime) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency;
                    Interlocked.Add(ref _totalLoadTimeNanos, elapsedNanos);
                    Interlocked.Increment(ref _loadFailureCount);
                }
                return default;
            }

            PutInternal(key, newValue);

            if (_recordStats)
            {
                long endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                long elapsedNanos = (endTime - startTime) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency;
                Interlocked.Add(ref _totalLoadTimeNanos, elapsedNanos);
                Interlocked.Increment(ref _loadSuccessCount);
            }

            return newValue;
        }
        catch
        {
            if (_recordStats)
            {
                long endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                long elapsedNanos = (endTime - startTime) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency;
                Interlocked.Add(ref _totalLoadTimeNanos, elapsedNanos);
                Interlocked.Increment(ref _loadFailureCount);
            }
            throw;
        }
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

            var value = GetIfPresent(key);
            if (value != null)
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
            var value = GetIfPresent(key);
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
            var loaded = mappingFunction(missingKeys);
            foreach (var entry in loaded)
            {
                PutInternal(entry.Key, entry.Value);
                result[entry.Key] = entry.Value;
            }
        }

        return result;
    }

    public void Put(K key, V value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        PutInternal(key, value);
    }

    private void PutInternal(K key, V value)
    {
        lock (_evictionLock)
        {
            // Remove existing entry if present
            if (_map.TryRemove(key, out var existingNode))
            {
                _accessOrder.Remove(existingNode);
            }

            // Add new entry
            var entry = new CacheEntry(key, value);
            var node = new LinkedListNode<CacheEntry>(entry);
            _accessOrder.AddLast(node);
            _map[key] = node;

            // Evict if over size
            while (_accessOrder.Count > _maximumSize)
            {
                var lruNode = _accessOrder.First;
                if (lruNode != null)
                {
                    _accessOrder.RemoveFirst();
                    _map.TryRemove(lruNode.Value.Key, out _);
                    if (_recordStats) Interlocked.Increment(ref _evictionCount);
                }
            }
        }
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
            PutInternal(entry.Key, entry.Value);
        }
    }

    public void Invalidate(K key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_evictionLock)
        {
            if (_map.TryRemove(key, out var node))
            {
                _accessOrder.Remove(node);
            }
        }
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
            Invalidate(key);
        }
    }

    public void InvalidateAll()
    {
        lock (_evictionLock)
        {
            _map.Clear();
            _accessOrder.Clear();
        }
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
            loadSuccessCount: Interlocked.Read(ref _loadSuccessCount),
            loadFailureCount: Interlocked.Read(ref _loadFailureCount),
            totalLoadTime: Interlocked.Read(ref _totalLoadTimeNanos),
            evictionCount: Interlocked.Read(ref _evictionCount),
            evictionWeight: 0);
    }

    public ConcurrentDictionary<K, V> AsMap()
    {
        var snapshot = new ConcurrentDictionary<K, V>();
        
        lock (_evictionLock)
        {
            foreach (var entry in _accessOrder)
            {
                snapshot[entry.Key] = entry.Value;
            }
        }

        return snapshot;
    }

    public void CleanUp()
    {
        // No-op for bounded cache - eviction happens on writes
    }

    public IPolicy<K, V> Policy()
    {
        return new SimplePolicy<K, V>();
    }
}

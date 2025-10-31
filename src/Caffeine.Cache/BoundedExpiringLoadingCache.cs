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
/// A loading cache implementation with both size-based eviction (LRU) and time-based expiration.
/// </summary>
internal sealed class BoundedExpiringLoadingCache<K, V> : ICache<K, V> where K : notnull
{
    private readonly ConcurrentDictionary<K, LinkedListNode<BoundedTimedEntry<K, V>>> _map;
    private readonly LinkedList<BoundedTimedEntry<K, V>> _accessOrder;
    private readonly object _evictionLock = new object();
    private readonly Func<K, V?> _loader;
    private readonly long _maximumSize;
    private readonly long _expireAfterWriteNanos;
    private readonly long _expireAfterAccessNanos;
    private readonly bool _recordStats;
    private long _hitCount;
    private long _missCount;
    private long _loadSuccessCount;
    private long _loadFailureCount;
    private long _totalLoadTimeNanos;
    private long _evictionCount;

    public BoundedExpiringLoadingCache(Caffeine<K, V> builder, Func<K, V?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        int initialCapacity = builder.GetInitialCapacity();
        _maximumSize = builder.GetMaximumSize();
        _expireAfterWriteNanos = builder.GetExpireAfterWriteNanos();
        _expireAfterAccessNanos = builder.GetExpireAfterAccessNanos();
        _map = new ConcurrentDictionary<K, LinkedListNode<BoundedTimedEntry<K, V>>>(
            Environment.ProcessorCount,
            initialCapacity);
        _accessOrder = new LinkedList<BoundedTimedEntry<K, V>>();
        _loader = loader;
        _recordStats = builder.ShouldRecordStats();
    }

    public V? GetIfPresent(K key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_map.TryGetValue(key, out var node))
        {
            long now = DateTime.UtcNow.Ticks;
            
            // Check if entry has expired
            if (IsExpired(node.Value, now))
            {
                // Remove expired entry
                lock (_evictionLock)
                {
                    if (_map.TryRemove(key, out var removedNode))
                    {
                        _accessOrder.Remove(removedNode);
                        if (_recordStats) Interlocked.Increment(ref _evictionCount);
                    }
                }
                if (_recordStats) Interlocked.Increment(ref _missCount);
                return default;
            }

            // Move to end (most recently used) and update access time
            lock (_evictionLock)
            {
                _accessOrder.Remove(node);
                _accessOrder.AddLast(node);
                
                // Update access time if tracking access-based expiration
                if (_expireAfterAccessNanos >= 0)
                {
                    node.Value.UpdateAccessTime(now);
                }
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

        long now = DateTime.UtcNow.Ticks;

        if (_map.TryGetValue(key, out var node))
        {
            // Check if entry has expired
            if (!IsExpired(node.Value, now))
            {
                // Move to end (most recently used) and update access time
                lock (_evictionLock)
                {
                    _accessOrder.Remove(node);
                    _accessOrder.AddLast(node);
                    
                    // Update access time if tracking
                    if (_expireAfterAccessNanos >= 0)
                    {
                        node.Value.UpdateAccessTime(now);
                    }
                }

                if (_recordStats) Interlocked.Increment(ref _hitCount);
                return node.Value.Value;
            }
            else
            {
                // Remove expired entry
                lock (_evictionLock)
                {
                    if (_map.TryRemove(key, out var removedNode))
                    {
                        _accessOrder.Remove(removedNode);
                        if (_recordStats) Interlocked.Increment(ref _evictionCount);
                    }
                }
            }
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
        long now = DateTime.UtcNow.Ticks;

        lock (_evictionLock)
        {
            // Remove existing entry if present
            if (_map.TryRemove(key, out var existingNode))
            {
                _accessOrder.Remove(existingNode);
            }

            // Add new entry
            var entry = new BoundedTimedEntry<K, V>(key, value, now);
            var node = new LinkedListNode<BoundedTimedEntry<K, V>>(entry);
            _accessOrder.AddLast(node);
            _map[key] = node;

            // Evict expired and LRU entries if over size
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
        // Clean up expired entries first
        CleanUp();
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
        long now = DateTime.UtcNow.Ticks;

        lock (_evictionLock)
        {
            foreach (var entry in _accessOrder)
            {
                if (!IsExpired(entry, now))
                {
                    snapshot[entry.Key] = entry.Value;
                }
            }
        }

        return snapshot;
    }

    public void CleanUp()
    {
        // Proactively remove expired entries
        long now = DateTime.UtcNow.Ticks;
        
        lock (_evictionLock)
        {
            var expiredKeys = new List<K>();
            
            foreach (var entry in _accessOrder)
            {
                if (IsExpired(entry, now))
                {
                    expiredKeys.Add(entry.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                if (_map.TryRemove(key, out var node))
                {
                    _accessOrder.Remove(node);
                    if (_recordStats) Interlocked.Increment(ref _evictionCount);
                }
            }
        }
    }

    public IPolicy<K, V> Policy()
    {
        return new SimplePolicy<K, V>();
    }

    private bool IsExpired(BoundedTimedEntry<K, V> entry, long currentTicks)
    {
        if (_expireAfterWriteNanos >= 0 && entry.IsExpiredAfterWrite(currentTicks, _expireAfterWriteNanos))
        {
            return true;
        }

        if (_expireAfterAccessNanos >= 0 && entry.IsExpiredAfterAccess(currentTicks, _expireAfterAccessNanos))
        {
            return true;
        }

        return false;
    }
}

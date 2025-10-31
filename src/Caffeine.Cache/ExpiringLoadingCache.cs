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
/// A loading cache implementation with time-based expiration support.
/// </summary>
internal sealed class ExpiringLoadingCache<K, V> : ICache<K, V> where K : notnull
{
    private readonly ConcurrentDictionary<K, TimedEntry<V>> _map;
    private readonly Func<K, V?> _loader;
    private readonly long _expireAfterWriteNanos;
    private readonly long _expireAfterAccessNanos;
    private readonly bool _recordStats;
    private long _hitCount;
    private long _missCount;
    private long _loadSuccessCount;
    private long _loadFailureCount;
    private long _totalLoadTimeNanos;
    private long _evictionCount;

    public ExpiringLoadingCache(Caffeine<K, V> builder, Func<K, V?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        int initialCapacity = builder.GetInitialCapacity();
        _map = new ConcurrentDictionary<K, TimedEntry<V>>(
            Environment.ProcessorCount,
            initialCapacity);
        _loader = loader;
        _expireAfterWriteNanos = builder.GetExpireAfterWriteNanos();
        _expireAfterAccessNanos = builder.GetExpireAfterAccessNanos();
        _recordStats = builder.ShouldRecordStats();
    }

    public V? GetIfPresent(K key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_map.TryGetValue(key, out var entry))
        {
            long now = DateTime.UtcNow.Ticks;
            
            // Check if entry has expired
            if (IsExpired(entry, now))
            {
                // Try to remove the expired entry
                if (_map.TryRemove(key, out _))
                {
                    if (_recordStats) Interlocked.Increment(ref _evictionCount);
                }
                if (_recordStats) Interlocked.Increment(ref _missCount);
                return default;
            }

            // Update access time if we're tracking access-based expiration
            if (_expireAfterAccessNanos >= 0)
            {
                entry.UpdateAccessTime(now);
            }

            if (_recordStats) Interlocked.Increment(ref _hitCount);
            return entry.Value;
        }

        if (_recordStats) Interlocked.Increment(ref _missCount);
        return default;
    }

    public V? Get(K key, Func<K, V?> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mappingFunction);

        long now = DateTime.UtcNow.Ticks;

        // Try to get existing value
        if (_map.TryGetValue(key, out var entry))
        {
            // Check if entry has expired
            if (!IsExpired(entry, now))
            {
                // Update access time if tracking
                if (_expireAfterAccessNanos >= 0)
                {
                    entry.UpdateAccessTime(now);
                }

                if (_recordStats) Interlocked.Increment(ref _hitCount);
                return entry.Value;
            }
            else
            {
                // Remove expired entry
                _map.TryRemove(key, out _);
                if (_recordStats) Interlocked.Increment(ref _evictionCount);
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
            var newValue = _map.GetOrAdd(key, k =>
            {
                var computed = mappingFunction(k);
                if (computed == null)
                {
                    throw new InvalidOperationException("Mapping function returned null");
                }
                return new TimedEntry<V>(computed, DateTime.UtcNow.Ticks);
            });

            if (_recordStats)
            {
                long endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                long elapsedNanos = (endTime - startTime) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency;
                Interlocked.Add(ref _totalLoadTimeNanos, elapsedNanos);
                Interlocked.Increment(ref _loadSuccessCount);
            }

            return newValue.Value;
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

        long now = DateTime.UtcNow.Ticks;
        var result = new Dictionary<K, V>();
        var expiredKeys = new List<K>();

        foreach (var key in keys)
        {
            if (key == null)
            {
                throw new ArgumentException("Keys collection contains null element", nameof(keys));
            }

            if (_map.TryGetValue(key, out var entry))
            {
                if (!IsExpired(entry, now))
                {
                    // Update access time if tracking
                    if (_expireAfterAccessNanos >= 0)
                    {
                        entry.UpdateAccessTime(now);
                    }
                    result[key] = entry.Value;
                }
                else
                {
                    expiredKeys.Add(key);
                }
            }
        }

        // Clean up expired entries
        foreach (var key in expiredKeys)
        {
            if (_map.TryRemove(key, out _))
            {
                if (_recordStats) Interlocked.Increment(ref _evictionCount);
            }
        }

        return result;
    }

    public IReadOnlyDictionary<K, V> GetAll(IEnumerable<K> keys, Func<ISet<K>, IDictionary<K, V>> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(mappingFunction);

        long now = DateTime.UtcNow.Ticks;
        var keySet = new HashSet<K>(keys);
        var result = new Dictionary<K, V>();
        var missingKeys = new HashSet<K>();
        var expiredKeys = new List<K>();

        foreach (var key in keySet)
        {
            if (_map.TryGetValue(key, out var entry))
            {
                if (!IsExpired(entry, now))
                {
                    // Update access time if tracking
                    if (_expireAfterAccessNanos >= 0)
                    {
                        entry.UpdateAccessTime(now);
                    }
                    result[key] = entry.Value;
                }
                else
                {
                    expiredKeys.Add(key);
                    missingKeys.Add(key);
                }
            }
            else
            {
                missingKeys.Add(key);
            }
        }

        // Clean up expired entries
        foreach (var key in expiredKeys)
        {
            if (_map.TryRemove(key, out _))
            {
                if (_recordStats) Interlocked.Increment(ref _evictionCount);
            }
        }

        if (missingKeys.Count > 0)
        {
            var loaded = mappingFunction(missingKeys);
            foreach (var kvp in loaded)
            {
                var timedEntry = new TimedEntry<V>(kvp.Value, now);
                _map[kvp.Key] = timedEntry;
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    public void Put(K key, V value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        long now = DateTime.UtcNow.Ticks;
        var entry = new TimedEntry<V>(value, now);
        _map[key] = entry;
    }

    public void PutAll(IDictionary<K, V> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        long now = DateTime.UtcNow.Ticks;

        foreach (var kvp in map)
        {
            if (kvp.Key == null || kvp.Value == null)
            {
                throw new ArgumentException("Map contains null keys or values", nameof(map));
            }
            var entry = new TimedEntry<V>(kvp.Value, now);
            _map[kvp.Key] = entry;
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
        // Return a snapshot that unwraps the timed entries
        var snapshot = new ConcurrentDictionary<K, V>();
        long now = DateTime.UtcNow.Ticks;

        foreach (var kvp in _map)
        {
            if (!IsExpired(kvp.Value, now))
            {
                snapshot[kvp.Key] = kvp.Value.Value;
            }
        }

        return snapshot;
    }

    public void CleanUp()
    {
        // Proactively remove expired entries
        long now = DateTime.UtcNow.Ticks;
        var expiredKeys = new List<K>();

        foreach (var kvp in _map)
        {
            if (IsExpired(kvp.Value, now))
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            if (_map.TryRemove(key, out _))
            {
                if (_recordStats) Interlocked.Increment(ref _evictionCount);
            }
        }
    }

    public IPolicy<K, V> Policy()
    {
        return new SimplePolicy<K, V>();
    }

    private bool IsExpired(TimedEntry<V> entry, long currentTicks)
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

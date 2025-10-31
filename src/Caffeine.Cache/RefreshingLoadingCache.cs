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
/// A loading cache with automatic refresh support.
/// When an entry becomes stale (after RefreshAfterWrite duration), it is asynchronously 
/// refreshed on the next access, but the stale value is still returned to the caller.
/// </summary>
internal sealed class RefreshingLoadingCache<K, V> : ICache<K, V> where K : notnull
{
    private readonly ConcurrentDictionary<K, RefreshEntry> _map;
    private readonly ICacheLoader<K, V> _loader;
    private readonly long _refreshAfterWriteNanos;
    private readonly bool _recordStats;
    private readonly RemovalListener<K, V>? _removalListener;
    
    private long _hitCount;
    private long _missCount;
    private long _loadSuccessCount;
    private long _loadFailureCount;
    private long _totalLoadTimeNanos;
    private long _evictionCount;

    // Track ongoing refreshes to avoid duplicate refresh tasks
    private readonly ConcurrentDictionary<K, Task> _refreshTasks = new();

    private sealed class RefreshEntry
    {
        public V Value;
        public long WriteTimeNanos;
        public volatile bool IsRefreshing;

        public RefreshEntry(V value, long writeTimeNanos)
        {
            Value = value;
            WriteTimeNanos = writeTimeNanos;
            IsRefreshing = false;
        }
    }

    public RefreshingLoadingCache(
        Caffeine<K, V> builder, 
        ICacheLoader<K, V> loader,
        long refreshAfterWriteNanos)
    {
        ArgumentNullException.ThrowIfNull(loader);

        int initialCapacity = builder.GetInitialCapacity();
        _map = new ConcurrentDictionary<K, RefreshEntry>(
            Environment.ProcessorCount,
            initialCapacity);
        _loader = loader;
        _refreshAfterWriteNanos = refreshAfterWriteNanos;
        _recordStats = builder.ShouldRecordStats();
        _removalListener = builder.GetRemovalListener();
    }

    public V? GetIfPresent(K key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_map.TryGetValue(key, out var entry))
        {
            if (_recordStats) Interlocked.Increment(ref _hitCount);
            
            // Check if entry needs refresh (stale but still valid)
            long now = DateTime.UtcNow.Ticks * 100; // Convert to nanoseconds
            if (now - entry.WriteTimeNanos > _refreshAfterWriteNanos && !entry.IsRefreshing)
            {
                // Trigger async refresh but return stale value
                TriggerAsyncRefresh(key, entry);
            }
            
            return entry.Value;
        }

        if (_recordStats) Interlocked.Increment(ref _missCount);
        return default;
    }

    public V? Get(K key, Func<K, V?> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mappingFunction);

        if (_map.TryGetValue(key, out var entry))
        {
            if (_recordStats) Interlocked.Increment(ref _hitCount);
            
            // Check if entry needs refresh
            long now = DateTime.UtcNow.Ticks * 100;
            if (now - entry.WriteTimeNanos > _refreshAfterWriteNanos && !entry.IsRefreshing)
            {
                TriggerAsyncRefresh(key, entry);
            }
            
            return entry.Value;
        }

        if (_recordStats) Interlocked.Increment(ref _missCount);

        return LoadValue(key, mappingFunction);
    }

    private V? LoadValue(K key, Func<K, V?> mappingFunction)
    {
        long startTime = _recordStats ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        try
        {
            var computed = mappingFunction(key);
            if (computed == null)
            {
                if (_recordStats) Interlocked.Increment(ref _loadFailureCount);
                return default;
            }

            long now = DateTime.UtcNow.Ticks * 100;
            var newEntry = new RefreshEntry(computed, now);
            _map[key] = newEntry;

            if (_recordStats)
            {
                long endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                long elapsedNanos = (endTime - startTime) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency;
                Interlocked.Add(ref _totalLoadTimeNanos, elapsedNanos);
                Interlocked.Increment(ref _loadSuccessCount);
            }

            return computed;
        }
        catch
        {
            if (_recordStats) Interlocked.Increment(ref _loadFailureCount);
            throw;
        }
    }

    private void TriggerAsyncRefresh(K key, RefreshEntry entry)
    {
        // Mark as refreshing to prevent duplicate refreshes
        if (!entry.IsRefreshing)
        {
            entry.IsRefreshing = true;

            // Start async refresh task
            var refreshTask = Task.Run(async () =>
            {
                try
                {
                    long startTime = _recordStats ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

                    // Use async load if available
                    var newValue = await _loader.AsyncLoad(key);

                    if (newValue != null)
                    {
                        long now = DateTime.UtcNow.Ticks * 100;
                        var oldValue = entry.Value;
                        
                        // Update with refreshed value
                        entry.Value = newValue;
                        entry.WriteTimeNanos = now;

                        // Notify listener of replacement
                        if (_removalListener != null && !EqualityComparer<V>.Default.Equals(oldValue, newValue))
                        {
                            NotifyRemoval(key, oldValue, RemovalCause.Replaced);
                        }

                        if (_recordStats)
                        {
                            long endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                            long elapsedNanos = (endTime - startTime) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency;
                            Interlocked.Add(ref _totalLoadTimeNanos, elapsedNanos);
                            Interlocked.Increment(ref _loadSuccessCount);
                        }
                    }
                }
                catch
                {
                    // Refresh failed, keep stale value and allow retry on next access
                    if (_recordStats) Interlocked.Increment(ref _loadFailureCount);
                }
                finally
                {
                    entry.IsRefreshing = false;
                    _refreshTasks.TryRemove(key, out _);
                }
            });

            _refreshTasks[key] = refreshTask;
        }
    }

    public void Put(K key, V value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        long now = DateTime.UtcNow.Ticks * 100;
        var newEntry = new RefreshEntry(value, now);

        if (_map.TryGetValue(key, out var oldEntry))
        {
            _map[key] = newEntry;
            if (_removalListener != null)
            {
                NotifyRemoval(key, oldEntry.Value, RemovalCause.Replaced);
            }
        }
        else
        {
            _map[key] = newEntry;
        }
    }

    public void PutAll(IDictionary<K, V> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        long now = DateTime.UtcNow.Ticks * 100;
        foreach (var kvp in map)
        {
            var newEntry = new RefreshEntry(kvp.Value, now);
            if (_map.TryGetValue(kvp.Key, out var oldEntry))
            {
                _map[kvp.Key] = newEntry;
                if (_removalListener != null)
                {
                    NotifyRemoval(kvp.Key, oldEntry.Value, RemovalCause.Replaced);
                }
            }
            else
            {
                _map[kvp.Key] = newEntry;
            }
        }
    }

    public void Invalidate(K key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_map.TryRemove(key, out var entry))
        {
            if (_recordStats) Interlocked.Increment(ref _evictionCount);
            if (_removalListener != null)
            {
                NotifyRemoval(key, entry.Value, RemovalCause.Explicit);
            }
        }
    }

    public void InvalidateAll(IEnumerable<K> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            Invalidate(key);
        }
    }

    public void InvalidateAll()
    {
        var keys = _map.Keys.ToList();
        foreach (var key in keys)
        {
            if (_map.TryRemove(key, out var entry))
            {
                if (_recordStats) Interlocked.Increment(ref _evictionCount);
                if (_removalListener != null)
                {
                    NotifyRemoval(key, entry.Value, RemovalCause.Explicit);
                }
            }
        }
    }

    public long EstimatedSize() => _map.Count;

    public ICacheStats Stats()
    {
        if (!_recordStats)
        {
            return CacheStats.Empty();
        }

        return CacheStats.Of(
            Interlocked.Read(ref _hitCount),
            Interlocked.Read(ref _missCount),
            Interlocked.Read(ref _loadSuccessCount),
            Interlocked.Read(ref _loadFailureCount),
            Interlocked.Read(ref _totalLoadTimeNanos),
            Interlocked.Read(ref _evictionCount),
            0 // evictionWeight not used yet
        );
    }

    public IReadOnlyDictionary<K, V> GetAllPresent(IEnumerable<K> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<K, V>();
        long now = DateTime.UtcNow.Ticks * 100;

        foreach (var key in keys)
        {
            if (_map.TryGetValue(key, out var entry))
            {
                if (_recordStats) Interlocked.Increment(ref _hitCount);
                result[key] = entry.Value;

                // Check if needs refresh
                if (now - entry.WriteTimeNanos > _refreshAfterWriteNanos && !entry.IsRefreshing)
                {
                    TriggerAsyncRefresh(key, entry);
                }
            }
            else
            {
                if (_recordStats) Interlocked.Increment(ref _missCount);
            }
        }

        return result;
    }

    public IReadOnlyDictionary<K, V> GetAll(IEnumerable<K> keys, Func<ISet<K>, IDictionary<K, V>> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(mappingFunction);

        var result = new Dictionary<K, V>();
        var keysToLoad = new HashSet<K>();
        long now = DateTime.UtcNow.Ticks * 100;

        foreach (var key in keys)
        {
            if (_map.TryGetValue(key, out var entry))
            {
                if (_recordStats) Interlocked.Increment(ref _hitCount);
                result[key] = entry.Value;

                // Check if needs refresh
                if (now - entry.WriteTimeNanos > _refreshAfterWriteNanos && !entry.IsRefreshing)
                {
                    TriggerAsyncRefresh(key, entry);
                }
            }
            else
            {
                if (_recordStats) Interlocked.Increment(ref _missCount);
                keysToLoad.Add(key);
            }
        }

        if (keysToLoad.Count > 0)
        {
            long startTime = _recordStats ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

            try
            {
                var loaded = mappingFunction(keysToLoad);
                foreach (var kvp in loaded)
                {
                    if (keysToLoad.Contains(kvp.Key))
                    {
                        var newEntry = new RefreshEntry(kvp.Value, now);
                        _map[kvp.Key] = newEntry;
                        result[kvp.Key] = kvp.Value;
                    }
                }

                if (_recordStats)
                {
                    long endTime = System.Diagnostics.Stopwatch.GetTimestamp();
                    long elapsedNanos = (endTime - startTime) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency;
                    Interlocked.Add(ref _totalLoadTimeNanos, elapsedNanos);
                    Interlocked.Increment(ref _loadSuccessCount);
                }
            }
            catch
            {
                if (_recordStats) Interlocked.Increment(ref _loadFailureCount);
                throw;
            }
        }

        return result;
    }

    public void CleanUp()
    {
        // For refresh cache, cleanup mainly involves removing completed refresh tasks
        // No explicit expiration cleanup needed since refresh doesn't remove entries
    }

    public IPolicy<K, V> Policy() => throw new NotImplementedException();

    public ConcurrentDictionary<K, V> AsMap()
    {
        var result = new ConcurrentDictionary<K, V>();
        foreach (var kvp in _map)
        {
            result[kvp.Key] = kvp.Value.Value;
        }
        return result;
    }

    private void NotifyRemoval(K key, V value, RemovalCause cause)
    {
        try
        {
            _removalListener?.Invoke(key, value, cause);
        }
        catch
        {
            // Suppress listener exceptions
        }
    }
}

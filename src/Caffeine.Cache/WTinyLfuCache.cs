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
/// A cache implementation using the Window TinyLFU eviction policy.
/// Combines frequency-based and recency-based eviction for optimal hit rates.
/// </summary>
internal sealed class WTinyLfuCache<K, V> : ICache<K, V> where K : notnull
{
    private readonly ConcurrentDictionary<K, CacheEntry> _map;
    private readonly LinkedList<CacheEntry> _windowQueue; // FIFO for new entries
    private readonly LinkedList<CacheEntry> _probationQueue; // LRU for established entries
    private readonly LinkedList<CacheEntry> _protectedQueue; // LRU for hot entries
    private readonly FrequencySketch<K> _sketch;
    private readonly object _evictionLock = new object();
    private readonly long _maximumSize;
    private readonly int _windowSize; // 1% of maximum size
    private readonly int _protectedSize; // 80% of main cache
    private readonly bool _recordStats;
    private readonly RemovalListener<K, V>? _removalListener;
    
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;
    private int _windowCount;
    private int _probationCount;
    private int _protectedCount;
    
    private enum QueueType
    {
        Window,
        Probation,
        Protected
    }
    
    private sealed class CacheEntry
    {
        public K Key { get; }
        public V Value { get; set; }
        public QueueType Queue { get; set; }
        public LinkedListNode<CacheEntry>? Node { get; set; }
        
        public CacheEntry(K key, V value, QueueType queue)
        {
            Key = key;
            Value = value;
            Queue = queue;
        }
    }
    
    public WTinyLfuCache(Caffeine<K, V> builder)
    {
        int initialCapacity = builder.GetInitialCapacity();
        _maximumSize = builder.GetMaximumSize();
        _recordStats = builder.ShouldRecordStats();
        _removalListener = builder.GetRemovalListener();
        
        // Window: 1% of maximum size (minimum 1)
        _windowSize = Math.Max(1, (int)(_maximumSize / 100));
        
        // Main cache: 99% of maximum size, split 80/20 between protected and probation
        int mainSize = (int)_maximumSize - _windowSize;
        _protectedSize = (int)(mainSize * 0.8);
        
        _map = new ConcurrentDictionary<K, CacheEntry>(Environment.ProcessorCount, initialCapacity);
        _windowQueue = new LinkedList<CacheEntry>();
        _probationQueue = new LinkedList<CacheEntry>();
        _protectedQueue = new LinkedList<CacheEntry>();
        _sketch = new FrequencySketch<K>(_maximumSize);
    }
    
    public V? GetIfPresent(K key)
    {
        ArgumentNullException.ThrowIfNull(key);
        
        if (_map.TryGetValue(key, out var entry))
        {
            if (_recordStats)
                Interlocked.Increment(ref _hitCount);
            
            // Record access in frequency sketch
            _sketch.Increment(key);
            
            // Promote if in probation queue
            OnAccess(entry);
            
            return entry.Value;
        }
        
        if (_recordStats)
            Interlocked.Increment(ref _missCount);
        
        return default;
    }
    
    public V? Get(K key, Func<K, V?> mappingFunction)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mappingFunction);
        
        if (_map.TryGetValue(key, out var entry))
        {
            if (_recordStats)
                Interlocked.Increment(ref _hitCount);
            
            _sketch.Increment(key);
            OnAccess(entry);
            return entry.Value;
        }
        
        if (_recordStats)
            Interlocked.Increment(ref _missCount);
        
        var value = mappingFunction(key);
        if (value != null)
        {
            Put(key, value);
        }
        return value;
    }
    
    public void Put(K key, V value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        
        _sketch.Increment(key);
        
        if (_map.TryGetValue(key, out var existingEntry))
        {
            // Update existing entry
            lock (_evictionLock)
            {
                var oldValue = existingEntry.Value;
                existingEntry.Value = value;
                NotifyRemoval(key, oldValue, RemovalCause.Replaced);
                OnAccess(existingEntry);
            }
        }
        else
        {
            // Add new entry to window
            lock (_evictionLock)
            {
                var newEntry = new CacheEntry(key, value, QueueType.Window);
                if (_map.TryAdd(key, newEntry))
                {
                    var node = _windowQueue.AddLast(newEntry);
                    newEntry.Node = node;
                    _windowCount++;
                    
                    // Evict if necessary
                    while (_windowCount + _probationCount + _protectedCount > _maximumSize)
                    {
                        Evict();
                    }
                }
            }
        }
    }
    
    private void OnAccess(CacheEntry entry)
    {
        lock (_evictionLock)
        {
            if (entry.Queue == QueueType.Window)
            {
                // Move to end of window queue
                if (entry.Node != null)
                {
                    _windowQueue.Remove(entry.Node);
                    entry.Node = _windowQueue.AddLast(entry);
                }
            }
            else if (entry.Queue == QueueType.Probation)
            {
                // Promote to protected queue
                if (entry.Node != null)
                {
                    _probationQueue.Remove(entry.Node);
                    _probationCount--;
                }
                
                entry.Queue = QueueType.Protected;
                entry.Node = _protectedQueue.AddLast(entry);
                _protectedCount++;
                
                // Demote from protected if too large
                while (_protectedCount > _protectedSize && _protectedQueue.First != null)
                {
                    var victim = _protectedQueue.First.Value;
                    _protectedQueue.RemoveFirst();
                    _protectedCount--;
                    
                    victim.Queue = QueueType.Probation;
                    victim.Node = _probationQueue.AddLast(victim);
                    _probationCount++;
                }
            }
            else if (entry.Queue == QueueType.Protected)
            {
                // Move to end of protected queue (LRU)
                if (entry.Node != null)
                {
                    _protectedQueue.Remove(entry.Node);
                    entry.Node = _protectedQueue.AddLast(entry);
                }
            }
        }
    }
    
    private void Evict()
    {
        // Evict from window if too large
        if (_windowCount > _windowSize && _windowQueue.First != null)
        {
            var victim = _windowQueue.First.Value;
            _windowQueue.RemoveFirst();
            _windowCount--;
            
            // Try to admit to main cache (probation)
            if (!TryAdmitToMain(victim))
            {
                _map.TryRemove(victim.Key, out _);
                NotifyRemoval(victim.Key, victim.Value, RemovalCause.Size);
                if (_recordStats)
                    Interlocked.Increment(ref _evictionCount);
            }
            return;
        }
        
        // Evict from probation
        if (_probationQueue.First != null)
        {
            var victim = _probationQueue.First.Value;
            _probationQueue.RemoveFirst();
            _probationCount--;
            
            _map.TryRemove(victim.Key, out _);
            NotifyRemoval(victim.Key, victim.Value, RemovalCause.Size);
            if (_recordStats)
                Interlocked.Increment(ref _evictionCount);
        }
    }
    
    private bool TryAdmitToMain(CacheEntry candidate)
    {
        // If main cache has space, admit immediately
        if (_probationCount + _protectedCount < _maximumSize - _windowSize)
        {
            candidate.Queue = QueueType.Probation;
            candidate.Node = _probationQueue.AddLast(candidate);
            _probationCount++;
            return true;
        }
        
        // Compare frequency with victim from probation
        if (_probationQueue.First != null)
        {
            var victim = _probationQueue.First.Value;
            int candidateFreq = _sketch.Frequency(candidate.Key);
            int victimFreq = _sketch.Frequency(victim.Key);
            
            // Admit if candidate is more frequent
            if (candidateFreq > victimFreq)
            {
                _probationQueue.RemoveFirst();
                _probationCount--;
                
                _map.TryRemove(victim.Key, out _);
                NotifyRemoval(victim.Key, victim.Value, RemovalCause.Size);
                if (_recordStats)
                    Interlocked.Increment(ref _evictionCount);
                
                candidate.Queue = QueueType.Probation;
                candidate.Node = _probationQueue.AddLast(candidate);
                _probationCount++;
                return true;
            }
        }
        
        return false;
    }
    
    public void Invalidate(K key)
    {
        ArgumentNullException.ThrowIfNull(key);
        
        if (_map.TryRemove(key, out var entry))
        {
            lock (_evictionLock)
            {
                RemoveFromQueue(entry);
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
            Invalidate(key);
        }
    }
    
    private void RemoveFromQueue(CacheEntry entry)
    {
        if (entry.Node != null)
        {
            switch (entry.Queue)
            {
                case QueueType.Window:
                    _windowQueue.Remove(entry.Node);
                    _windowCount--;
                    break;
                case QueueType.Probation:
                    _probationQueue.Remove(entry.Node);
                    _probationCount--;
                    break;
                case QueueType.Protected:
                    _protectedQueue.Remove(entry.Node);
                    _protectedCount--;
                    break;
            }
            entry.Node = null;
        }
    }
    
    public IReadOnlyDictionary<K, V> GetAllPresent(IEnumerable<K> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        
        var result = new Dictionary<K, V>();
        foreach (var key in keys)
        {
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
            var computed = mappingFunction(missingKeys);
            foreach (var kvp in computed)
            {
                Put(kvp.Key, kvp.Value);
                result[kvp.Key] = kvp.Value;
            }
        }
        
        return result;
    }
    
    public void PutAll(IDictionary<K, V> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        
        foreach (var kvp in map)
        {
            Put(kvp.Key, kvp.Value);
        }
    }
    
    private void NotifyRemoval(K key, V value, RemovalCause cause)
    {
        if (_removalListener != null)
        {
            try
            {
                _removalListener(key, value, cause);
            }
            catch
            {
                // Suppress listener exceptions
            }
        }
    }
    
    private sealed class EmptyPolicy<TK, TV> : IPolicy<TK, TV> where TK : notnull
    {
        // Policy methods not yet implemented
    }
    
    public long EstimatedSize() => _map.Count;
    
    public ICacheStats Stats()
    {
        if (!_recordStats)
            return CacheStats.Empty();
        
        long hits = Interlocked.Read(ref _hitCount);
        long misses = Interlocked.Read(ref _missCount);
        long evictions = Interlocked.Read(ref _evictionCount);
        
        return CacheStats.Of(hits, misses, 0, 0, 0, evictions, 0);
    }
    
    public void CleanUp()
    {
        // W-TinyLFU performs cleanup during operations
    }
    
    public ConcurrentDictionary<K, V> AsMap()
    {
        var result = new ConcurrentDictionary<K, V>(Environment.ProcessorCount, (int)EstimatedSize());
        foreach (var kvp in _map)
        {
            result.TryAdd(kvp.Key, kvp.Value.Value);
        }
        return result;
    }
    
    public IPolicy<K, V> Policy() => new EmptyPolicy<K, V>();
}

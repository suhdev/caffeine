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
/// A builder of <see cref="ICache{K, V}"/> instances having a combination of features such as:
/// <list type="bullet">
/// <item>Automatic loading of entries into the cache, optionally asynchronously</item>
/// <item>Size-based eviction when a maximum is exceeded based on frequency and recency</item>
/// <item>Time-based expiration of entries, measured since last access or last write</item>
/// <item>Asynchronously refresh when the first stale request for an entry occurs</item>
/// <item>Notification of evicted (or otherwise removed) entries</item>
/// <item>Accumulation of cache access statistics</item>
/// </list>
/// <para>
/// These features are all optional; caches can be created using all or none of them. 
/// By default, cache instances created by <see cref="Caffeine{K, V}"/> will not perform 
/// any type of eviction.
/// </para>
/// </summary>
/// <typeparam name="K">The most general key type this builder will be able to create caches for</typeparam>
/// <typeparam name="V">The most general value type this builder will be able to create caches for</typeparam>
public sealed class Caffeine<K, V> where K : notnull
{
    private const double DefaultLoadFactor = 0.75;
    private const int DefaultInitialCapacity = 16;
    private const int UnsetInt = -1;

    private long _maximumSize = UnsetInt;
    private long _maximumWeight = UnsetInt;
    private int _initialCapacity = UnsetInt;

    private long _expireAfterWriteNanos = UnsetInt;
    private long _expireAfterAccessNanos = UnsetInt;
    private long _refreshAfterWriteNanos = UnsetInt;

    private bool _recordStats = false;

    private Caffeine() { }

    /// <summary>
    /// Constructs a new <see cref="Caffeine{K, V}"/> instance with default settings, 
    /// including no automatic eviction of any kind.
    /// </summary>
    /// <returns>A new instance with default settings</returns>
    public static Caffeine<K, V> NewBuilder()
    {
        return new Caffeine<K, V>();
    }

    /// <summary>
    /// Sets the minimum total size for the internal data structures. Providing a large enough 
    /// estimate at construction time avoids the need for expensive resizing operations later, 
    /// but setting this value unnecessarily high wastes memory.
    /// </summary>
    /// <param name="initialCapacity">The initial capacity</param>
    /// <returns>This builder instance</returns>
    /// <exception cref="ArgumentException">If <paramref name="initialCapacity"/> is negative</exception>
    /// <exception cref="InvalidOperationException">If initial capacity was already set</exception>
    public Caffeine<K, V> InitialCapacity(int initialCapacity)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentException("Initial capacity must be non-negative", nameof(initialCapacity));
        }
        if (_initialCapacity != UnsetInt)
        {
            throw new InvalidOperationException($"Initial capacity was already set to {_initialCapacity}");
        }

        _initialCapacity = initialCapacity;
        return this;
    }

    /// <summary>
    /// Specifies the maximum number of entries the cache may contain. Note that the cache 
    /// <b>may evict an entry before this limit is exceeded</b>. As the cache size grows close 
    /// to the maximum, the cache evicts entries that are less likely to be used again. For 
    /// example, the cache may evict an entry because it hasn't been used recently or very often.
    /// <para>
    /// When <paramref name="maximumSize"/> is zero, elements will be evicted immediately after 
    /// being loaded into the cache. This can be useful in testing, or to disable caching temporarily.
    /// </para>
    /// </summary>
    /// <param name="maximumSize">The maximum size of the cache</param>
    /// <returns>This builder instance</returns>
    /// <exception cref="ArgumentException">If <paramref name="maximumSize"/> is negative</exception>
    /// <exception cref="InvalidOperationException">
    /// If maximum size or maximum weight was already set
    /// </exception>
    public Caffeine<K, V> MaximumSize(long maximumSize)
    {
        if (maximumSize < 0)
        {
            throw new ArgumentException("Maximum size must be non-negative", nameof(maximumSize));
        }
        if (_maximumSize != UnsetInt)
        {
            throw new InvalidOperationException($"Maximum size was already set to {_maximumSize}");
        }
        if (_maximumWeight != UnsetInt)
        {
            throw new InvalidOperationException($"Maximum weight was already set to {_maximumWeight}");
        }

        _maximumSize = maximumSize;
        return this;
    }

    /// <summary>
    /// Specifies that each entry should be automatically removed from the cache once a fixed 
    /// duration has elapsed after the entry's creation, or the most recent replacement of its value.
    /// <para>
    /// When <paramref name="duration"/> is zero, elements will be evicted immediately after being 
    /// loaded into the cache.
    /// </para>
    /// </summary>
    /// <param name="duration">The length of time after an entry is created that it should be automatically removed</param>
    /// <returns>This builder instance</returns>
    /// <exception cref="ArgumentException">If <paramref name="duration"/> is negative</exception>
    /// <exception cref="InvalidOperationException">
    /// If time-to-live or expiry was already set
    /// </exception>
    public Caffeine<K, V> ExpireAfterWrite(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentException("Duration must be non-negative", nameof(duration));
        }
        if (_expireAfterWriteNanos != UnsetInt)
        {
            throw new InvalidOperationException(
                $"ExpireAfterWrite was already set to {TimeSpan.FromTicks(_expireAfterWriteNanos / 100)}");
        }

        _expireAfterWriteNanos = duration.Ticks * 100; // Convert to nanoseconds
        return this;
    }

    /// <summary>
    /// Specifies that each entry should be automatically removed from the cache once a fixed 
    /// duration has elapsed after the entry's creation, the most recent replacement of its value, 
    /// or its last access.
    /// </summary>
    /// <param name="duration">The length of time after an entry is last accessed that it should be automatically removed</param>
    /// <returns>This builder instance</returns>
    /// <exception cref="ArgumentException">If <paramref name="duration"/> is negative</exception>
    /// <exception cref="InvalidOperationException">
    /// If time-to-idle or expiry was already set
    /// </exception>
    public Caffeine<K, V> ExpireAfterAccess(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentException("Duration must be non-negative", nameof(duration));
        }
        if (_expireAfterAccessNanos != UnsetInt)
        {
            throw new InvalidOperationException(
                $"ExpireAfterAccess was already set to {TimeSpan.FromTicks(_expireAfterAccessNanos / 100)}");
        }

        _expireAfterAccessNanos = duration.Ticks * 100; // Convert to nanoseconds
        return this;
    }

    /// <summary>
    /// Specifies that active entries are eligible for automatic refresh once a fixed duration 
    /// has elapsed after the entry's creation, or the most recent replacement of its value.
    /// </summary>
    /// <param name="duration">The length of time after an entry is created that it should be considered stale</param>
    /// <returns>This builder instance</returns>
    /// <exception cref="ArgumentException">If <paramref name="duration"/> is negative</exception>
    /// <exception cref="InvalidOperationException">
    /// If refresh was already set
    /// </exception>
    public Caffeine<K, V> RefreshAfterWrite(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentException("Duration must be non-negative", nameof(duration));
        }
        if (_refreshAfterWriteNanos != UnsetInt)
        {
            throw new InvalidOperationException(
                $"RefreshAfterWrite was already set to {TimeSpan.FromTicks(_refreshAfterWriteNanos / 100)}");
        }

        _refreshAfterWriteNanos = duration.Ticks * 100; // Convert to nanoseconds
        return this;
    }

    /// <summary>
    /// Enables the accumulation of <see cref="ICacheStats"/> during the operation of the cache. 
    /// Without this, <see cref="ICache{K, V}.Stats"/> will return zero for all statistics.
    /// </summary>
    /// <returns>This builder instance</returns>
    public Caffeine<K, V> RecordStats()
    {
        _recordStats = true;
        return this;
    }

    /// <summary>
    /// Builds a cache which does not automatically load values when keys are requested.
    /// <para>
    /// Consider using <see cref="Build(Func{K, V?})"/> instead if it is feasible to 
    /// implement a cache loader.
    /// </para>
    /// </summary>
    /// <returns>A cache having the requested features</returns>
    public ICache<K, V> Build()
    {
        return new SimpleConcurrentCache<K, V>(this);
    }

    /// <summary>
    /// Builds a cache which automatically loads values when keys are requested, using the 
    /// supplied <paramref name="loader"/>.
    /// </summary>
    /// <param name="loader">The cache loader used to obtain new values</param>
    /// <returns>A cache having the requested features</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="loader"/> is null</exception>
    public ICache<K, V> Build(Func<K, V?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new LoadingCache<K, V>(this, loader);
    }

    /// <summary>
    /// Builds a cache which automatically loads values when keys are requested, using the 
    /// supplied <paramref name="loader"/>.
    /// </summary>
    /// <param name="loader">The cache loader used to obtain new values</param>
    /// <returns>A cache having the requested features</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="loader"/> is null</exception>
    public ICache<K, V> Build(ICacheLoader<K, V> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new LoadingCache<K, V>(this, loader.Load);
    }

    internal int GetInitialCapacity()
    {
        return _initialCapacity == UnsetInt ? DefaultInitialCapacity : _initialCapacity;
    }

    internal long GetMaximumSize()
    {
        return _maximumSize;
    }

    internal bool ShouldRecordStats()
    {
        return _recordStats;
    }
}

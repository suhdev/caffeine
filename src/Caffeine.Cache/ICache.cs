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
/// A semi-persistent mapping from keys to values. Cache entries are manually added using
/// <see cref="Get{K}"/> or <see cref="Put"/>, and are stored in the cache until
/// either evicted or manually invalidated.
/// <para>
/// Implementations of this interface are expected to be thread-safe and can be safely accessed by
/// multiple concurrent threads.
/// </para>
/// </summary>
/// <typeparam name="K">The type of keys maintained by this cache</typeparam>
/// <typeparam name="V">The type of mapped values</typeparam>
public interface ICache<K, V> where K : notnull
{
    /// <summary>
    /// Returns the value associated with the <paramref name="key"/> in this cache, 
    /// or <c>null</c> if there is no cached value for the <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key whose associated value is to be returned</param>
    /// <returns>
    /// The value to which the specified key is mapped, or <c>null</c> if this cache 
    /// does not contain a mapping for the key
    /// </returns>
    /// <exception cref="ArgumentNullException">If the specified key is null</exception>
    V? GetIfPresent(K key);

    /// <summary>
    /// Returns the value associated with the <paramref name="key"/> in this cache, 
    /// obtaining that value from the <paramref name="mappingFunction"/> if necessary. 
    /// This method provides a simple substitute for the conventional 
    /// "if cached, return; otherwise create, cache and return" pattern.
    /// <para>
    /// If the specified key is not already associated with a value, attempts to compute 
    /// its value using the given mapping function and enters it into this cache unless 
    /// <c>null</c>. The entire method invocation is performed atomically, so the function 
    /// is applied at most once per key. Some attempted update operations on this cache by 
    /// other threads may be blocked while the computation is in progress, so the computation 
    /// should be short and simple, and must not attempt to update any other mappings of 
    /// this cache.
    /// </para>
    /// </summary>
    /// <param name="key">The key with which the specified value is to be associated</param>
    /// <param name="mappingFunction">The function to compute a value</param>
    /// <returns>
    /// The current (existing or computed) value associated with the specified key, 
    /// or null if the computed value is null
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified key or mappingFunction is null
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// If the computation detectably attempts a recursive update to this cache 
    /// that would otherwise never complete
    /// </exception>
    V? Get(K key, Func<K, V?> mappingFunction);

    /// <summary>
    /// Returns a map of the values associated with the <paramref name="keys"/> in this cache. 
    /// The returned map will only contain entries which are already present in the cache.
    /// <para>
    /// Note that duplicate elements in <paramref name="keys"/> will be ignored.
    /// </para>
    /// </summary>
    /// <param name="keys">The keys whose associated values are to be returned</param>
    /// <returns>
    /// An unmodifiable mapping of keys to values for the specified keys found in this cache
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified collection is null or contains a null element
    /// </exception>
    IReadOnlyDictionary<K, V> GetAllPresent(IEnumerable<K> keys);

    /// <summary>
    /// Returns a map of the values associated with the <paramref name="keys"/>, creating 
    /// or retrieving those values if necessary. The returned map contains entries that were 
    /// already cached, combined with the newly loaded entries; it will never contain null 
    /// keys or values.
    /// <para>
    /// A single request to the <paramref name="mappingFunction"/> is performed for all keys 
    /// which are not already present in the cache.
    /// </para>
    /// </summary>
    /// <param name="keys">The keys whose associated values are to be returned</param>
    /// <param name="mappingFunction">The function to compute the values</param>
    /// <returns>
    /// An unmodifiable mapping of keys to values for the specified keys in this cache
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified collection is null or contains a null element, or if the map 
    /// returned by the mappingFunction is null
    /// </exception>
    IReadOnlyDictionary<K, V> GetAll(
        IEnumerable<K> keys,
        Func<ISet<K>, IDictionary<K, V>> mappingFunction);

    /// <summary>
    /// Associates the <paramref name="value"/> with the <paramref name="key"/> in this cache. 
    /// If the cache previously contained a value associated with the <paramref name="key"/>, 
    /// the old value is replaced by the new <paramref name="value"/>.
    /// <para>
    /// Prefer <see cref="Get"/> when using the conventional "if cached, return; otherwise 
    /// create, cache and return" pattern.
    /// </para>
    /// </summary>
    /// <param name="key">The key with which the specified value is to be associated</param>
    /// <param name="value">Value to be associated with the specified key</param>
    /// <exception cref="ArgumentNullException">
    /// If the specified key or value is null
    /// </exception>
    void Put(K key, V value);

    /// <summary>
    /// Copies all of the mappings from the specified map to the cache. The effect of this 
    /// call is equivalent to that of calling <see cref="Put"/> on this map once for each 
    /// mapping from key to value in the specified map.
    /// </summary>
    /// <param name="map">The mappings to be stored in this cache</param>
    /// <exception cref="ArgumentNullException">
    /// If the specified map is null or the specified map contains null keys or values
    /// </exception>
    void PutAll(IDictionary<K, V> map);

    /// <summary>
    /// Discards any cached value for the <paramref name="key"/>. The behavior of this operation 
    /// is undefined for an entry that is being loaded (or reloaded) and is otherwise not present.
    /// </summary>
    /// <param name="key">The key whose mapping is to be removed from the cache</param>
    /// <exception cref="ArgumentNullException">If the specified key is null</exception>
    void Invalidate(K key);

    /// <summary>
    /// Discards any cached values for the <paramref name="keys"/>. The behavior of this operation 
    /// is undefined for an entry that is being loaded (or reloaded) and is otherwise not present.
    /// </summary>
    /// <param name="keys">The keys whose associated values are to be removed</param>
    /// <exception cref="ArgumentNullException">
    /// If the specified collection is null or contains a null element
    /// </exception>
    void InvalidateAll(IEnumerable<K> keys);

    /// <summary>
    /// Discards all entries in the cache. The behavior of this operation is undefined for 
    /// an entry that is being loaded (or reloaded) and is otherwise not present.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Returns the approximate number of entries in this cache. The value returned is an estimate; 
    /// the actual count may differ if there are concurrent insertions or removals, or if some 
    /// entries are pending removal due to expiration or weak/soft reference collection. In the 
    /// case of stale entries this inaccuracy can be mitigated by performing a <see cref="CleanUp"/> first.
    /// </summary>
    /// <returns>The estimated number of mappings</returns>
    long EstimatedSize();

    /// <summary>
    /// Returns a current snapshot of this cache's cumulative statistics. All statistics are
    /// initialized to zero and are monotonically increasing over the lifetime of the cache.
    /// <para>
    /// Due to the performance penalty of maintaining statistics, some implementations may not 
    /// record the usage history immediately or at all.
    /// </para>
    /// </summary>
    /// <returns>The current snapshot of the statistics of this cache</returns>
    ICacheStats Stats();

    /// <summary>
    /// Returns a view of the entries stored in this cache as a thread-safe map. 
    /// Modifications made to the map directly affect the cache.
    /// <para>
    /// Iterators from the returned map are at least <i>weakly consistent</i>: they are safe 
    /// for concurrent use, but if the cache is modified (including by eviction) after the 
    /// iterator is created, it is undefined which of the changes (if any) will be reflected 
    /// in that iterator.
    /// </para>
    /// </summary>
    /// <returns>
    /// A thread-safe view of this cache supporting all of the optional dictionary operations
    /// </returns>
    ConcurrentDictionary<K, V> AsMap();

    /// <summary>
    /// Performs any pending maintenance operations needed by the cache. Exactly which 
    /// activities are performed -- if any -- is implementation-dependent.
    /// </summary>
    void CleanUp();

    /// <summary>
    /// Returns access to inspect and perform low-level operations on this cache based on 
    /// its runtime characteristics. These operations are optional and dependent on how the 
    /// cache was constructed and what abilities the implementation exposes.
    /// </summary>
    /// <returns>
    /// Access to inspect and perform advanced operations based on the cache's characteristics
    /// </returns>
    IPolicy<K, V> Policy();
}

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
/// A semi-persistent mapping from keys to values with asynchronous operations. 
/// Async cache entries are manually added using <see cref="GetAsync"/> or <see cref="PutAsync"/>, 
/// and are stored in the cache until either evicted or manually invalidated.
/// <para>
/// Implementations of this interface are expected to be thread-safe and can be safely accessed by
/// multiple concurrent threads.
/// </para>
/// </summary>
/// <typeparam name="K">The type of keys maintained by this cache</typeparam>
/// <typeparam name="V">The type of mapped values</typeparam>
public interface IAsyncCache<K, V> where K : notnull
{
    /// <summary>
    /// Asynchronously returns the value associated with the <paramref name="key"/> in this cache, 
    /// or <c>null</c> if there is no cached value for the <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key whose associated value is to be returned</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing the value to which the specified key is mapped, or <c>null</c> 
    /// if this cache does not contain a mapping for the key
    /// </returns>
    /// <exception cref="ArgumentNullException">If the specified key is null</exception>
    Task<V?> GetIfPresentAsync(K key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously returns the value associated with the <paramref name="key"/> in this cache, 
    /// obtaining that value from the <paramref name="mappingFunction"/> if necessary.
    /// <para>
    /// If the specified key is not already associated with a value, attempts to compute 
    /// its value using the given mapping function and enters it into this cache unless 
    /// <c>null</c>.
    /// </para>
    /// </summary>
    /// <param name="key">The key with which the specified value is to be associated</param>
    /// <param name="mappingFunction">The async function to compute a value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing the current (existing or computed) value associated with the 
    /// specified key, or null if the computed value is null
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified key or mappingFunction is null
    /// </exception>
    Task<V?> GetAsync(K key, Func<K, CancellationToken, Task<V?>> mappingFunction, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously returns a map of the values associated with the <paramref name="keys"/> 
    /// in this cache. The returned map will only contain entries which are already present 
    /// in the cache.
    /// </summary>
    /// <param name="keys">The keys whose associated values are to be returned</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing an unmodifiable mapping of keys to values for the specified keys 
    /// found in this cache
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified collection is null or contains a null element
    /// </exception>
    Task<IReadOnlyDictionary<K, V>> GetAllPresentAsync(IEnumerable<K> keys, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously returns a map of the values associated with the <paramref name="keys"/>, 
    /// creating or retrieving those values if necessary.
    /// </summary>
    /// <param name="keys">The keys whose associated values are to be returned</param>
    /// <param name="mappingFunction">The async function to compute the values</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing an unmodifiable mapping of keys to values for the specified keys 
    /// in this cache
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified collection is null or contains a null element, or if the map 
    /// returned by the mappingFunction is null
    /// </exception>
    Task<IReadOnlyDictionary<K, V>> GetAllAsync(
        IEnumerable<K> keys,
        Func<ISet<K>, CancellationToken, Task<IDictionary<K, V>>> mappingFunction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously associates the <paramref name="value"/> with the <paramref name="key"/> 
    /// in this cache. If the cache previously contained a value associated with the 
    /// <paramref name="key"/>, the old value is replaced by the new <paramref name="value"/>.
    /// </summary>
    /// <param name="key">The key with which the specified value is to be associated</param>
    /// <param name="value">Value to be associated with the specified key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous put operation</returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified key or value is null
    /// </exception>
    Task PutAsync(K key, V value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously copies all of the mappings from the specified map to the cache.
    /// </summary>
    /// <param name="map">The mappings to be stored in this cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous put all operation</returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified map is null or contains null keys or values
    /// </exception>
    Task PutAllAsync(IDictionary<K, V> map, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously discards any cached value for the <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key whose mapping is to be removed from the cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous invalidate operation</returns>
    /// <exception cref="ArgumentNullException">If the specified key is null</exception>
    Task InvalidateAsync(K key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously discards any cached values for the <paramref name="keys"/>.
    /// </summary>
    /// <param name="keys">The keys whose associated values are to be removed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous invalidate operation</returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified collection is null or contains a null element
    /// </exception>
    Task InvalidateAllAsync(IEnumerable<K> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously discards all entries in the cache.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous invalidate all operation</returns>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the synchronous view of this async cache. All operations on the returned 
    /// cache will be synchronous, blocking until completion.
    /// </summary>
    /// <returns>A synchronous view of this cache</returns>
    ICache<K, V> Synchronous();

    /// <summary>
    /// Returns the approximate number of entries in this cache.
    /// </summary>
    /// <returns>The estimated number of mappings</returns>
    long EstimatedSize();

    /// <summary>
    /// Returns a current snapshot of this cache's cumulative statistics.
    /// </summary>
    /// <returns>The current snapshot of the statistics of this cache</returns>
    ICacheStats Stats();

    /// <summary>
    /// Returns a view of the entries stored in this cache as a thread-safe map.
    /// </summary>
    /// <returns>A thread-safe view of this cache</returns>
    ConcurrentDictionary<K, V> AsMap();

    /// <summary>
    /// Asynchronously performs any pending maintenance operations needed by the cache.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous cleanup operation</returns>
    Task CleanUpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns access to inspect and perform low-level operations on this cache based on 
    /// its runtime characteristics.
    /// </summary>
    /// <returns>
    /// Access to inspect and perform advanced operations based on the cache's characteristics
    /// </returns>
    IPolicy<K, V> Policy();
}

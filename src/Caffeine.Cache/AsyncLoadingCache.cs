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

namespace Caffeine.Cache;

/// <summary>
/// An async loading cache with automatic value loading capabilities.
/// </summary>
/// <typeparam name="K">The type of keys</typeparam>
/// <typeparam name="V">The type of values</typeparam>
public interface IAsyncLoadingCache<K, V> : IAsyncCache<K, V> where K : notnull
{
    /// <summary>
    /// Asynchronously returns the value associated with the <paramref name="key"/> in this cache,
    /// obtaining that value from the cache loader if necessary.
    /// <para>
    /// If the specified key is not already associated with a value, the cache loader is invoked 
    /// to compute the value and add it to the cache.
    /// </para>
    /// </summary>
    /// <param name="key">The key whose associated value is to be returned</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing the current (existing or computed) value associated with the specified key,
    /// or null if the computed value is null
    /// </returns>
    /// <exception cref="ArgumentNullException">If the specified key is null</exception>
    Task<V?> GetAsync(K key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously returns the values associated with the <paramref name="keys"/>,
    /// obtaining those values from the cache loader if necessary.
    /// <para>
    /// A single request to the cache loader is performed for all keys which are not already present 
    /// in the cache.
    /// </para>
    /// </summary>
    /// <param name="keys">The keys whose associated values are to be returned</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing an unmodifiable mapping of keys to values for the specified keys in this cache
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// If the specified collection is null or contains a null element
    /// </exception>
    Task<IReadOnlyDictionary<K, V>> GetAllAsync(IEnumerable<K> keys, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Internal async loading cache implementation.
/// </summary>
internal class AsyncLoadingCacheImpl<K, V> : AsyncCacheWrapper<K, V>, IAsyncLoadingCache<K, V> 
    where K : notnull
{
    private readonly ICacheLoader<K, V> _loader;

    public AsyncLoadingCacheImpl(ICache<K, V> cache, ICacheLoader<K, V> loader) 
        : base(cache)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public async Task<V?> GetAsync(K key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Use the synchronous cache's Get method which handles statistics properly
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Synchronous().Get(key, k =>
            {
                // Execute the async loader synchronously for the sync cache
                var task = _loader.AsyncLoad(k);
                return task.GetAwaiter().GetResult();
            });
        }, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<K, V>> GetAllAsync(IEnumerable<K> keys, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();

        // Use the synchronous cache's GetAll method which handles statistics properly
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keysList = keys.ToList();
            return Synchronous().GetAll(keysList, keysSet =>
            {
                // Execute the async loader synchronously for the sync cache
                var task = _loader.AsyncLoadAll(keysSet);
                return task.GetAwaiter().GetResult();
            });
        }, cancellationToken);
    }
}

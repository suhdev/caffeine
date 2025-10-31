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
/// Computes or retrieves values, based on a key, for use in populating a cache.
/// <para>
/// Most implementations will only need to implement <see cref="Load"/>.
/// </para>
/// </summary>
/// <typeparam name="K">The type of keys</typeparam>
/// <typeparam name="V">The type of values</typeparam>
public interface ICacheLoader<K, V> where K : notnull
{
    /// <summary>
    /// Computes or retrieves the value corresponding to <paramref name="key"/>.
    /// <para>
    /// <b>Warning:</b> loading <b>must not</b> attempt to update any mappings of this cache directly.
    /// </para>
    /// </summary>
    /// <param name="key">The non-null key whose value should be loaded</param>
    /// <returns>The value associated with <paramref name="key"/> or <c>null</c> if not found</returns>
    /// <exception cref="Exception">If loading fails</exception>
    V? Load(K key);

    /// <summary>
    /// Computes or retrieves the values corresponding to <paramref name="keys"/>.
    /// <para>
    /// If the returned map doesn't contain all requested <paramref name="keys"/>, then the entries 
    /// it does contain will be cached. If the returned map contains extra keys not present in 
    /// <paramref name="keys"/> then all returned entries will be cached, but only the entries for 
    /// <paramref name="keys"/> will be returned.
    /// </para>
    /// <para>
    /// This method should be overridden when bulk retrieval is significantly more efficient than 
    /// many individual lookups.
    /// </para>
    /// </summary>
    /// <param name="keys">The unique, non-null keys whose values should be loaded</param>
    /// <returns>A map from each key in <paramref name="keys"/> to the value associated with that key</returns>
    /// <exception cref="Exception">If loading fails</exception>
    IDictionary<K, V> LoadAll(ISet<K> keys)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Asynchronously computes or retrieves the value corresponding to <paramref name="key"/>.
    /// <para>
    /// <b>Warning:</b> loading <b>must not</b> attempt to update any mappings of this cache directly.
    /// </para>
    /// </summary>
    /// <param name="key">The non-null key whose value should be loaded</param>
    /// <returns>A task that represents the asynchronous load operation</returns>
    Task<V?> AsyncLoad(K key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.Run(() => Load(key));
    }

    /// <summary>
    /// Asynchronously computes or retrieves the values corresponding to <paramref name="keys"/>.
    /// <para>
    /// This method should be overridden when bulk retrieval is significantly more efficient than 
    /// many individual lookups.
    /// </para>
    /// </summary>
    /// <param name="keys">The unique, non-null keys whose values should be loaded</param>
    /// <returns>A task containing the map from each key to its value</returns>
    Task<IDictionary<K, V>> AsyncLoadAll(ISet<K> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Task.Run(() => LoadAll(keys));
    }
}

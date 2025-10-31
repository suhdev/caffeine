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
/// A simple async cache implementation that wraps a synchronous cache and provides 
/// asynchronous operations.
/// </summary>
internal class AsyncCacheWrapper<K, V> : IAsyncCache<K, V> where K : notnull
{
    private readonly ICache<K, V> _cache;

    public AsyncCacheWrapper(ICache<K, V> cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public Task<V?> GetIfPresentAsync(K key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _cache.GetIfPresent(key);
        }, cancellationToken);
    }

    public Task<V?> GetAsync(K key, Func<K, CancellationToken, Task<V?>> mappingFunction, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mappingFunction);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _cache.Get(key, k =>
            {
                // Execute the async mapping function synchronously
                var task = mappingFunction(k, cancellationToken);
                return task.GetAwaiter().GetResult();
            });
        }, cancellationToken);
    }

    public Task<IReadOnlyDictionary<K, V>> GetAllPresentAsync(IEnumerable<K> keys, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _cache.GetAllPresent(keys);
        }, cancellationToken);
    }

    public Task<IReadOnlyDictionary<K, V>> GetAllAsync(
        IEnumerable<K> keys,
        Func<ISet<K>, CancellationToken, Task<IDictionary<K, V>>> mappingFunction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(mappingFunction);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _cache.GetAll(keys, keysSet =>
            {
                // Execute the async mapping function synchronously
                var task = mappingFunction(keysSet, cancellationToken);
                return task.GetAwaiter().GetResult();
            });
        }, cancellationToken);
    }

    public Task PutAsync(K key, V value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Put(key, value);
        }, cancellationToken);
    }

    public Task PutAllAsync(IDictionary<K, V> map, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cache.PutAll(map);
        }, cancellationToken);
    }

    public Task InvalidateAsync(K key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Invalidate(key);
        }, cancellationToken);
    }

    public Task InvalidateAllAsync(IEnumerable<K> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cache.InvalidateAll(keys);
        }, cancellationToken);
    }

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cache.InvalidateAll();
        }, cancellationToken);
    }

    public Task CleanUpAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cache.CleanUp();
        }, cancellationToken);
    }

    public ICache<K, V> Synchronous() => _cache;

    public long EstimatedSize() => _cache.EstimatedSize();

    public ICacheStats Stats() => _cache.Stats();

    public ConcurrentDictionary<K, V> AsMap() => _cache.AsMap();

    public IPolicy<K, V> Policy() => _cache.Policy();
}

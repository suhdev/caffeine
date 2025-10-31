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

using Xunit;

namespace Caffeine.Cache.Tests;

public class AsyncLoadingCacheTests
{
    [Fact]
    public async Task GetAsync_LoadsValue_WhenKeyNotPresent()
    {
        var loadCount = 0;
        var cache = Caffeine<int, string>.NewBuilder()
            .BuildAsync(async (key, ct) =>
            {
                loadCount++;
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        var result = await cache.GetAsync(42);
        
        Assert.Equal("loaded-42", result);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task GetAsync_ReturnsCachedValue_WhenKeyPresent()
    {
        var loadCount = 0;
        var cache = Caffeine<int, string>.NewBuilder()
            .BuildAsync(async (key, ct) =>
            {
                loadCount++;
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        await cache.GetAsync(42);
        var result = await cache.GetAsync(42);
        
        Assert.Equal("loaded-42", result);
        Assert.Equal(1, loadCount); // Should only load once
    }

    [Fact]
    public async Task GetAsync_WithICacheLoader_LoadsValues()
    {
        var loader = new TestAsyncCacheLoader();
        var cache = Caffeine<int, string>.NewBuilder().BuildAsync(loader);
        
        var result = await cache.GetAsync(1);
        
        Assert.Equal("async-1", result);
        Assert.Equal(1, loader.LoadCount);
    }

    [Fact]
    public async Task GetAllAsync_LoadsMissingValues()
    {
        var loadCount = 0;
        var cache = Caffeine<int, string>.NewBuilder()
            .BuildAsync(async (key, ct) =>
            {
                loadCount++;
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        await cache.PutAsync(1, "existing-1");
        
        var result = await cache.GetAllAsync(new[] { 1, 2, 3 });
        
        Assert.Equal(3, result.Count);
        Assert.Equal("existing-1", result[1]);
        Assert.Equal("loaded-2", result[2]);
        Assert.Equal("loaded-3", result[3]);
        Assert.Equal(2, loadCount); // Only loaded keys 2 and 3
    }

    [Fact]
    public async Task GetAllAsync_WithICacheLoader_UsesBulkLoad()
    {
        var loader = new TestAsyncCacheLoader();
        var cache = Caffeine<int, string>.NewBuilder().BuildAsync(loader);
        
        await cache.PutAsync(1, "existing-1");
        var result = await cache.GetAllAsync(new[] { 1, 2, 3 });
        
        Assert.Equal(3, result.Count);
        Assert.Equal("existing-1", result[1]);
        Assert.Equal("async-2", result[2]);
        Assert.Equal("async-3", result[3]);
        Assert.Equal(1, loader.LoadAllCount); // Bulk load called once
    }

    [Fact]
    public async Task GetAsync_TracksStatistics()
    {
        var cache = Caffeine<int, string>.NewBuilder()
            .RecordStats()
            .BuildAsync(async (key, ct) =>
            {
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        await cache.GetAsync(1); // miss + load
        await cache.GetAsync(1); // hit
        await cache.GetAsync(2); // miss + load
        
        var stats = cache.Stats();
        Assert.Equal(1, stats.HitCount());
        Assert.Equal(2, stats.MissCount());
        Assert.Equal(2, stats.LoadSuccessCount());
    }

    [Fact]
    public async Task AsyncLoadingCache_WorksWithExpiration()
    {
        var loadCount = 0;
        var cache = Caffeine<int, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(50))
            .BuildAsync(async (key, ct) =>
            {
                loadCount++;
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        await cache.GetAsync(1);
        Assert.Equal(1, loadCount);
        
        await Task.Delay(100);
        await cache.CleanUpAsync();
        
        await cache.GetAsync(1); // Should reload
        Assert.Equal(2, loadCount);
    }

    [Fact]
    public async Task AsyncLoadingCache_WorksWithSizeLimit()
    {
        var cache = Caffeine<int, string>.NewBuilder()
            .MaximumSize(2)
            .BuildAsync(async (key, ct) =>
            {
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        await cache.GetAsync(1);
        await cache.GetAsync(2);
        await cache.GetAsync(3);
        
        // Key 1 should have been evicted (LRU)
        Assert.Null(await cache.GetIfPresentAsync(1));
        Assert.Equal(2, cache.EstimatedSize());
    }

    [Fact]
    public async Task AsyncLoadingCache_WithCombinedEviction()
    {
        var cache = Caffeine<int, string>.NewBuilder()
            .MaximumSize(3)
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(50))
            .BuildAsync(async (key, ct) =>
            {
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        await cache.GetAsync(1);
        await cache.GetAsync(2);
        
        await Task.Delay(100);
        await cache.CleanUpAsync();
        
        // Should be expired
        Assert.Equal(0, cache.EstimatedSize());
    }

    [Fact]
    public async Task GetAsync_PropagatesLoaderException()
    {
        var cache = Caffeine<int, string>.NewBuilder()
            .BuildAsync(async (key, ct) =>
            {
                await Task.Delay(10, ct);
                throw new InvalidOperationException("Load failed");
            });
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(1));
    }

    [Fact]
    public async Task GetAsync_SupportsCancellation()
    {
        var tcs = new TaskCompletionSource<string>();
        var cache = Caffeine<int, string>.NewBuilder()
            .BuildAsync(async (key, ct) =>
            {
                // Wait for cancellation or explicit completion
                using var registration = ct.Register(() => tcs.TrySetCanceled());
                return await tcs.Task;
            });
        
        var cts = new CancellationTokenSource();
        var task = cache.GetAsync(1, cts.Token);
        
        cts.Cancel();
        
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Synchronous_AllowsMixedAsyncSyncAccess()
    {
        var cache = Caffeine<int, string>.NewBuilder()
            .BuildAsync(async (key, ct) =>
            {
                await Task.Delay(10, ct);
                return $"loaded-{key}";
            });
        
        await cache.GetAsync(1);
        
        var syncCache = cache.Synchronous();
        var result = syncCache.GetIfPresent(1);
        
        Assert.Equal("loaded-1", result);
    }
}

/// <summary>
/// Test cache loader with async capabilities
/// </summary>
internal class TestAsyncCacheLoader : ICacheLoader<int, string>
{
    public int LoadCount { get; private set; }
    public int LoadAllCount { get; private set; }

    public string? Load(int key)
    {
        LoadCount++;
        return $"sync-{key}";
    }

    public async Task<string?> AsyncLoad(int key)
    {
        LoadCount++;
        await Task.Delay(10);
        return $"async-{key}";
    }

    public async Task<IDictionary<int, string>> AsyncLoadAll(ISet<int> keys)
    {
        LoadAllCount++;
        await Task.Delay(10);
        return keys.ToDictionary(k => k, k => $"async-{k}");
    }
}

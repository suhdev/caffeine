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

public class AsyncCacheTests
{
    [Fact]
    public async Task GetIfPresentAsync_ReturnsNull_WhenKeyNotPresent()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        
        var result = await cache.GetIfPresentAsync("missing");
        
        Assert.Null(result);
    }

    [Fact]
    public async Task PutAsync_And_GetIfPresentAsync_StoresAndRetrievesValue()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        
        await cache.PutAsync("key1", "value1");
        var result = await cache.GetIfPresentAsync("key1");
        
        Assert.Equal("value1", result);
    }

    [Fact]
    public async Task GetAsync_ComputesValue_WhenKeyNotPresent()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        var computed = false;
        
        var result = await cache.GetAsync("key1", async (k, ct) =>
        {
            computed = true;
            await Task.Delay(10, ct);
            return $"computed-{k}";
        });
        
        Assert.True(computed);
        Assert.Equal("computed-key1", result);
        
        // Verify it's now cached
        var cached = await cache.GetIfPresentAsync("key1");
        Assert.Equal("computed-key1", cached);
    }

    [Fact]
    public async Task GetAsync_ReturnsExistingValue_WhenKeyPresent()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        await cache.PutAsync("key1", "existing");
        var computed = false;
        
        var result = await cache.GetAsync("key1", async (k, ct) =>
        {
            computed = true;
            await Task.Delay(10, ct);
            return "computed";
        });
        
        Assert.False(computed);
        Assert.Equal("existing", result);
    }

    [Fact]
    public async Task GetAllPresentAsync_ReturnsOnlyPresentKeys()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        await cache.PutAsync("key1", "value1");
        await cache.PutAsync("key2", "value2");
        
        var result = await cache.GetAllPresentAsync(new[] { "key1", "key2", "key3" });
        
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.False(result.ContainsKey("key3"));
    }

    [Fact]
    public async Task PutAllAsync_StoresMultipleValues()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        var map = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2",
            ["key3"] = "value3"
        };
        
        await cache.PutAllAsync(map);
        
        var result = await cache.GetAllPresentAsync(new[] { "key1", "key2", "key3" });
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesValue()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        await cache.PutAsync("key1", "value1");
        
        await cache.InvalidateAsync("key1");
        
        var result = await cache.GetIfPresentAsync("key1");
        Assert.Null(result);
    }

    [Fact]
    public async Task InvalidateAllAsync_WithKeys_RemovesSpecifiedValues()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        await cache.PutAsync("key1", "value1");
        await cache.PutAsync("key2", "value2");
        await cache.PutAsync("key3", "value3");
        
        await cache.InvalidateAllAsync(new[] { "key1", "key3" });
        
        Assert.Null(await cache.GetIfPresentAsync("key1"));
        Assert.Equal("value2", await cache.GetIfPresentAsync("key2"));
        Assert.Null(await cache.GetIfPresentAsync("key3"));
    }

    [Fact]
    public async Task InvalidateAllAsync_RemovesAllValues()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        await cache.PutAsync("key1", "value1");
        await cache.PutAsync("key2", "value2");
        
        await cache.InvalidateAllAsync();
        
        Assert.Equal(0, cache.EstimatedSize());
    }

    [Fact]
    public async Task EstimatedSize_ReturnsCorrectCount()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        
        Assert.Equal(0, cache.EstimatedSize());
        
        await cache.PutAsync("key1", "value1");
        await cache.PutAsync("key2", "value2");
        
        Assert.Equal(2, cache.EstimatedSize());
    }

    [Fact]
    public async Task Stats_TracksAsyncOperations()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .RecordStats()
            .BuildAsync();
        
        await cache.PutAsync("key1", "value1");
        await cache.GetIfPresentAsync("key1"); // hit
        await cache.GetIfPresentAsync("key2"); // miss
        
        var stats = cache.Stats();
        Assert.Equal(1, stats.HitCount());
        Assert.Equal(1, stats.MissCount());
    }

    [Fact]
    public async Task Synchronous_ReturnsWrappedCache()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        await cache.PutAsync("key1", "value1");
        
        var syncCache = cache.Synchronous();
        
        Assert.Equal("value1", syncCache.GetIfPresent("key1"));
    }

    [Fact]
    public async Task AsMap_ReturnsUnderlyingDictionary()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        await cache.PutAsync("key1", "value1");
        
        var map = cache.AsMap();
        
        Assert.Single(map);
        Assert.Equal("value1", map["key1"]);
    }

    [Fact]
    public async Task CleanUpAsync_PerformsMaintenanceWithExpiration()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(50))
            .BuildAsync();
        
        await cache.PutAsync("key1", "value1");
        await Task.Delay(100);
        await cache.CleanUpAsync();
        
        Assert.Equal(0, cache.EstimatedSize());
    }

    [Fact]
    public async Task AsyncCache_WorksWithSizeLimits()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(2)
            .BuildAsync();
        
        await cache.PutAsync("key1", "value1");
        await cache.PutAsync("key2", "value2");
        await cache.PutAsync("key3", "value3");
        
        // key1 should have been evicted (LRU)
        Assert.Null(await cache.GetIfPresentAsync("key1"));
        Assert.Equal(2, cache.EstimatedSize());
    }

    [Fact]
    public async Task GetAsync_SupportsCancellation()
    {
        var cache = Caffeine<string, string>.NewBuilder().BuildAsync();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cache.GetAsync("key1", async (k, ct) =>
            {
                await Task.Delay(100, ct);
                return "value";
            }, cts.Token));
    }
}

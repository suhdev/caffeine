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

namespace Caffeine.Cache.Tests;

public class CacheTests
{
    [Fact]
    public void SimpleCacheBasicOperations()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .InitialCapacity(10)
            .Build();

        // Test Put and GetIfPresent
        cache.Put("one", "1");
        Assert.Equal("1", cache.GetIfPresent("one"));
        Assert.Null(cache.GetIfPresent("two"));

        // Test Get with mapping function
        var value = cache.Get("two", key => "2");
        Assert.Equal("2", value);
        Assert.Equal("2", cache.GetIfPresent("two"));
    }

    [Fact]
    public void LoadingCacheBasicOperations()
    {
        int loadCount = 0;
        var cache = Caffeine<string, int?>.NewBuilder()
            .InitialCapacity(10)
            .Build(key =>
            {
                loadCount++;
                return key.Length;
            });

        // First access should load using the cache loader, not the override function
        var value1 = cache.GetIfPresent("test");
        Assert.Null(value1); // Not loaded yet

        // Now load it
        value1 = cache.Get("test", _ => 999); // Should use the mapping function provided here
        Assert.Equal(999, value1);
        Assert.Equal(0, loadCount); // Cache loader not called when explicit mapping function provided

        // Add one using the cache's Put
        cache.Put("hello", 5);
        var value2 = cache.GetIfPresent("hello");
        Assert.Equal(5, value2);
    }

    [Fact]
    public void CacheWithStatistics()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .RecordStats()
            .Build();

        // Generate some hits and misses
        cache.Put("key1", "value1");
        
        var hit = cache.GetIfPresent("key1");
        var miss = cache.GetIfPresent("key2");

        var stats = cache.Stats();
        
        Assert.Equal(1, stats.HitCount());
        Assert.Equal(1, stats.MissCount());
        Assert.Equal(2, stats.RequestCount());
        Assert.Equal(0.5, stats.HitRate());
        Assert.Equal(0.5, stats.MissRate());
    }

    [Fact]
    public void CacheInvalidation()
    {
        var cache = Caffeine<string, string>.NewBuilder().Build();

        cache.Put("key1", "1");
        cache.Put("key2", "2");
        cache.Put("key3", "3");

        Assert.Equal(3, cache.EstimatedSize());

        // Invalidate single key
        cache.Invalidate("key1");
        Assert.Null(cache.GetIfPresent("key1"));
        Assert.Equal(2, cache.EstimatedSize());

        // Invalidate multiple keys
        cache.InvalidateAll(new[] { "key2", "key3" });
        Assert.Equal(0, cache.EstimatedSize());
    }

    [Fact]
    public void CachePutAll()
    {
        var cache = Caffeine<string, int>.NewBuilder().Build();

        var data = new Dictionary<string, int>
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3
        };

        cache.PutAll(data);

        Assert.Equal(3, cache.EstimatedSize());
        Assert.Equal(1, cache.GetIfPresent("one"));
        Assert.Equal(2, cache.GetIfPresent("two"));
        Assert.Equal(3, cache.GetIfPresent("three"));
    }

    [Fact]
    public void CacheGetAllPresent()
    {
        var cache = Caffeine<string, int>.NewBuilder().Build();

        cache.Put("one", 1);
        cache.Put("two", 2);
        cache.Put("three", 3);

        var result = cache.GetAllPresent(new[] { "one", "three", "five" });

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["one"]);
        Assert.Equal(3, result["three"]);
        Assert.False(result.ContainsKey("five"));
    }

    [Fact]
    public void CacheAsMap()
    {
        var cache = Caffeine<string, int>.NewBuilder().Build();

        cache.Put("key1", 1);
        cache.Put("key2", 2);

        var map = cache.AsMap();

        Assert.Equal(2, map.Count);
        Assert.True(map.ContainsKey("key1"));
        Assert.True(map.ContainsKey("key2"));

        // Modifications to the map affect the cache
        map["key3"] = 3;
        Assert.Equal(3, cache.GetIfPresent("key3"));
    }

    [Fact]
    public void NullKeyThrowsException()
    {
        var cache = Caffeine<string, int>.NewBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => cache.Put(null!, 1));
        Assert.Throws<ArgumentNullException>(() => cache.GetIfPresent(null!));
    }

    [Fact]
    public void InitialCapacityValidation()
    {
        Assert.Throws<ArgumentException>(() =>
            Caffeine<string, int>.NewBuilder().InitialCapacity(-1));

        var builder = Caffeine<string, int>.NewBuilder().InitialCapacity(100);
        Assert.Throws<InvalidOperationException>(() => builder.InitialCapacity(200));
    }

    [Fact]
    public void MaximumSizeValidation()
    {
        Assert.Throws<ArgumentException>(() =>
            Caffeine<string, int>.NewBuilder().MaximumSize(-1));

        var builder = Caffeine<string, int>.NewBuilder().MaximumSize(100);
        Assert.Throws<InvalidOperationException>(() => builder.MaximumSize(200));
    }
}

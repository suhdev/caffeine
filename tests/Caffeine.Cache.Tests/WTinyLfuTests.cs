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

public class WTinyLfuTests
{
    [Fact]
    public void WTinyLfu_BasicOperations()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(100)
            .UseWTinyLfu()
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        
        var stats = cache.Stats();
        Assert.Equal(1, stats.HitCount());
    }

    [Fact]
    public void WTinyLfu_FrequencyBasedAdmission()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .UseWTinyLfu()
            .RecordStats()
            .Build();

        // Fill the cache
        cache.Put("A", "valueA");
        cache.Put("B", "valueB");
        cache.Put("C", "valueC");
        
        // Access A and B multiple times to increase their frequency
        for (int i = 0; i < 10; i++)
        {
            cache.GetIfPresent("A");
            cache.GetIfPresent("B");
        }
        
        // Access C only once
        cache.GetIfPresent("C");
        
        // Add a new entry D - should evict C (least frequently used)
        cache.Put("D", "valueD");
        
        // A and B should still be present (higher frequency)
        Assert.Equal("valueA", cache.GetIfPresent("A"));
        Assert.Equal("valueB", cache.GetIfPresent("B"));
        Assert.Equal("valueD", cache.GetIfPresent("D"));
        
        // C should be evicted (lowest frequency)
        Assert.Null(cache.GetIfPresent("C"));
    }

    [Fact]
    public void WTinyLfu_WindowEviction()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(100)
            .UseWTinyLfu()
            .RecordStats()
            .Build();

        // Add many entries to test window eviction
        for (int i = 0; i < 150; i++)
        {
            cache.Put($"key{i}", $"value{i}");
        }

        Assert.True(cache.EstimatedSize() <= 100);
        
        var stats = cache.Stats();
        Assert.True(stats.EvictionCount() > 0);
    }

    [Fact]
    public void WTinyLfu_LRUInProtectedQueue()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .Build();

        // Fill cache
        for (int i = 0; i < 10; i++)
        {
            cache.Put($"key{i}", $"value{i}");
            cache.GetIfPresent($"key{i}"); // Access to promote to protected
        }
        
        // Access key0-key7, leaving key8 and key9 as LRU
        for (int i = 0; i < 8; i++)
        {
            cache.GetIfPresent($"key{i}");
        }
        
        // Add two new entries
        cache.Put("new1", "newvalue1");
        cache.Put("new2", "newvalue2");
        
        // key8 and key9 should be evicted (LRU in protected queue)
        Assert.Null(cache.GetIfPresent("key8"));
        Assert.Null(cache.GetIfPresent("key9"));
        
        // Others should still be present
        Assert.Equal("value0", cache.GetIfPresent("key0"));
        Assert.Equal("newvalue1", cache.GetIfPresent("new1"));
    }

    [Fact]
    public void WTinyLfu_StatsTracking()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(5)
            .UseWTinyLfu()
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        
        cache.GetIfPresent("key1"); // Hit
        cache.GetIfPresent("missing"); // Miss
        
        var stats = cache.Stats();
        Assert.Equal(1, stats.HitCount());
        Assert.Equal(1, stats.MissCount());
        Assert.Equal(0.5, stats.HitRate());
    }

    [Fact]
    public void WTinyLfu_InvalidateOperations()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");
        
        cache.Invalidate("key2");
        Assert.Null(cache.GetIfPresent("key2"));
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        
        cache.InvalidateAll(new[] { "key1", "key3" });
        Assert.Null(cache.GetIfPresent("key1"));
        Assert.Null(cache.GetIfPresent("key3"));
    }

    [Fact]
    public void WTinyLfu_PutReplacement()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key1", "value2"); // Replace
        
        Assert.Equal("value2", cache.GetIfPresent("key1"));
    }

    [Fact]
    public void WTinyLfu_GetWithMapping()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .RecordStats()
            .Build();

        var value = cache.Get("key1", k => $"computed_{k}");
        Assert.Equal("computed_key1", value);
        
        // Should be cached now
        var cachedValue = cache.GetIfPresent("key1");
        Assert.Equal("computed_key1", cachedValue);
        
        var stats = cache.Stats();
        Assert.Equal(1, stats.HitCount());
        Assert.Equal(1, stats.MissCount());
    }

    [Fact]
    public void WTinyLfu_BulkOperations()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .Build();

        var map = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" },
            { "key3", "value3" }
        };
        
        cache.PutAll(map);
        
        var result = cache.GetAllPresent(new[] { "key1", "key2", "missing" });
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
    }

    [Fact]
    public void WTinyLfu_RemovalListener()
    {
        var removals = new List<(string Key, string Value, RemovalCause Cause)>();
        
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .UseWTinyLfu()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key1", "value2"); // Replacement
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");
        cache.Put("key4", "value4"); // Should trigger eviction
        
        // Should have at least one replacement and one size eviction
        Assert.Contains(removals, r => r.Cause == RemovalCause.Replaced);
        Assert.Contains(removals, r => r.Cause == RemovalCause.Size);
    }

    [Fact]
    public void WTinyLfu_LoadingCache()
    {
        int loadCount = 0;
        
        var cache = Caffeine<int, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .RecordStats()
            .Build(key => 
            {
                loadCount++;
                return $"loaded_{key}";
            });

        var value1 = cache.GetIfPresent(1);
        Assert.Null(value1); // Not loaded automatically with GetIfPresent
        
        // Manually load
        cache.Put(1, "loaded_1");
        
        var value2 = cache.GetIfPresent(1);
        Assert.Equal("loaded_1", value2);
        
        var stats = cache.Stats();
        Assert.Equal(1, stats.HitCount());
    }

    [Fact]
    public void WTinyLfu_AsMapView()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        
        var map = cache.AsMap();
        Assert.Equal(2, map.Count);
        Assert.Equal("value1", map["key1"]);
        Assert.Equal("value2", map["key2"]);
    }

    [Fact]
    public void WTinyLfu_GetAllWithMappingFunction()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(10)
            .UseWTinyLfu()
            .Build();

        cache.Put("key1", "value1");
        
        var result = cache.GetAll(
            new[] { "key1", "key2", "key3" },
            missingKeys =>
            {
                var dict = new Dictionary<string, string>();
                foreach (var key in missingKeys)
                {
                    dict[key] = $"computed_{key}";
                }
                return dict;
            });

        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]); // From cache
        Assert.Equal("computed_key2", result["key2"]); // Computed
        Assert.Equal("computed_key3", result["key3"]); // Computed
        
        // Should now be cached
        Assert.Equal("computed_key2", cache.GetIfPresent("key2"));
    }

    [Fact]
    public void WTinyLfu_ZeroSize()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(0)
            .UseWTinyLfu()
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        
        // Should be immediately evicted
        Assert.Null(cache.GetIfPresent("key1"));
        Assert.True(cache.Stats().EvictionCount() > 0);
    }

    [Fact]
    public void WTinyLfu_HighFrequencyAccess()
    {
        var cache = Caffeine<int, string>.NewBuilder()
            .MaximumSize(100)
            .UseWTinyLfu()
            .RecordStats()
            .Build();

        // Add 100 entries
        for (int i = 0; i < 100; i++)
        {
            cache.Put(i, $"value{i}");
        }

        // Access first 50 entries frequently
        for (int iteration = 0; iteration < 100; iteration++)
        {
            for (int i = 0; i < 50; i++)
            {
                cache.GetIfPresent(i);
            }
        }

        // Add 100 more entries (should evict mostly from second half)
        for (int i = 100; i < 200; i++)
        {
            cache.Put(i, $"value{i}");
        }

        // First 50 should mostly still be present (high frequency)
        int presentCount = 0;
        for (int i = 0; i < 50; i++)
        {
            if (cache.GetIfPresent(i) != null)
            {
                presentCount++;
            }
        }
        
        // Should have retained most of the frequently accessed entries
        Assert.True(presentCount > 40);
        
        var stats = cache.Stats();
        Assert.True(stats.EvictionCount() >= 100);
    }
}

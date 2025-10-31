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

public class SizeBasedEvictionTests
{
    [Fact]
    public void MaximumSize_EvictsOldestEntries()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .RecordStats()
            .Build();

        // Add 3 entries - should fit within limit
        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        Assert.Equal(3, cache.EstimatedSize());
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        Assert.Equal("value2", cache.GetIfPresent("key2"));
        Assert.Equal("value3", cache.GetIfPresent("key3"));

        // Add 4th entry - should evict the oldest (key1)
        cache.Put("key4", "value4");

        Assert.Equal(3, cache.EstimatedSize());
        Assert.Null(cache.GetIfPresent("key1")); // Evicted
        Assert.Equal("value2", cache.GetIfPresent("key2"));
        Assert.Equal("value3", cache.GetIfPresent("key3"));
        Assert.Equal("value4", cache.GetIfPresent("key4"));

        // Check eviction count
        var stats = cache.Stats();
        Assert.Equal(1, stats.EvictionCount());
    }

    [Fact]
    public void MaximumSize_LRUEvictionPolicy()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        // Access key1 to make it most recently used
        var _ = cache.GetIfPresent("key1");

        // Add key4 - should evict key2 (least recently used)
        cache.Put("key4", "value4");

        Assert.Equal(3, cache.EstimatedSize());
        Assert.Equal("value1", cache.GetIfPresent("key1")); // Still present
        Assert.Null(cache.GetIfPresent("key2")); // Evicted (LRU)
        Assert.Equal("value3", cache.GetIfPresent("key3"));
        Assert.Equal("value4", cache.GetIfPresent("key4"));
    }

    [Fact]
    public void MaximumSize_WithLoadingCache()
    {
        int loadCount = 0;
        var cache = Caffeine<int, string>.NewBuilder()
            .MaximumSize(2)
            .RecordStats()
            .Build(key =>
            {
                loadCount++;
                return $"value{key}";
            });

        // Manually put 2 entries
        cache.Put(1, "value1");
        cache.Put(2, "value2");

        Assert.Equal(2, cache.EstimatedSize());

        // Put 3rd entry - should evict first
        cache.Put(3, "value3");

        Assert.Equal(2, cache.EstimatedSize());
        Assert.Null(cache.GetIfPresent(1)); // Evicted
        Assert.Equal("value2", cache.GetIfPresent(2));
        Assert.Equal("value3", cache.GetIfPresent(3));

        var stats = cache.Stats();
        Assert.Equal(1, stats.EvictionCount());
    }

    [Fact]
    public void MaximumSize_ZeroImmediateEviction()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(0)
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");

        // Entry should be immediately evicted
        Assert.Equal(0, cache.EstimatedSize());
        Assert.Null(cache.GetIfPresent("key1"));

        var stats = cache.Stats();
        Assert.True(stats.EvictionCount() > 0);
    }

    [Fact]
    public void MaximumSize_PutAllRespectsLimit()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .RecordStats()
            .Build();

        var data = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2",
            ["key3"] = "value3",
            ["key4"] = "value4",
            ["key5"] = "value5"
        };

        cache.PutAll(data);

        // Should only keep the last 3 entries
        Assert.Equal(3, cache.EstimatedSize());

        var stats = cache.Stats();
        Assert.Equal(2, stats.EvictionCount()); // 2 entries evicted
    }

    [Fact]
    public void MaximumSize_UpdateExistingEntry()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        // Update existing entry - should not trigger eviction
        cache.Put("key2", "value2-updated");

        Assert.Equal(3, cache.EstimatedSize());
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        Assert.Equal("value2-updated", cache.GetIfPresent("key2"));
        Assert.Equal("value3", cache.GetIfPresent("key3"));
    }

    [Fact]
    public void MaximumSize_GetWithMappingFunction()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(2)
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");

        // Get with mapping function for new key - should evict key1
        var value = cache.Get("key3", k => "value3");

        Assert.Equal(2, cache.EstimatedSize());
        Assert.Null(cache.GetIfPresent("key1")); // Evicted
        Assert.Equal("value2", cache.GetIfPresent("key2"));
        Assert.Equal("value3", cache.GetIfPresent("key3"));
    }

    [Fact]
    public void MaximumSize_GetAllRespectsLimit()
    {
        var cache = Caffeine<int, string>.NewBuilder()
            .MaximumSize(3)
            .Build();

        cache.Put(1, "one");

        // GetAll with mapping function for multiple new keys
        var result = cache.GetAll(new[] { 2, 3, 4, 5 }, keys =>
        {
            var dict = new Dictionary<int, string>();
            foreach (var key in keys)
            {
                dict[key] = $"value{key}";
            }
            return dict;
        });

        // Cache should only contain 3 entries (last 3)
        Assert.Equal(3, cache.EstimatedSize());
    }

    [Fact]
    public void MaximumSize_AsMapReflectsEviction()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(2)
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3"); // Evicts key1

        var map = cache.AsMap();

        Assert.Equal(2, map.Count);
        Assert.False(map.ContainsKey("key1"));
        Assert.True(map.ContainsKey("key2"));
        Assert.True(map.ContainsKey("key3"));
    }

    [Fact]
    public void MaximumSize_InvalidateDoesNotAffectCount()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        // Invalidate one entry
        cache.Invalidate("key2");

        Assert.Equal(2, cache.EstimatedSize());

        // Add new entry - should not trigger eviction since we have room
        cache.Put("key4", "value4");

        Assert.Equal(3, cache.EstimatedSize());

        var stats = cache.Stats();
        Assert.Equal(0, stats.EvictionCount()); // No evictions due to size limit
    }
}

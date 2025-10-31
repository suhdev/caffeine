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

public class CombinedEvictionTests
{
    [Fact]
    public void CombinedEviction_BothSizeAndTimeBasedEviction()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(200))
            .RecordStats()
            .Build();

        // Add 3 entries - should fit within size limit
        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        Assert.Equal(3, cache.EstimatedSize());

        // Add 4th entry - should evict oldest by LRU (key1)
        cache.Put("key4", "value4");

        Assert.Equal(3, cache.EstimatedSize());
        Assert.Null(cache.GetIfPresent("key1")); // Evicted by size

        // Wait for expiration
        Thread.Sleep(250);

        // All remaining entries should be expired
        Assert.Null(cache.GetIfPresent("key2"));
        Assert.Null(cache.GetIfPresent("key3"));
        Assert.Null(cache.GetIfPresent("key4"));

        var stats = cache.Stats();
        Assert.True(stats.EvictionCount() >= 4); // At least 4 evictions (1 size + 3 time)
    }

    [Fact]
    public void CombinedEviction_SizeEvictionTakesPrecedence()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(2)
            .ExpireAfterWrite(TimeSpan.FromSeconds(10)) // Long expiration
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3"); // Should evict key1 immediately

        // Size limit enforced immediately, not waiting for expiration
        Assert.Equal(2, cache.EstimatedSize());
        Assert.Null(cache.GetIfPresent("key1")); // Evicted by size
        Assert.Equal("value2", cache.GetIfPresent("key2"));
        Assert.Equal("value3", cache.GetIfPresent("key3"));
    }

    [Fact]
    public void CombinedEviction_TimeEvictionWithinSizeLimit()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(5)
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");

        Assert.Equal(2, cache.EstimatedSize());

        // Wait for expiration
        Thread.Sleep(150);

        // Entries should be expired even though we're under size limit
        Assert.Null(cache.GetIfPresent("key1"));
        Assert.Null(cache.GetIfPresent("key2"));

        var stats = cache.Stats();
        Assert.Equal(2, stats.EvictionCount());
    }

    [Fact]
    public void CombinedEviction_LRUWithAccessBasedExpiration()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .ExpireAfterAccess(TimeSpan.FromMilliseconds(150))
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        // Access key1 to keep it alive and make it most recently used
        Thread.Sleep(50);
        _ = cache.GetIfPresent("key1");

        // Add key4 - should evict key2 (LRU)
        cache.Put("key4", "value4");

        Assert.Equal(3, cache.EstimatedSize());
        Assert.Equal("value1", cache.GetIfPresent("key1")); // Still present
        Assert.Null(cache.GetIfPresent("key2")); // Evicted by LRU

        // Wait for remaining entries to expire
        Thread.Sleep(200);

        // Check expiration (key3 and key4 should be expired, key1 was accessed more recently)
        Assert.Null(cache.GetIfPresent("key3"));
        Assert.Null(cache.GetIfPresent("key4"));
    }

    [Fact]
    public void CombinedEviction_WithLoadingCache()
    {
        int loadCount = 0;
        var cache = Caffeine<int, string>.NewBuilder()
            .MaximumSize(2)
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build(key =>
            {
                loadCount++;
                return $"value{key}";
            });

        cache.Put(1, "value1");
        cache.Put(2, "value2");
        cache.Put(3, "value3"); // Should evict 1 by size

        Assert.Equal(2, cache.EstimatedSize());
        Assert.Null(cache.GetIfPresent(1)); // Evicted by size

        // Wait for expiration
        Thread.Sleep(150);

        // Remaining entries should be expired
        Assert.Null(cache.GetIfPresent(2));
        Assert.Null(cache.GetIfPresent(3));

        var stats = cache.Stats();
        Assert.True(stats.EvictionCount() >= 3);
    }

    [Fact]
    public void CombinedEviction_CleanUpRemovesExpiredAndRespectsSize()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(5)
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        Assert.Equal(3, cache.EstimatedSize());

        // Wait for expiration
        Thread.Sleep(150);

        // CleanUp should remove expired entries
        cache.CleanUp();

        Assert.Equal(0, cache.EstimatedSize());
    }

    [Fact]
    public void CombinedEviction_AccessUpdatesLRUAndResetExpiration()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .ExpireAfterAccess(TimeSpan.FromMilliseconds(150))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        Thread.Sleep(80);

        // Access key1 to reset its expiration timer
        _ = cache.GetIfPresent("key1");

        Thread.Sleep(100); // Total 180ms for key2 and key3, but only 100ms since key1 access

        // key2 and key3 should be expired, but key1 should still be valid
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        Assert.Null(cache.GetIfPresent("key2"));
        Assert.Null(cache.GetIfPresent("key3"));
    }

    [Fact]
    public void CombinedEviction_StatsTrackBothEvictionTypes()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(2)
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build();

        // Cause size-based eviction
        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3"); // Evicts key1

        long evictionsAfterSize = cache.Stats().EvictionCount();
        Assert.Equal(1, evictionsAfterSize);

        // Wait and cause time-based evictions
        Thread.Sleep(150);

        cache.GetIfPresent("key2"); // Trigger expiration check
        cache.GetIfPresent("key3"); // Trigger expiration check

        long evictionsAfterTime = cache.Stats().EvictionCount();
        Assert.True(evictionsAfterTime >= 3); // 1 from size + 2 from time
    }

    [Fact]
    public void CombinedEviction_AsMapReflectsBothEvictionTypes()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");
        cache.Put("key4", "value4"); // Evicts key1 by size

        var map1 = cache.AsMap();
        Assert.Equal(3, map1.Count);
        Assert.False(map1.ContainsKey("key1"));

        // Wait for expiration
        Thread.Sleep(150);

        var map2 = cache.AsMap();
        Assert.Equal(0, map2.Count); // All expired
    }
}

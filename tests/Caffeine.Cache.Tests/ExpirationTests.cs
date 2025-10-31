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

public class ExpirationTests
{
    [Fact]
    public void ExpireAfterWrite_RemovesEntriesAfterDuration()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        
        // Immediately after write, entry should be present
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        
        // Wait for expiration
        Thread.Sleep(150);
        
        // After expiration, entry should be gone
        Assert.Null(cache.GetIfPresent("key1"));
        
        // Check that eviction was recorded
        var stats = cache.Stats();
        Assert.True(stats.EvictionCount() > 0, "Expected eviction count to be greater than 0");
    }

    [Fact]
    public void ExpireAfterAccess_RemovesEntriesAfterInactivity()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterAccess(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build();

        cache.Put("key1", "value1");
        
        // Access the entry to reset the timer
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        Thread.Sleep(50);
        
        // Access again within the expiration window
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        Thread.Sleep(50);
        
        // Still accessible because we accessed it 50ms ago
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        
        // Wait for expiration
        Thread.Sleep(150);
        
        // After no access for 150ms, entry should be gone
        Assert.Null(cache.GetIfPresent("key1"));
    }

    [Fact]
    public void ExpireAfterWrite_WithLoadingCache()
    {
        int loadCount = 0;
        var cache = Caffeine<string, int>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build(key =>
            {
                loadCount++;
                return key.Length;
            });

        // Load a value
        var value1 = cache.Get("test", _ => 999);
        Assert.Equal(999, value1);
        
        // Wait for expiration
        Thread.Sleep(150);
        
        // Value should be expired and reloaded
        var value2 = cache.Get("test", _ => 888);
        Assert.Equal(888, value2);
        
        // Check statistics
        var stats = cache.Stats();
        Assert.True(stats.EvictionCount() > 0);
    }

    [Fact]
    public void ExpireAfterAccess_DoesNotExpireActiveEntries()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterAccess(TimeSpan.FromMilliseconds(100))
            .Build();

        cache.Put("key1", "value1");
        
        // Keep accessing the entry to prevent expiration
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(50);
            Assert.Equal("value1", cache.GetIfPresent("key1"));
        }
        
        // Entry should still be present after 250ms because it was accessed
        Assert.Equal("value1", cache.GetIfPresent("key1"));
    }

    [Fact]
    public void BothExpirations_UsesShortestDuration()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .ExpireAfterAccess(TimeSpan.FromMilliseconds(200))
            .Build();

        cache.Put("key1", "value1");
        
        // Access it multiple times
        Thread.Sleep(50);
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        Thread.Sleep(50);
        
        // Entry should be gone due to write expiration (100ms)
        // even though it was accessed more recently
        Thread.Sleep(50);
        Assert.Null(cache.GetIfPresent("key1"));
    }

    [Fact]
    public void CleanUp_RemovesExpiredEntries()
    {
        var cache = Caffeine<string, string>.NewBuilder()
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
    public void GetAllPresent_FiltersExpiredEntries()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        
        Thread.Sleep(50);
        cache.Put("key3", "value3"); // Fresh entry
        
        Thread.Sleep(70); // key1 and key2 expired, key3 still fresh
        
        var result = cache.GetAllPresent(new[] { "key1", "key2", "key3" });
        
        // Only key3 should be present
        Assert.Equal(1, result.Count);
        Assert.True(result.ContainsKey("key3"));
        Assert.False(result.ContainsKey("key1"));
        Assert.False(result.ContainsKey("key2"));
    }

    [Fact]
    public void PutAll_ResetsExpirationForAllEntries()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build();

        var data = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };

        cache.PutAll(data);
        
        Thread.Sleep(50);
        
        // Access to check they're still there
        Assert.Equal("value1", cache.GetIfPresent("key1"));
        Assert.Equal("value2", cache.GetIfPresent("key2"));
        
        Thread.Sleep(70);
        
        // Should be expired now
        Assert.Null(cache.GetIfPresent("key1"));
        Assert.Null(cache.GetIfPresent("key2"));
    }

    [Fact]
    public void AsMap_OnlyReturnsNonExpiredEntries()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        
        Thread.Sleep(150);
        
        cache.Put("key3", "value3"); // Fresh entry
        
        var map = cache.AsMap();
        
        // Only key3 should be in the map
        Assert.Equal(1, map.Count);
        Assert.True(map.ContainsKey("key3"));
        Assert.False(map.ContainsKey("key1"));
        Assert.False(map.ContainsKey("key2"));
    }
}

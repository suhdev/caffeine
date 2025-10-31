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

public class RefreshTests
{
    [Fact]
    public void RefreshAfterWrite_LoadsInitialValue()
    {
        int loadCount = 0;
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            loadCount++;
            return Task.FromResult<string?>($"value-{loadCount}");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromSeconds(1))
            .Build(loader);

        // For a refresh cache, we should use GetIfPresent or Get without mapping function
        // to use the configured loader
        cache.Put("key1", "initial");
        var value = cache.GetIfPresent("key1");
        Assert.Equal("initial", value);
        
        // Loader not called yet since we used Put
        Assert.Equal(0, loadCount);
    }

    [Fact]
    public async Task RefreshAfterWrite_TriggersAsyncRefresh()
    {
        int loadCount = 0;
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            loadCount++;
            return Task.FromResult<string?>($"value-{loadCount}");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build(loader);

        // Initial load - use Put to set initial value
        cache.Put("key1", "initial-value");
        var value1 = cache.GetIfPresent("key1");
        Assert.Equal("initial-value", value1);
        Assert.Equal(0, loadCount); // No loads yet since we used Put

        // Wait for entry to become stale
        await Task.Delay(150);

        // Access triggers async refresh but returns stale value
        var value2 = cache.GetIfPresent("key1");
        Assert.Equal("initial-value", value2); // Still returns stale value immediately

        // Wait for async refresh to complete
        await Task.Delay(100);

        // Now get refreshed value
        var value3 = cache.GetIfPresent("key1");
        Assert.Equal("value-1", value3); // Refreshed value
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task RefreshAfterWrite_DoesNotExpireEntry()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build(key => $"value-{key}");

        cache.Put("key1", "initial");

        // Wait for entry to become stale
        await Task.Delay(150);

        // Entry is still present (not expired)
        var value = cache.GetIfPresent("key1");
        Assert.NotNull(value);
        Assert.Equal("initial", value);
    }

    [Fact]
    public async Task RefreshAfterWrite_ReturnsStaleValueDuringRefresh()
    {
        var refreshStarted = new TaskCompletionSource<bool>();
        var continueRefresh = new TaskCompletionSource<bool>();
        
        var loader = new TestLoader<string, string>(async (key, ct) =>
        {
            refreshStarted.SetResult(true);
            await continueRefresh.Task;
            return "refreshed-value";
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build(loader);

        cache.Put("key1", "initial-value");

        // Wait for entry to become stale
        await Task.Delay(150);

        // Access triggers refresh
        var value1 = cache.GetIfPresent("key1");
        Assert.Equal("initial-value", value1); // Returns stale value immediately

        // Wait for refresh to start
        await refreshStarted.Task;

        // Even while refreshing, stale value is still returned
        var value2 = cache.GetIfPresent("key1");
        Assert.Equal("initial-value", value2);

        // Allow refresh to complete
        continueRefresh.SetResult(true);
        await Task.Delay(100);

        // Now get refreshed value
        var value3 = cache.GetIfPresent("key1");
        Assert.Equal("refreshed-value", value3);
    }

    [Fact]
    public async Task RefreshAfterWrite_TracksLoadStatistics()
    {
        int loadCount = 0;
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            loadCount++;
            return Task.FromResult<string?>($"value-{loadCount}");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build(loader);

        // Initial load
        cache.Get("key1", k => "initial");
        var stats1 = cache.Stats();
        Assert.Equal(1, stats1.LoadSuccessCount());

        // Wait for stale
        await Task.Delay(150);

        // Trigger refresh
        cache.GetIfPresent("key1");

        // Wait for refresh to complete
        await Task.Delay(100);

        // Check stats include refresh
        var stats2 = cache.Stats();
        Assert.Equal(2, stats2.LoadSuccessCount());
    }

    [Fact]
    public async Task RefreshAfterWrite_OnlyRefreshesOnce()
    {
        int refreshCount = 0;
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            Interlocked.Increment(ref refreshCount);
            return Task.FromResult<string?>($"value-{refreshCount}");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build(loader);

        cache.Put("key1", "initial");

        // Wait for entry to become stale
        await Task.Delay(150);

        // Multiple concurrent accesses should only trigger one refresh
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => cache.GetIfPresent("key1")))
            .ToArray();

        await Task.WhenAll(tasks);

        // Wait for any refreshes to complete
        await Task.Delay(100);

        // Only one refresh should have occurred
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task RefreshAfterWrite_HandlesLoaderFailure()
    {
        int callCount = 0;
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            Interlocked.Increment(ref callCount);
            throw new InvalidOperationException("Refresh failed");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .RecordStats()
            .Build(loader);

        // Initial load via Put
        cache.Put("key1", "initial-value");

        // Wait for stale
        await Task.Delay(150);

        // Trigger refresh (which will fail)
        var value1 = cache.GetIfPresent("key1");
        Assert.Equal("initial-value", value1); // Stale value returned

        // Wait for failed refresh to complete
        await Task.Delay(250);

        // Stale value is still present after failed refresh
        var value2 = cache.GetIfPresent("key1");
        Assert.Equal("initial-value", value2);

        // Second GetIfPresent above might trigger another refresh if entry is still stale
        // Wait a bit more for any additional refreshes
        await Task.Delay(150);

        // Stats should show at least one failed load (could be more if retry happened)
        var stats = cache.Stats();
        Assert.True(stats.LoadFailureCount() >= 1, $"Expected at least 1 load failure, got {stats.LoadFailureCount()}");
    }

    [Fact]
    public async Task RefreshAfterWrite_WorksWithGetAllPresent()
    {
        int loadCount = 0;
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            loadCount++;
            return Task.FromResult<string?>($"refreshed-{loadCount}");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .Build(loader);

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");

        // Wait for entries to become stale
        await Task.Delay(150);

        // GetAllPresent should trigger refreshes
        var result = cache.GetAllPresent(new[] { "key1", "key2" });
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["key1"]); // Returns stale values
        Assert.Equal("value2", result["key2"]);

        // Wait for refreshes to complete
        await Task.Delay(100);

        // Now should have refreshed values
        var value1 = cache.GetIfPresent("key1");
        var value2 = cache.GetIfPresent("key2");
        Assert.StartsWith("refreshed-", value1);
        Assert.StartsWith("refreshed-", value2);
    }

    [Fact]
    public async Task RefreshAfterWrite_NotifiesRemovalListener()
    {
        var removals = new List<(string Key, string Value, RemovalCause Cause)>();
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            return Task.FromResult<string?>("refreshed-value");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(100))
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build(loader);

        cache.Put("key1", "initial-value");

        // Wait for entry to become stale
        await Task.Delay(150);

        // Trigger refresh
        cache.GetIfPresent("key1");

        // Wait for refresh to complete
        await Task.Delay(100);

        // Should have notified listener of replacement
        Assert.Single(removals);
        Assert.Equal("key1", removals[0].Key);
        Assert.Equal("initial-value", removals[0].Value);
        Assert.Equal(RemovalCause.Replaced, removals[0].Cause);
    }

    [Fact]
    public void RefreshAfterWrite_RequiresLoader()
    {
        // RefreshAfterWrite should only work with loading caches
        var builder = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromSeconds(1));

        // Build without loader should still create a cache, but refresh won't work
        var cache = builder.Build();
        cache.Put("key1", "value1");

        // Cache should work normally without refresh
        var value = cache.GetIfPresent("key1");
        Assert.Equal("value1", value);
    }

    [Fact]
    public async Task RefreshAfterWrite_PutResetsRefreshTimer()
    {
        int loadCount = 0;
        var loader = new TestLoader<string, string>((key, ct) =>
        {
            loadCount++;
            return Task.FromResult<string?>($"refreshed-{loadCount}");
        });

        var cache = Caffeine<string, string>.NewBuilder()
            .RefreshAfterWrite(TimeSpan.FromMilliseconds(200))
            .Build(loader);

        cache.Put("key1", "initial");

        // Wait half the refresh time
        await Task.Delay(100);

        // Update the entry (resets refresh timer)
        cache.Put("key1", "updated");

        // Wait another 100ms (total 200ms since initial, but only 100ms since update)
        await Task.Delay(100);

        // Should not have triggered refresh yet
        var value = cache.GetIfPresent("key1");
        Assert.Equal("updated", value);
        Assert.Equal(0, loadCount); // No refresh happened

        // Wait for entry to become stale after update
        await Task.Delay(150);

        // Now it should trigger refresh
        cache.GetIfPresent("key1");
        await Task.Delay(100);

        var refreshedValue = cache.GetIfPresent("key1");
        Assert.StartsWith("refreshed-", refreshedValue);
        Assert.Equal(1, loadCount);
    }

    private class TestLoader<K, V> : ICacheLoader<K, V> where K : notnull
    {
        private readonly Func<K, CancellationToken, Task<V?>> _asyncLoad;

        public TestLoader(Func<K, CancellationToken, Task<V?>> asyncLoad)
        {
            _asyncLoad = asyncLoad;
        }

        public V? Load(K key) => AsyncLoad(key).GetAwaiter().GetResult();

        public Task<V?> AsyncLoad(K key, CancellationToken cancellationToken = default)
        {
            return _asyncLoad(key, cancellationToken);
        }

        Task<V?> ICacheLoader<K, V>.AsyncLoad(K key) => AsyncLoad(key);
    }
}

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

namespace Caffeine.Cache.Tests;

public class RemovalListenerTests
{
    [Fact]
    public void RemovalListener_NotifiedOnExplicitRemoval()
    {
        var removals = new ConcurrentBag<(string? key, string? value, RemovalCause cause)>();

        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Put("key1", "value1");
        cache.Invalidate("key1");

        Assert.Single(removals);
        var removal = removals.First();
        Assert.Equal("key1", removal.key);
        Assert.Equal("value1", removal.value);
        Assert.Equal(RemovalCause.Explicit, removal.cause);
    }

    [Fact]
    public void RemovalListener_NotifiedOnReplacement()
    {
        var removals = new ConcurrentBag<(string? key, string? value, RemovalCause cause)>();

        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key1", "value2"); // Replace

        Assert.Single(removals);
        var removal = removals.First();
        Assert.Equal("key1", removal.key);
        Assert.Equal("value1", removal.value);
        Assert.Equal(RemovalCause.Replaced, removal.cause);
    }

    [Fact]
    public void RemovalListener_NotifiedOnInvalidateAll()
    {
        var removals = new ConcurrentBag<(string? key, string? value, RemovalCause cause)>();

        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        cache.InvalidateAll();

        Assert.Equal(3, removals.Count);
        Assert.All(removals, r => Assert.Equal(RemovalCause.Explicit, r.cause));
    }

    [Fact]
    public void RemovalListener_NotifiedOnInvalidateAllWithKeys()
    {
        var removals = new ConcurrentBag<(string? key, string? value, RemovalCause cause)>();

        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");
        cache.Put("key3", "value3");

        cache.InvalidateAll(new[] { "key1", "key3" });

        Assert.Equal(2, removals.Count);
        Assert.Contains(removals, r => r.key == "key1" && r.value == "value1");
        Assert.Contains(removals, r => r.key == "key3" && r.value == "value3");
        Assert.All(removals, r => Assert.Equal(RemovalCause.Explicit, r.cause));
    }

    [Fact]
    public void RemovalListener_NotInvokedOnMissingKey()
    {
        var removals = new ConcurrentBag<(string? key, string? value, RemovalCause cause)>();

        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Invalidate("nonexistent");

        Assert.Empty(removals);
    }

    [Fact]
    public void RemovalListener_ExceptionsSuppressed()
    {
        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => throw new Exception("Test exception"))
            .Build();

        cache.Put("key1", "value1");

        // Should not throw
        cache.Invalidate("key1");
        cache.Put("key2", "value2");
        cache.Put("key2", "value3"); // Replacement
    }

    [Fact]
    public void RemovalListener_CauseReflectsAction()
    {
        var removals = new ConcurrentBag<(string? key, string? value, RemovalCause cause)>();

        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key1", "value2");  // Replaced
        cache.Invalidate("key1");     // Explicit

        Assert.Equal(2, removals.Count);
        Assert.Contains(removals, r => r.cause == RemovalCause.Replaced);
        Assert.Contains(removals, r => r.cause == RemovalCause.Explicit);
    }

    [Fact]
    public void RemovalListener_WasEvictedMethod()
    {
        Assert.False(RemovalCause.Explicit.WasEvicted());
        Assert.False(RemovalCause.Replaced.WasEvicted());
        Assert.True(RemovalCause.Expired.WasEvicted());
        Assert.True(RemovalCause.Size.WasEvicted());
        Assert.True(RemovalCause.Collected.WasEvicted());
    }

    [Fact]
    public void RemovalListener_CannotSetTwice()
    {
        var builder = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => { });

        Assert.Throws<InvalidOperationException>(() =>
            builder.RemovalListener((k, v, cause) => { }));
    }

    [Fact]
    public void RemovalListener_NullListenerThrows()
    {
        var builder = Caffeine<string, string>.NewBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.RemovalListener(null!));
    }

    [Fact]
    public void RemovalListener_PutAllReplacements()
    {
        var removals = new ConcurrentBag<(string? key, string? value, RemovalCause cause)>();

        var cache = Caffeine<string, string>.NewBuilder()
            .RemovalListener((k, v, cause) => removals.Add((k, v, cause)))
            .Build();

        cache.Put("key1", "value1");
        cache.Put("key2", "value2");

        var updates = new Dictionary<string, string>
        {
            ["key1"] = "newValue1",
            ["key3"] = "value3"
        };

        cache.PutAll(updates);

        // Should have 1 replacement notification for key1
        Assert.Single(removals);
        var removal = removals.First();
        Assert.Equal("key1", removal.key);
        Assert.Equal("value1", removal.value);
        Assert.Equal(RemovalCause.Replaced, removal.cause);
    }
}

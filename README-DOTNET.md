# Caffeine.NET - High Performance Caching Library for .NET 8

This is a C# .NET 8 port of [Caffeine](https://github.com/ben-manes/caffeine), a high-performance, near-optimal caching library for Java.

## Status

🚀 **Production-Ready Implementation with Async Support** - The caching library now supports asynchronous operations!

### Completed Components

- ✅ Core cache interfaces (`ICache<K,V>`, `IAsyncCache<K,V>`)
- ✅ Statistics interfaces and implementations (`ICacheStats`, `CacheStats`)
- ✅ Policy interfaces (`IPolicy<K,V>`)
- ✅ Cache builder with fluent API (`Caffeine<K,V>`)
- ✅ Simple concurrent cache implementation
- ✅ Loading cache with automatic value loading
- ✅ Cache loader interface (`ICacheLoader<K,V>`)
- ✅ **Time-based expiration (ExpireAfterWrite, ExpireAfterAccess)** ⭐
- ✅ **Size-based eviction with LRU policy** ⭐
- ✅ **Combined size + time-based eviction** ⭐
- ✅ **Removal listeners with cause tracking** ⭐
- ✅ **Async cache support (IAsyncCache, IAsyncLoadingCache)** ⭐ NEW
- ✅ Comprehensive test suite (77/77 tests passing)
- ✅ Working example application
- ✅ .NET 8 project structure with solution file

### In Progress / Future Work

- 🔄 Automatic refresh (RefreshAfterWrite)
- 🔄 Custom weigher for eviction
- 🔄 Weak/soft reference support
- 🔄 Advanced eviction policies (W-TinyLFU)

## Overview

Caffeine provides an in-memory cache using a design inspired by Google Guava. This C# port provides high-performance caching capabilities with .NET-specific improvements and idioms.

### Key Features

- **Thread-safe** operations using `ConcurrentDictionary` and synchronized access
- **Fluent builder API** for easy configuration
- **Manual and automatic** cache loading
- **Time-based expiration** - ExpireAfterWrite and ExpireAfterAccess with automatic eviction ⭐
- **Size-based eviction** - LRU (Least Recently Used) policy when maximum size is exceeded ⭐
- **Combined eviction** - Simultaneous size and time-based eviction in a single cache ⭐
- **Removal listeners** - Event notifications for all cache entry removals with cause tracking ⭐
- **Async operations** - Full async/await support with IAsyncCache and IAsyncLoadingCache ⭐
- **Statistics tracking** for monitoring cache performance including eviction counts
- **Flexible configuration** with initial capacity, maximum size, and expiration settings
- **Type-safe** with generic support and nullable reference types

## Example Usage

```csharp
using Caffeine.Cache;

// Create a simple cache
var cache = Caffeine<string, string>.NewBuilder()
    .InitialCapacity(100)
    .MaximumSize(10_000)  // LRU eviction when limit exceeded
    .ExpireAfterWrite(TimeSpan.FromMinutes(5))  // Entries expire 5 minutes after write
    .RecordStats()
    .Build();

// Put and get values
cache.Put("greeting", "Hello, World!");
var value = cache.GetIfPresent("greeting");
Console.WriteLine(value); // Hello, World!

// Get with computing function
var computed = cache.Get("missing", key => "Computed Value");
Console.WriteLine(computed); // Computed Value

// Create a cache with both size and time-based eviction
var combinedCache = Caffeine<string, string>.NewBuilder()
    .MaximumSize(100)  // Keep only 100 entries
    .ExpireAfterWrite(TimeSpan.FromMinutes(5))  // Also expire after 5 minutes
    .RecordStats()
    .Build();

// Entries are evicted by LRU when size is exceeded
// OR by time expiration, whichever comes first
combinedCache.Put("key1", "value1");
// Entry will be evicted if cache grows beyond 100 entries
// OR if 5 minutes pass since it was written

// Cache with removal listener
var monitoredCache = Caffeine<string, string>.NewBuilder()
    .MaximumSize(100)
    .RemovalListener((key, value, cause) =>
    {
        // Log or handle removed entries
        Console.WriteLine($"Removed {key}: {cause}");
    })
    .Build();

// Listener is notified for all removals:
// - Explicit (Invalidate)
// - Replaced (Put on existing key)
// - Size (LRU eviction)
// - Expired (Time-based)
// - Collected (Weak/soft references, when implemented)

// Create a cache with access-based expiration
var sessionCache = Caffeine<string, string>.NewBuilder()
    .ExpireAfterAccess(TimeSpan.FromMinutes(30))  // Expire 30 min after last access
    .Build();

sessionCache.Put("sessionId", "userData");
// Entry stays fresh as long as it's accessed within 30 minutes

// Create a loading cache
var loadingCache = Caffeine<int, string>.NewBuilder()
    .MaximumSize(1000)
    .RecordStats()
    .Build(key => $"Value-{key}");

// Values are loaded automatically when missing
loadingCache.Put(1, "One");
var one = loadingCache.GetIfPresent(1);
Console.WriteLine(one); // One

// Check statistics
var stats = cache.Stats();
Console.WriteLine($"Hit Rate: {stats.HitRate():P2}");
Console.WriteLine($"Miss Rate: {stats.MissRate():P2}");

// Async cache operations
var asyncCache = Caffeine<int, string>.NewBuilder()
    .RecordStats()
    .BuildAsync();

await asyncCache.PutAsync(1, "value1");
var value = await asyncCache.GetIfPresentAsync(1);

// Async cache with computation
var computed = await asyncCache.GetAsync(2, async (key, ct) =>
{
    await Task.Delay(100, ct); // Simulate async operation
    return $"computed-{key}";
});

// Async loading cache
var asyncLoadingCache = Caffeine<int, User>.NewBuilder()
    .ExpireAfterWrite(TimeSpan.FromMinutes(5))
    .BuildAsync(async (userId, ct) =>
    {
        // Simulate async database call
        await Task.Delay(50, ct);
        return await FetchUserFromDatabaseAsync(userId, ct);
    });

var user = await asyncLoadingCache.GetAsync(42);
var users = await asyncLoadingCache.GetAllAsync(new[] { 1, 2, 3 });

// Access synchronous view from async cache
var syncView = asyncCache.Synchronous();
syncView.Put(3, "value3");
```

For more examples, see the [Caffeine.Example](examples/Caffeine.Example/Program.cs) project.

## Building

```bash
dotnet build Caffeine.sln
```

## Testing

```bash
dotnet test Caffeine.sln
```

## Running Examples

```bash
dotnet run --project examples/Caffeine.Example/Caffeine.Example.csproj
```

## Requirements

- .NET 8.0 SDK or later

## License

Licensed under the Apache License, Version 2.0. See LICENSE file for details.

## Original Project

This is a port of the original Caffeine library:
- Repository: https://github.com/ben-manes/caffeine
- Author: Ben Manes (ben.manes@gmail.com)

## Contributing

This is an automated conversion project. Contributions and feedback are welcome once the initial conversion is complete.

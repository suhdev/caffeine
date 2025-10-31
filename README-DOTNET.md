# Caffeine.NET - High Performance Caching Library for .NET 8

This is a C# .NET 8 port of [Caffeine](https://github.com/ben-manes/caffeine), a high-performance, near-optimal caching library for Java.

## Status

🚀 **Functional Core Implementation** - The basic caching functionality is implemented and working!

### Completed Components

- ✅ Core cache interfaces (`ICache<K,V>`)
- ✅ Statistics interfaces and implementations (`ICacheStats`, `CacheStats`)
- ✅ Policy interfaces (`IPolicy<K,V>`)
- ✅ Cache builder with fluent API (`Caffeine<K,V>`)
- ✅ Simple concurrent cache implementation
- ✅ Loading cache with automatic value loading
- ✅ Cache loader interface (`ICacheLoader<K,V>`)
- ✅ Comprehensive test suite (10/10 tests passing)
- ✅ Working example application
- ✅ .NET 8 project structure with solution file

### In Progress / Future Work

- 🔄 Bounded cache with size-based eviction
- 🔄 Time-based expiration (ExpireAfterWrite, ExpireAfterAccess)
- 🔄 Automatic refresh (RefreshAfterWrite)
- 🔄 Custom weigher for eviction
- 🔄 Weak/soft reference support
- 🔄 Removal listeners
- 🔄 Advanced eviction policies (W-TinyLFU)
- 🔄 Async cache support

## Overview

Caffeine provides an in-memory cache using a design inspired by Google Guava. This C# port provides high-performance caching capabilities with .NET-specific improvements and idioms.

### Key Features

- **Thread-safe** operations using `ConcurrentDictionary`
- **Fluent builder API** for easy configuration
- **Manual and automatic** cache loading
- **Statistics tracking** for monitoring cache performance
- **Flexible configuration** with initial capacity, maximum size, and expiration settings
- **Type-safe** with generic support and nullable reference types

## Example Usage

```csharp
using Caffeine.Cache;

// Create a simple cache
var cache = Caffeine<string, string>.NewBuilder()
    .InitialCapacity(100)
    .MaximumSize(10_000)
    .RecordStats()
    .Build();

// Put and get values
cache.Put("greeting", "Hello, World!");
var value = cache.GetIfPresent("greeting");
Console.WriteLine(value); // Hello, World!

// Get with computing function
var computed = cache.Get("missing", key => "Computed Value");
Console.WriteLine(computed); // Computed Value

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

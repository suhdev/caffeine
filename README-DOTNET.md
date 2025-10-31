# Caffeine.NET - High Performance Caching Library for .NET 8

This is a C# .NET 8 port of [Caffeine](https://github.com/ben-manes/caffeine), a high-performance, near-optimal caching library for Java.

## Status

🚧 **Work in Progress** - This is an active conversion of the Caffeine caching library to C# .NET 8.

### Completed Components

- ✅ Core cache interfaces (`ICache<K,V>`)
- ✅ Statistics interfaces and implementations (`ICacheStats`, `CacheStats`)
- ✅ Policy interfaces (`IPolicy<K,V>`)
- ✅ .NET 8 project structure with solution file

### In Progress

- 🔄 Cache builder implementation (`Caffeine` class)
- 🔄 Cache implementations (Bounded, Unbounded)
- 🔄 Eviction policies
- 🔄 Async support
- 🔄 Test suite

## Overview

Caffeine provides an in-memory cache using a design inspired by Google Guava. This C# port aims to provide the same high-performance caching capabilities with .NET-specific improvements and idioms.

### Key Features (Target)

- **Automatic loading** of entries into the cache, optionally asynchronously
- **Size-based eviction** when a maximum is exceeded based on frequency and recency
- **Time-based expiration** of entries, measured since last access or last write
- **Asynchronous refresh** when the first stale request for an entry occurs
- **Weak references** for keys (using ConditionalWeakTable)
- **Soft/Weak references** for values
- **Notification** of evicted (or otherwise removed) entries
- **Writes propagated** to an external resource
- **Cache access statistics**

## Example Usage (Target API)

```csharp
using Caffeine.Cache;

// Create a cache with a maximum size
var cache = Caffeine<string, Graph>
    .NewBuilder()
    .MaximumSize(10_000)
    .ExpireAfterWrite(TimeSpan.FromMinutes(5))
    .RefreshAfterWrite(TimeSpan.FromMinutes(1))
    .Build(key => CreateExpensiveGraph(key));

// Use the cache
var graph = cache.Get("key", k => CreateExpensiveGraph(k));
```

## Building

```bash
dotnet build Caffeine.sln
```

## Testing

```bash
dotnet test Caffeine.sln
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

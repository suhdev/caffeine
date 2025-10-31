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

using Caffeine.Cache;

namespace Caffeine.Example;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Caffeine.NET Cache Examples ===\n");

        // Example 1: Simple Cache
        SimpleCache();
        Console.WriteLine();

        // Example 2: Loading Cache
        LoadingCache();
        Console.WriteLine();

        // Example 3: Cache with Statistics
        CacheWithStatistics();
        Console.WriteLine();

        // Example 4: Cache with Size Limit
        CacheWithSizeLimit();
        Console.WriteLine();

        // Example 5: Cache with Time-Based Expiration
        CacheWithExpiration();
        Console.WriteLine();

        // Example 6: Combined Size and Time-Based Eviction
        CombinedEviction();
        Console.WriteLine();

        // Example 7: Removal Listener
        RemovalListenerExample();
        Console.WriteLine();
    }

    static void SimpleCache()
    {
        Console.WriteLine("Example 1: Simple Cache");
        Console.WriteLine("------------------------");

        // Create a simple cache
        var cache = Caffeine<string, string>.NewBuilder()
            .InitialCapacity(100)
            .Build();

        // Add some values
        cache.Put("greeting", "Hello, World!");
        cache.Put("language", "C#");
        cache.Put("framework", ".NET 8");

        // Retrieve values
        Console.WriteLine($"Greeting: {cache.GetIfPresent("greeting")}");
        Console.WriteLine($"Language: {cache.GetIfPresent("language")}");
        Console.WriteLine($"Framework: {cache.GetIfPresent("framework")}");

        // Get with computing function
        var version = cache.Get("version", key => "8.0");
        Console.WriteLine($"Version: {version}");

        Console.WriteLine($"Cache size: {cache.EstimatedSize()}");
    }

    static void LoadingCache()
    {
        Console.WriteLine("Example 2: Loading Cache");
        Console.WriteLine("------------------------");

        int loadCount = 0;

        // Create a cache that automatically loads values
        var cache = Caffeine<int, string>.NewBuilder()
            .Build(key =>
            {
                loadCount++;
                Console.WriteLine($"  Loading value for key: {key}");
                return $"Value-{key}";
            });

        // First access - value not in cache yet
        Console.WriteLine($"Getting key 1 (GetIfPresent): {cache.GetIfPresent(1) ?? "not found"}");
        
        // Add value manually
        cache.Put(1, "Manually added");
        Console.WriteLine($"Getting key 1 again (GetIfPresent): {cache.GetIfPresent(1)}");
        
        // Get with override loader (doesn't use constructor loader)
        var value2 = cache.Get(2, k => $"Override-{k}");
        Console.WriteLine($"Getting key 2 (with override loader): {value2}");
        
        Console.WriteLine($"Total constructor loads: {loadCount}");
    }

    static void CacheWithStatistics()
    {
        Console.WriteLine("Example 3: Cache with Statistics");
        Console.WriteLine("--------------------------------");

        // Create a cache with statistics enabled
        var cache = Caffeine<string, int>.NewBuilder()
            .RecordStats()
            .Build();

        // Generate some cache activity
        cache.Put("one", 1);
        cache.Put("two", 2);
        cache.Put("three", 3);

        // Some hits
        cache.GetIfPresent("one");
        cache.GetIfPresent("two");
        cache.GetIfPresent("three");

        // Some misses
        cache.GetIfPresent("four");
        cache.GetIfPresent("five");

        // Get statistics
        var stats = cache.Stats();
        Console.WriteLine($"Requests: {stats.RequestCount()}");
        Console.WriteLine($"Hits: {stats.HitCount()}");
        Console.WriteLine($"Misses: {stats.MissCount()}");
        Console.WriteLine($"Hit Rate: {stats.HitRate():P2}");
        Console.WriteLine($"Miss Rate: {stats.MissRate():P2}");
    }

    static void CacheWithSizeLimit()
    {
        Console.WriteLine("Example 4: Cache with Size-Based Eviction - ✅ WORKING");
        Console.WriteLine("--------------------------------------------------------");

        // Create a cache with a maximum size
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .RecordStats()
            .Build();

        // Add 3 items - should fit within limit
        cache.Put("A", "Value A");
        cache.Put("B", "Value B");
        cache.Put("C", "Value C");
        Console.WriteLine($"Initial cache size: {cache.EstimatedSize()}");
        Console.WriteLine($"Keys: A={cache.GetIfPresent("A")}, B={cache.GetIfPresent("B")}, C={cache.GetIfPresent("C")}");
        
        // Add 4th item - should evict the oldest (A)
        Console.WriteLine("\nAdding 'D' (exceeds max size of 3)...");
        cache.Put("D", "Value D");
        
        Console.WriteLine($"Cache size after adding D: {cache.EstimatedSize()}");
        Console.WriteLine($"A: {cache.GetIfPresent("A") ?? "null (evicted)"} ");
        Console.WriteLine($"B: {cache.GetIfPresent("B")} (kept)");
        Console.WriteLine($"C: {cache.GetIfPresent("C")} (kept)");
        Console.WriteLine($"D: {cache.GetIfPresent("D")} (new)");
        
        // Access B to make it most recently used
        Console.WriteLine("\nAccessing 'B' to make it most recently used...");
        _ = cache.GetIfPresent("B");
        
        // Add E - should evict C (LRU policy)
        Console.WriteLine("Adding 'E'...");
        cache.Put("E", "Value E");
        
        Console.WriteLine($"\nCache size: {cache.EstimatedSize()}");
        Console.WriteLine($"B: {cache.GetIfPresent("B")} (kept - recently accessed)");
        Console.WriteLine($"C: {cache.GetIfPresent("C") ?? "null (evicted as LRU)"} ");
        Console.WriteLine($"D: {cache.GetIfPresent("D")} (kept)");
        Console.WriteLine($"E: {cache.GetIfPresent("E")} (new)");
        
        // Check statistics
        var stats = cache.Stats();
        Console.WriteLine($"\nStatistics:");
        Console.WriteLine($"  Evictions: {stats.EvictionCount()} (due to size limit)");
        Console.WriteLine($"  Hit Rate: {stats.HitRate():P2}");
    }

    static void CacheWithExpiration()
    {
        Console.WriteLine("Example 5: Cache with Time-Based Expiration - ✅ WORKING");
        Console.WriteLine("----------------------------------------------------------");

        // Create a cache with time-based expiration
        var cache = Caffeine<string, string>.NewBuilder()
            .ExpireAfterWrite(TimeSpan.FromSeconds(2))
            .RecordStats()
            .Build();

        // Add some values
        cache.Put("session1", "user123");
        cache.Put("session2", "user456");
        
        Console.WriteLine($"Initial cache size: {cache.EstimatedSize()}");
        Console.WriteLine($"Value for session1: {cache.GetIfPresent("session1")}");
        
        // Wait 1 second (entries still valid)
        Console.WriteLine("\nWaiting 1 second...");
        Thread.Sleep(1000);
        Console.WriteLine($"After 1s - session1: {cache.GetIfPresent("session1")} (still valid)");
        Console.WriteLine($"Cache size: {cache.EstimatedSize()}");
        
        // Wait another 1.5 seconds (entries should expire)
        Console.WriteLine("\nWaiting another 1.5 seconds (total 2.5s)...");
        Thread.Sleep(1500);
        Console.WriteLine($"After 2.5s - session1: {cache.GetIfPresent("session1") ?? "null (expired)"} ");
        Console.WriteLine($"After 2.5s - session2: {cache.GetIfPresent("session2") ?? "null (expired)"} ");
        
        // Check statistics
        var stats = cache.Stats();
        Console.WriteLine($"\nStatistics:");
        Console.WriteLine($"  Hits: {stats.HitCount()}");
        Console.WriteLine($"  Misses: {stats.MissCount()}");
        Console.WriteLine($"  Evictions: {stats.EvictionCount()} (expired entries)");
        
        // Add a new entry to show it works after expiration
        cache.Put("session3", "user789");
        Console.WriteLine($"\nAdded new entry after expiration");
        Console.WriteLine($"Cache size: {cache.EstimatedSize()}");
        Console.WriteLine($"session3: {cache.GetIfPresent("session3")}");
    }

    static void CombinedEviction()
    {
        Console.WriteLine("Example 6: Combined Size & Time-Based Eviction - ✅ WORKING");
        Console.WriteLine("------------------------------------------------------------");

        // Create a cache with both size limit and time expiration
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .ExpireAfterWrite(TimeSpan.FromSeconds(2))
            .RecordStats()
            .Build();

        // Add 3 entries - should fit within limit
        Console.WriteLine("Adding 3 entries (within size limit):");
        cache.Put("A", "Value A");
        cache.Put("B", "Value B");
        cache.Put("C", "Value C");
        Console.WriteLine($"Cache size: {cache.EstimatedSize()}");
        
        // Add 4th entry - should evict oldest by LRU (A)
        Console.WriteLine("\nAdding 'D' (exceeds max size of 3)...");
        cache.Put("D", "Value D");
        
        Console.WriteLine($"Cache size: {cache.EstimatedSize()}");
        Console.WriteLine($"A: {cache.GetIfPresent("A") ?? "null (evicted by size limit)"}");
        Console.WriteLine($"B: {cache.GetIfPresent("B")} (kept)");
        Console.WriteLine($"C: {cache.GetIfPresent("C")} (kept)");
        Console.WriteLine($"D: {cache.GetIfPresent("D")} (new)");
        
        // Wait 1 second - entries still valid
        Console.WriteLine("\nWaiting 1 second...");
        Thread.Sleep(1000);
        Console.WriteLine($"B: {cache.GetIfPresent("B")} (still valid)");
        
        // Wait another 1.5 seconds - should expire
        Console.WriteLine("\nWaiting another 1.5 seconds (total 2.5s)...");
        Thread.Sleep(1500);
        
        Console.WriteLine($"B: {cache.GetIfPresent("B") ?? "null (expired by time)"}");
        Console.WriteLine($"C: {cache.GetIfPresent("C") ?? "null (expired by time)"}");
        Console.WriteLine($"D: {cache.GetIfPresent("D") ?? "null (expired by time)"}");
        
        // Add fresh entries after eviction
        Console.WriteLine("\nAdding fresh entries after eviction:");
        cache.Put("E", "Value E");
        cache.Put("F", "Value F");
        
        Console.WriteLine($"Cache size: {cache.EstimatedSize()}");
        Console.WriteLine($"E: {cache.GetIfPresent("E")}");
        Console.WriteLine($"F: {cache.GetIfPresent("F")}");
        
        // Check statistics
        var stats = cache.Stats();
        Console.WriteLine($"\nStatistics:");
        Console.WriteLine($"  Total Evictions: {stats.EvictionCount()} (size + time)");
        Console.WriteLine($"  Hit Rate: {stats.HitRate():P2}");
        Console.WriteLine($"  Miss Rate: {stats.MissRate():P2}");
    }

    static void RemovalListenerExample()
    {
        Console.WriteLine("Example 7: Removal Listener - ✅ WORKING");
        Console.WriteLine("------------------------------------------");

        // Track removed entries
        var removedEntries = new List<(string? key, string? value, string cause)>();

        // Create a cache with a removal listener
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .RemovalListener((key, value, cause) =>
            {
                var causeStr = cause switch
                {
                    RemovalCause.Explicit => "Explicit removal",
                    RemovalCause.Replaced => "Value replaced",
                    RemovalCause.Size => "Size limit",
                    RemovalCause.Expired => "Expired",
                    _ => cause.ToString()
                };
                removedEntries.Add((key, value, causeStr));
                Console.WriteLine($"  Removed: key='{key}', value='{value}', cause={causeStr}");
            })
            .Build();

        Console.WriteLine("Adding entries...");
        cache.Put("user1", "Alice");
        cache.Put("user2", "Bob");
        cache.Put("user3", "Charlie");

        Console.WriteLine("\nReplacing user2:");
        cache.Put("user2", "Robert"); // Triggers Replaced event

        Console.WriteLine("\nAdding user4 (exceeds size limit):");
        cache.Put("user4", "Diana"); // Triggers Size eviction for user1

        Console.WriteLine("\nExplicitly removing user3:");
        cache.Invalidate("user3"); // Triggers Explicit removal

        Console.WriteLine("\nRemoving all remaining entries:");
        cache.InvalidateAll(); // Triggers Explicit removal for user2 and user4

        Console.WriteLine($"\nTotal removal events captured: {removedEntries.Count}");
        Console.WriteLine($"  Replaced: {removedEntries.Count(e => e.cause == "Value replaced")}");
        Console.WriteLine($"  Size limit: {removedEntries.Count(e => e.cause == "Size limit")}");
        Console.WriteLine($"  Explicit: {removedEntries.Count(e => e.cause == "Explicit removal")}");
    }
}

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
        Console.WriteLine("Example 4: Cache with Size Limit (configured but not yet enforced)");
        Console.WriteLine("------------------------------------------------------------------");

        // Create a cache with a maximum size
        // Note: Eviction is not yet implemented, but the API is ready
        var cache = Caffeine<string, string>.NewBuilder()
            .MaximumSize(3)
            .RecordStats()
            .Build();

        // Add items
        cache.Put("A", "Value A");
        cache.Put("B", "Value B");
        cache.Put("C", "Value C");
        cache.Put("D", "Value D"); // Would exceed max size in full implementation

        Console.WriteLine($"Cache size: {cache.EstimatedSize()}");
        Console.WriteLine("Note: Automatic eviction based on size is planned for future implementation");
        
        // Manual invalidation works
        cache.Invalidate("A");
        Console.WriteLine($"After invalidating 'A': {cache.EstimatedSize()}");
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
}

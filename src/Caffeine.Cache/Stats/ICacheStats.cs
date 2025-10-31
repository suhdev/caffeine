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

namespace Caffeine.Cache.Stats;

/// <summary>
/// Statistics about the performance of a cache.
/// </summary>
public interface ICacheStats
{
    /// <summary>
    /// Returns the number of times cache lookup methods have returned either a cached or
    /// uncached value. This is defined as <c>HitCount + MissCount</c>.
    /// </summary>
    long RequestCount();

    /// <summary>
    /// Returns the number of times cache lookup methods have returned a cached value.
    /// </summary>
    long HitCount();

    /// <summary>
    /// Returns the ratio of cache requests which were hits. This is defined as
    /// <c>HitCount / RequestCount</c>, or <c>1.0</c> when <c>RequestCount == 0</c>.
    /// </summary>
    double HitRate();

    /// <summary>
    /// Returns the number of times cache lookup methods have returned an uncached (newly
    /// loaded) value, or null.
    /// </summary>
    long MissCount();

    /// <summary>
    /// Returns the ratio of cache requests which were misses. This is defined as
    /// <c>MissCount / RequestCount</c>, or <c>0.0</c> when <c>RequestCount == 0</c>.
    /// </summary>
    double MissRate();

    /// <summary>
    /// Returns the total number of times that cache lookup methods attempted to load new
    /// values. This includes both successful load operations and those that threw exceptions.
    /// </summary>
    long LoadCount();

    /// <summary>
    /// Returns the number of times cache lookup methods have successfully loaded a new value.
    /// </summary>
    long LoadSuccessCount();

    /// <summary>
    /// Returns the number of times cache lookup methods failed to load a new value.
    /// </summary>
    long LoadFailureCount();

    /// <summary>
    /// Returns the ratio of cache loading attempts which threw exceptions.
    /// </summary>
    double LoadFailureRate();

    /// <summary>
    /// Returns the total number of nanoseconds the cache has spent loading new values.
    /// </summary>
    long TotalLoadTime();

    /// <summary>
    /// Returns the average number of nanoseconds spent loading new values.
    /// </summary>
    double AverageLoadPenalty();

    /// <summary>
    /// Returns the number of times an entry has been evicted.
    /// </summary>
    long EvictionCount();

    /// <summary>
    /// Returns the sum of weights of evicted entries.
    /// </summary>
    long EvictionWeight();
}

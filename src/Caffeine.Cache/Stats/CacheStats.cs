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
/// Statistics about the performance of a cache. This is an immutable value type.
/// </summary>
public readonly struct CacheStats : ICacheStats, IEquatable<CacheStats>
{
    private static readonly CacheStats _empty = new(0, 0, 0, 0, 0, 0, 0);

    private readonly long _hitCount;
    private readonly long _missCount;
    private readonly long _loadSuccessCount;
    private readonly long _loadFailureCount;
    private readonly long _totalLoadTime;
    private readonly long _evictionCount;
    private readonly long _evictionWeight;

    private CacheStats(long hitCount, long missCount, long loadSuccessCount, long loadFailureCount,
        long totalLoadTime, long evictionCount, long evictionWeight)
    {
        if (hitCount < 0 || missCount < 0 || loadSuccessCount < 0 || loadFailureCount < 0
            || totalLoadTime < 0 || evictionCount < 0 || evictionWeight < 0)
        {
            throw new ArgumentException("Cache statistics values cannot be negative");
        }

        _hitCount = hitCount;
        _missCount = missCount;
        _loadSuccessCount = loadSuccessCount;
        _loadFailureCount = loadFailureCount;
        _totalLoadTime = totalLoadTime;
        _evictionCount = evictionCount;
        _evictionWeight = evictionWeight;
    }

    /// <summary>
    /// Returns a <see cref="CacheStats"/> representing the specified statistics.
    /// </summary>
    public static CacheStats Of(long hitCount, long missCount, long loadSuccessCount,
        long loadFailureCount, long totalLoadTime, long evictionCount, long evictionWeight)
    {
        return new CacheStats(hitCount, missCount, loadSuccessCount,
            loadFailureCount, totalLoadTime, evictionCount, evictionWeight);
    }

    /// <summary>
    /// Returns a statistics instance where no cache events have been recorded.
    /// </summary>
    public static CacheStats Empty() => _empty;

    /// <inheritdoc/>
    public long RequestCount() => SaturatedAdd(_hitCount, _missCount);

    /// <inheritdoc/>
    public long HitCount() => _hitCount;

    /// <inheritdoc/>
    public double HitRate()
    {
        long requestCount = RequestCount();
        return requestCount == 0 ? 1.0 : (double)_hitCount / requestCount;
    }

    /// <inheritdoc/>
    public long MissCount() => _missCount;

    /// <inheritdoc/>
    public double MissRate()
    {
        long requestCount = RequestCount();
        return requestCount == 0 ? 0.0 : (double)_missCount / requestCount;
    }

    /// <inheritdoc/>
    public long LoadCount() => SaturatedAdd(_loadSuccessCount, _loadFailureCount);

    /// <inheritdoc/>
    public long LoadSuccessCount() => _loadSuccessCount;

    /// <inheritdoc/>
    public long LoadFailureCount() => _loadFailureCount;

    /// <inheritdoc/>
    public double LoadFailureRate()
    {
        long totalLoadCount = SaturatedAdd(_loadSuccessCount, _loadFailureCount);
        return totalLoadCount == 0 ? 0.0 : (double)_loadFailureCount / totalLoadCount;
    }

    /// <inheritdoc/>
    public long TotalLoadTime() => _totalLoadTime;

    /// <inheritdoc/>
    public double AverageLoadPenalty()
    {
        long totalLoadCount = SaturatedAdd(_loadSuccessCount, _loadFailureCount);
        return totalLoadCount == 0 ? 0.0 : (double)_totalLoadTime / totalLoadCount;
    }

    /// <inheritdoc/>
    public long EvictionCount() => _evictionCount;

    /// <inheritdoc/>
    public long EvictionWeight() => _evictionWeight;

    /// <summary>
    /// Returns a new <see cref="CacheStats"/> representing the difference between this 
    /// instance and <paramref name="other"/>. Negative values will be rounded up to zero.
    /// </summary>
    public CacheStats Minus(CacheStats other)
    {
        return Of(
            Math.Max(0L, _hitCount - other._hitCount),
            Math.Max(0L, _missCount - other._missCount),
            Math.Max(0L, _loadSuccessCount - other._loadSuccessCount),
            Math.Max(0L, _loadFailureCount - other._loadFailureCount),
            Math.Max(0L, _totalLoadTime - other._totalLoadTime),
            Math.Max(0L, _evictionCount - other._evictionCount),
            Math.Max(0L, _evictionWeight - other._evictionWeight));
    }

    /// <summary>
    /// Returns a new <see cref="CacheStats"/> representing the sum of this instance 
    /// and <paramref name="other"/>.
    /// </summary>
    public CacheStats Plus(CacheStats other)
    {
        return Of(
            SaturatedAdd(_hitCount, other._hitCount),
            SaturatedAdd(_missCount, other._missCount),
            SaturatedAdd(_loadSuccessCount, other._loadSuccessCount),
            SaturatedAdd(_loadFailureCount, other._loadFailureCount),
            SaturatedAdd(_totalLoadTime, other._totalLoadTime),
            SaturatedAdd(_evictionCount, other._evictionCount),
            SaturatedAdd(_evictionWeight, other._evictionWeight));
    }

    /// <summary>
    /// Returns the sum of <paramref name="a"/> and <paramref name="b"/> unless it would 
    /// overflow or underflow, in which case <see cref="long.MaxValue"/> or 
    /// <see cref="long.MinValue"/> is returned, respectively.
    /// </summary>
    private static long SaturatedAdd(long a, long b)
    {
        long naiveSum = a + b;
        if (((a ^ b) < 0) | ((a ^ naiveSum) >= 0))
        {
            // If a and b have different signs or a has the same sign as the result then there was no
            // overflow, return.
            return naiveSum;
        }
        // we did over/under flow, if the sign is negative we should return MAX otherwise MIN
        return long.MaxValue + (long)(((ulong)naiveSum >> (sizeof(long) * 8 - 1)) ^ 1);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_hitCount, _missCount, _loadSuccessCount,
            _loadFailureCount, _totalLoadTime, _evictionCount, _evictionWeight);
    }

    public override bool Equals(object? obj)
    {
        return obj is CacheStats other && Equals(other);
    }

    public bool Equals(CacheStats other)
    {
        return _hitCount == other._hitCount
            && _missCount == other._missCount
            && _loadSuccessCount == other._loadSuccessCount
            && _loadFailureCount == other._loadFailureCount
            && _totalLoadTime == other._totalLoadTime
            && _evictionCount == other._evictionCount
            && _evictionWeight == other._evictionWeight;
    }

    public static bool operator ==(CacheStats left, CacheStats right) => left.Equals(right);

    public static bool operator !=(CacheStats left, CacheStats right) => !left.Equals(right);

    public override string ToString()
    {
        return $"CacheStats{{" +
            $"hitCount={_hitCount}, " +
            $"missCount={_missCount}, " +
            $"loadSuccessCount={_loadSuccessCount}, " +
            $"loadFailureCount={_loadFailureCount}, " +
            $"totalLoadTime={_totalLoadTime}, " +
            $"evictionCount={_evictionCount}, " +
            $"evictionWeight={_evictionWeight}" +
            $"}}";
    }
}

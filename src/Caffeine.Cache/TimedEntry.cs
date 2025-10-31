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

namespace Caffeine.Cache;

/// <summary>
/// A cache entry that tracks timestamps for time-based expiration.
/// </summary>
internal sealed class TimedEntry<V>
{
    public V Value { get; }
    public long WriteTimeTicks { get; private set; }
    public long AccessTimeTicks { get; private set; }

    public TimedEntry(V value, long currentTicks)
    {
        Value = value;
        WriteTimeTicks = currentTicks;
        AccessTimeTicks = currentTicks;
    }

    public void UpdateAccessTime(long currentTicks)
    {
        AccessTimeTicks = currentTicks;
    }

    public void UpdateWriteTime(long currentTicks)
    {
        WriteTimeTicks = currentTicks;
        AccessTimeTicks = currentTicks;
    }

    public bool IsExpiredAfterWrite(long currentTicks, long expireAfterWriteNanos)
    {
        if (expireAfterWriteNanos < 0)
            return false;

        long elapsedNanos = (currentTicks - WriteTimeTicks) * 100; // Convert ticks to nanos
        return elapsedNanos >= expireAfterWriteNanos;
    }

    public bool IsExpiredAfterAccess(long currentTicks, long expireAfterAccessNanos)
    {
        if (expireAfterAccessNanos < 0)
            return false;

        long elapsedNanos = (currentTicks - AccessTimeTicks) * 100; // Convert ticks to nanos
        return elapsedNanos >= expireAfterAccessNanos;
    }
}

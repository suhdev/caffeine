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
/// The reason why a cached entry was removed.
/// </summary>
public enum RemovalCause
{
    /// <summary>
    /// The entry was manually removed by the user. This can result from the user invoking
    /// Invalidate, InvalidateAll, or Put (when replacing an existing value).
    /// </summary>
    Explicit,

    /// <summary>
    /// The entry itself was not actually removed, but its value was replaced by the user.
    /// This can result from the user invoking Put or PutAll.
    /// </summary>
    Replaced,

    /// <summary>
    /// The entry was removed automatically because its key or value was garbage-collected.
    /// This can occur when using weak or soft references (not yet implemented).
    /// </summary>
    Collected,

    /// <summary>
    /// The entry's expiration timestamp has passed. This can occur when using
    /// ExpireAfterWrite or ExpireAfterAccess.
    /// </summary>
    Expired,

    /// <summary>
    /// The entry was evicted due to size constraints. This can occur when using MaximumSize.
    /// </summary>
    Size
}

/// <summary>
/// Extension methods for <see cref="RemovalCause"/>.
/// </summary>
public static class RemovalCauseExtensions
{
    /// <summary>
    /// Returns true if there was an automatic removal due to eviction (the cause is neither
    /// Explicit nor Replaced).
    /// </summary>
    /// <param name="cause">The removal cause</param>
    /// <returns>true if the entry was automatically removed due to eviction</returns>
    public static bool WasEvicted(this RemovalCause cause)
    {
        return cause != RemovalCause.Explicit && cause != RemovalCause.Replaced;
    }
}

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
/// A listener that receives notification when an entry is removed from a cache. The removal
/// resulting in notification could have occurred to an entry being manually removed or replaced, or
/// due to eviction resulting from timed expiration, exceeding a maximum size, or garbage collection.
/// <para>
/// An instance may be called concurrently by multiple threads to process different entries.
/// Implementations should avoid performing blocking calls or synchronizing on shared resources.
/// </para>
/// <para>
/// Warning: any exception thrown by the listener will not be propagated to callers of the cache.
/// </para>
/// </summary>
/// <typeparam name="K">The type of keys</typeparam>
/// <typeparam name="V">The type of values</typeparam>
/// <param name="key">The key that was removed, or null if collected</param>
/// <param name="value">The value that was removed, or null if collected</param>
/// <param name="cause">The reason for which the entry was removed</param>
public delegate void RemovalListener<K, V>(K? key, V? value, RemovalCause cause);

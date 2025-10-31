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
/// A cache loader implementation that wraps a simple function.
/// </summary>
internal sealed class FunctionCacheLoader<K, V> : ICacheLoader<K, V> where K : notnull
{
    private readonly Func<K, V?> _function;

    public FunctionCacheLoader(Func<K, V?> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        _function = function;
    }

    public V? Load(K key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _function(key);
    }
}

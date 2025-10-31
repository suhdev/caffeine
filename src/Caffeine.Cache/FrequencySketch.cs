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
/// A probabilistic data structure for estimating the frequency of events.
/// Uses Count-Min Sketch with 4-bit counters for space efficiency.
/// </summary>
internal sealed class FrequencySketch<K> where K : notnull
{
    private readonly int[] _table;
    private readonly int _tableMask;
    private readonly long _sampleSize;
    private long _size;
    
    private const int MaxCounterValue = 15; // 4-bit counter max
    
    public FrequencySketch(long maximumSize)
    {
        // Use 10x the maximum size for good accuracy
        int tableSize = Math.Max(16, NearestPowerOfTwo((int)Math.Min(maximumSize * 10, int.MaxValue / 2)));
        _table = new int[tableSize];
        _tableMask = tableSize - 1;
        _sampleSize = Math.Max(10, maximumSize);
    }
    
    private static int NearestPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value && result < (1 << 30))
        {
            result <<= 1;
        }
        return result;
    }
    
    /// <summary>
    /// Records an occurrence of the specified key.
    /// </summary>
    public void Increment(K key)
    {
        int hash = key.GetHashCode();
        
        // Use 4 different hash functions (via bit manipulation)
        int hash1 = Spread(hash);
        int hash2 = Spread(hash1);
        int hash3 = Spread(hash2);
        int hash4 = Spread(hash3);
        
        // Increment 4 counters (4-bit each, packed into int)
        IncrementAt(hash1 & _tableMask, 0);
        IncrementAt(hash2 & _tableMask, 1);
        IncrementAt(hash3 & _tableMask, 2);
        IncrementAt(hash4 & _tableMask, 3);
        
        // Periodically reset to prevent saturation
        if (++_size >= _sampleSize)
        {
            Reset();
        }
    }
    
    /// <summary>
    /// Gets the estimated frequency of the specified key.
    /// Returns the minimum of the 4 counter values.
    /// </summary>
    public int Frequency(K key)
    {
        int hash = key.GetHashCode();
        
        int hash1 = Spread(hash);
        int hash2 = Spread(hash1);
        int hash3 = Spread(hash2);
        int hash4 = Spread(hash3);
        
        int freq1 = GetCounterAt(hash1 & _tableMask, 0);
        int freq2 = GetCounterAt(hash2 & _tableMask, 1);
        int freq3 = GetCounterAt(hash3 & _tableMask, 2);
        int freq4 = GetCounterAt(hash4 & _tableMask, 3);
        
        return Math.Min(Math.Min(freq1, freq2), Math.Min(freq3, freq4));
    }
    
    /// <summary>
    /// Increments the counter at the specified index and offset.
    /// Each int holds 4 counters of 4 bits each.
    /// </summary>
    private void IncrementAt(int index, int offset)
    {
        int shift = offset * 8; // 8 bits per counter slot (we use 4, leaving 4 for safety)
        int mask = 0xF << shift;
        
        int current = _table[index];
        int counter = (current >> shift) & 0xF;
        
        if (counter < MaxCounterValue)
        {
            _table[index] = (current & ~mask) | ((counter + 1) << shift);
        }
    }
    
    /// <summary>
    /// Gets the counter value at the specified index and offset.
    /// </summary>
    private int GetCounterAt(int index, int offset)
    {
        int shift = offset * 8;
        return (_table[index] >> shift) & 0xF;
    }
    
    /// <summary>
    /// Resets all counters by halving them (aging).
    /// </summary>
    private void Reset()
    {
        for (int i = 0; i < _table.Length; i++)
        {
            int value = _table[i];
            // Halve each 4-bit counter
            _table[i] = ((value >> 1) & 0x7777_7777);
        }
        _size = _size >> 1;
    }
    
    /// <summary>
    /// Spreads bits for better hash distribution.
    /// </summary>
    private static int Spread(int h)
    {
        h ^= h >> 16;
        h = unchecked(h * (int)0x85ebca6b);
        h ^= h >> 13;
        h = unchecked(h * (int)0xc2b2ae35);
        h ^= h >> 16;
        return h;
    }
}

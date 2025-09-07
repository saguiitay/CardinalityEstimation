// /*  
//     See https://github.com/saguiitay/CardinalityEstimation.
//     The MIT License (MIT)
// 
//     Copyright (c) 2015 Microsoft
// 
//     Permission is hereby granted, free of charge, to any person obtaining a copy
//     of this software and associated documentation files (the "Software"), to deal
//     in the Software without restriction, including without limitation the rights
//     to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//     copies of the Software, and to permit persons to whom the Software is
//     furnished to do so, subject to the following conditions:
// 
//     The above copyright notice and this permission notice shall be included in all
//     copies or substantial portions of the Software.
// 
//     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//     IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//     FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//     AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//     LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//     OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//     SOFTWARE.
// */

namespace CardinalityEstimation.Hash
{
    using System;
    using System.Collections.Concurrent;
    using Murmur;

    /// <summary>
    /// Provides Murmur3 128-bit hash functionality with object pooling for performance optimization.
    /// This class is used as an alternative hash function for cardinality estimation when 
    /// a different hash algorithm is preferred over the default XxHash128.
    /// </summary>
    /// <remarks>
    /// <para>The Murmur3 algorithm is a non-cryptographic hash function suitable for general hash-based 
    /// lookup. It provides good distribution properties required for accurate cardinality estimation.</para>
    /// <para>This implementation uses object pooling to reuse Murmur128 instances and reduce 
    /// garbage collection pressure in high-throughput scenarios.</para>
    /// <para>This class is thread-safe due to the use of ConcurrentStack for the object pool.</para>
    /// </remarks>
    /// <seealso href="https://github.com/aappleby/smhasher/blob/master/src/MurmurHash3.cpp"/>
    public class Murmur3
    {
        /// <summary>
        /// Thread-safe pool of Murmur128 hash function instances for reuse to reduce allocation overhead.
        /// </summary>
        private static readonly ConcurrentStack<Murmur128> pool = new ConcurrentStack<Murmur128>();

        /// <summary>
        /// Computes a 64-bit hash code for the specified byte array using the Murmur3 128-bit algorithm.
        /// </summary>
        /// <param name="bytes">The byte array to hash</param>
        /// <returns>
        /// A 64-bit hash code derived from the lower 64 bits of the Murmur3 128-bit hash.
        /// This provides good distribution properties suitable for cardinality estimation.
        /// </returns>
        /// <remarks>
        /// <para>This method uses object pooling to reuse Murmur128 instances for better performance.
        /// The hash function instances are automatically returned to the pool after use.</para>
        /// <para>The method returns only the lower 64 bits of the 128-bit Murmur3 hash, which provides
        /// sufficient entropy for cardinality estimation purposes.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null</exception>
        public static ulong GetHashCode(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            Murmur128 murmurHash;
            if (!pool.TryPop(out murmurHash))
            {
                murmurHash = MurmurHash.Create128(managed: true, preference: AlgorithmPreference.X64);
            }

            byte[] result = murmurHash.ComputeHash(bytes);
            pool.Push(murmurHash);
            return BitConverter.ToUInt64(result, 0);
        }
    }
}
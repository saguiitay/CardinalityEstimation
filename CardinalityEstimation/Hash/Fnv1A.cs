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

    /// <summary>
    /// Provides FNV-1a (Fowler-Noll-Vo) 64-bit hash functionality for byte arrays.
    /// This hash function is provided for legacy support and compatibility with older implementations.
    /// </summary>
    /// <remarks>
    /// <para>The FNV-1a hash algorithm is a non-cryptographic hash function known for its simplicity 
    /// and good distribution properties for general-purpose hashing scenarios.</para>
    /// <para>This implementation is primarily kept for backward compatibility. For new applications,
    /// consider using the default XxHash128 or Murmur3 hash functions which may provide better
    /// performance and distribution characteristics.</para>
    /// <para>The algorithm processes each byte of the input sequentially, making it cache-friendly
    /// but potentially slower than more modern hash functions on larger inputs.</para>
    /// </remarks>
    /// <seealso href="http://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function"/>
    /// <seealso href="http://www.isthe.com/chongo/src/fnv/hash_64a.c"/>
    public class Fnv1A
    {
        /// <summary>
        /// Computes the 64-bit FNV-1a hash of the given byte array.
        /// </summary>
        /// <param name="bytes">The byte array to compute the hash for</param>
        /// <returns>
        /// The 64-bit FNV-1a hash value. This hash provides reasonable distribution properties
        /// suitable for use in hash tables and cardinality estimation, though modern alternatives
        /// may offer better performance.
        /// </returns>
        /// <remarks>
        /// <para>The FNV-1a algorithm works by:</para>
        /// <list type="number">
        /// <item>Starting with the FNV-1a 64-bit offset basis (14695981039346656037)</item>
        /// <item>For each byte: XOR the hash with the byte value, then multiply by the FNV prime (0x100000001b3)</item>
        /// </list>
        /// <para>This process ensures good avalanche properties where small changes in input 
        /// produce large changes in output.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null</exception>
        /// <seealso href="http://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function"/>
        /// <seealso href="http://www.isthe.com/chongo/src/fnv/hash_64a.c"/>
        public static ulong GetHashCode(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            const ulong fnv1A64Init = 14695981039346656037;
            const ulong fnv64Prime = 0x100000001b3;
            ulong hash = fnv1A64Init;

            foreach (byte b in bytes)
            {
                /* xor the bottom with the current octet */
                hash ^= b;
                /* multiply by the 64 bit FNV magic prime mod 2^64 */
                hash *= fnv64Prime;
            }

            return hash;
        }
    }
}
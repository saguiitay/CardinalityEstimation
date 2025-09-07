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

namespace CardinalityEstimation
{
    using System.Collections.Generic;
    using Hash;

    /// <summary>
    /// Represents state of a <see cref="CardinalityEstimator" /> for serialization purposes.
    /// This class contains all the data needed to reconstruct a CardinalityEstimator instance.
    /// </summary>
    /// <remarks>
    /// <para>This class is used by <see cref="CardinalityEstimatorSerializer" /> to persist and restore
    /// CardinalityEstimator instances. It captures the complete internal state including the accuracy
    /// parameters, counting data, and representation type.</para>
    /// <para>The state includes support for all three counting modes: direct counting for small sets,
    /// sparse representation for medium sets, and dense representation for large sets.</para>
    /// </remarks>
    /// <seealso cref="CardinalityEstimator"/>
    /// <seealso cref="CardinalityEstimatorSerializer"/>
    internal class CardinalityEstimatorState
    {
        /// <summary>
        /// Gets or sets the number of bits for indexing HLL sub-streams.
        /// The number of estimator buckets is 2^BitsPerIndex.
        /// </summary>
        /// <value>
        /// A value between 4 and 16 that determines the accuracy and memory consumption of the estimator.
        /// Higher values provide better accuracy but use more memory.
        /// </value>
        public int BitsPerIndex;
        
        /// <summary>
        /// Gets or sets the direct count hash set used for small cardinalities.
        /// Contains the exact hash values of elements when direct counting is active.
        /// </summary>
        /// <value>
        /// A HashSet containing hash codes of unique elements, or null if direct counting is not used
        /// or if the estimator has switched to probabilistic counting.
        /// </value>
        public HashSet<ulong> DirectCount;
        
        /// <summary>
        /// Gets or sets a value indicating whether the sparse representation is currently being used.
        /// </summary>
        /// <value>
        /// <c>true</c> if the estimator is using sparse representation (dictionary-based);
        /// <c>false</c> if using dense representation (array-based).
        /// </value>
        public bool IsSparse;
        
        /// <summary>
        /// Gets or sets the lookup table for the dense representation of HLL buckets.
        /// This is a byte array where each position represents a bucket and contains the maximum
        /// rank (sigma value) seen for that bucket.
        /// </summary>
        /// <value>
        /// A byte array of size 2^BitsPerIndex, or null if sparse representation is being used.
        /// Each byte value represents the maximum rank observed in that bucket.
        /// </value>
        public byte[] LookupDense;
        
        /// <summary>
        /// Gets or sets the lookup dictionary for the sparse representation of HLL buckets.
        /// This dictionary only stores non-zero buckets to save memory when cardinality is not too large.
        /// </summary>
        /// <value>
        /// A dictionary mapping bucket indices (ushort) to their maximum rank values (byte),
        /// or null if dense representation is being used. Only contains entries for buckets
        /// that have received at least one element.
        /// </value>
        public IDictionary<ushort, byte> LookupSparse;
        
        /// <summary>
        /// Gets or sets the total number of addition operations performed on the estimator.
        /// This includes duplicate additions and provides a count of all Add() method calls.
        /// </summary>
        /// <value>
        /// The total number of times elements were added to the estimator, including duplicates.
        /// This value is used for tracking and debugging purposes.
        /// </value>
        public ulong CountAdditions;
    }
}
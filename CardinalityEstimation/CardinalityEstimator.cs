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
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Hash;

    /// <summary>
    /// Represents a hash function delegate that computes a hash code for a byte array
    /// </summary>
    /// <param name="bytes">The byte array to hash</param>
    /// <returns>A 64-bit hash code for the input byte array</returns>
    public delegate ulong GetHashCodeDelegate(byte[] bytes);
    
    /// <summary>
    /// Represents a hash function delegate that computes a hash code for a read-only span of bytes
    /// </summary>
    /// <param name="bytes">The read-only span of bytes to hash</param>
    /// <returns>A 64-bit hash code for the input byte span</returns>
    public delegate ulong GetHashCodeSpanDelegate(ReadOnlySpan<byte> bytes);
    
    /// <summary>
    /// A cardinality estimator for sets of some common types, which uses a HashSet for small cardinalities,
    /// LinearCounting for medium-range cardinalities and HyperLogLog for large cardinalities.  Based off of the following:
    /// 1. Flajolet et al., "HyperLogLog: the analysis of a near-optimal cardinality estimation algorithm",
    ///    DMTCS proc. AH 2007, <see cref="http://algo.inria.fr/flajolet/Publications/FlFuGaMe07.pdf" />
    /// 2. Heule, Nunkesser and Hall 2013, "HyperLogLog in Practice: Algorithmic Engineering of a State of The Art Cardinality Estimation
    ///    Algorithm",
    /// <see cref="http://static.googleusercontent.com/external_content/untrusted_dlcp/research.google.com/en/us/pubs/archive/40671.pdf" />
    /// </summary>
    /// <remarks>
    /// <para>1. This implementation is not thread-safe. For thread-safe operations, use <see cref="ConcurrentCardinalityEstimator"/>.</para>
    /// <para>2. By default, it uses the 128-bit XxHash128 hash function from .NET 9+. 
    ///    For custom hash functions, provide your own delegate to the constructor.</para>
    /// <para>3. Estimation is perfect up to 100 elements, then approximate</para>
    /// <para>4. The estimator automatically switches between three different counting strategies based on the number of elements:
    /// - Direct counting (exact) for up to 100 elements
    /// - Sparse representation for medium cardinalities
    /// - Dense representation for large cardinalities</para>
    /// </remarks>
    [Serializable]
    public class CardinalityEstimator : ICardinalityEstimator<string>, ICardinalityEstimator<int>, ICardinalityEstimator<uint>,
        ICardinalityEstimator<long>, ICardinalityEstimator<ulong>, ICardinalityEstimator<float>, ICardinalityEstimator<double>,
        ICardinalityEstimator<byte[]>, ICardinalityEstimatorMemory,
        IEquatable<CardinalityEstimator>
    {

        #region Private consts
        /// <summary>
        /// Max number of elements to hold in the direct representation
        /// </summary>
        private const int DirectCounterMaxElements = 100;
        #endregion

        #region Private fields
        /// <summary>
        /// Number of bits for indexing HLL sub-streams - the number of estimators is 2^bitsPerIndex
        /// </summary>
        private readonly int bitsPerIndex;

        /// <summary>
        /// Number of bits to compute the HLL estimate on
        /// </summary>
        private readonly byte bitsForHll;

        /// <summary>
        /// HLL lookup table size (2^bitsPerIndex)
        /// </summary>
        private readonly int m;

        /// <summary>
        /// Fixed bias correction factor for the HyperLogLog algorithm
        /// </summary>
        private readonly double alphaM;

        /// <summary>
        /// Threshold determining whether to use LinearCounting or HyperLogLog based on an initial estimate
        /// </summary>
        private readonly double subAlgorithmSelectionThreshold;

        /// <summary>
        /// Lookup table for the dense representation of HLL buckets
        /// </summary>
        private byte[] lookupDense;

        /// <summary>
        /// Lookup dictionary for the sparse representation of HLL buckets
        /// </summary>
        private IDictionary<ushort, byte> lookupSparse;

        /// <summary>
        /// Max number of elements to hold in the sparse representation before switching to dense
        /// </summary>
        private readonly int sparseMaxElements;

        /// <summary>
        /// Indicates that the sparse representation is currently being used
        /// </summary>
        private bool isSparse;

        /// <summary>
        /// Set for direct counting of elements for perfect accuracy on small sets
        /// </summary>
        private HashSet<ulong> directCount;

        /// <summary>
        /// Hash function used to hash input elements to 64-bit values
        /// </summary>
        [NonSerialized]
        private GetHashCodeDelegate hashFunction;
        
        /// <summary>
        /// Hash function used to hash input spans to 64-bit values for zero-allocation scenarios
        /// </summary>
        [NonSerialized]
        private GetHashCodeSpanDelegate hashFunctionSpan;

        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new instance of CardinalityEstimator
        /// </summary>
        /// <param name="hashFunction">
        /// Hash function to use for hashing input elements. If null, defaults to XxHash128 from .NET.
        /// The hash function should provide good distribution properties for accurate estimates.
        /// </param>
        /// <param name="b">
        /// Number of bits determining accuracy and memory consumption, in the range [4, 16] (higher = greater accuracy and memory usage).
        /// For large cardinalities, the standard error is 1.04 * 2^(-b/2), and the memory consumption is bounded by 2^b kilobytes.
        /// The default value of 14 typically yields 3% error or less across the entire range of cardinalities (usually much less),
        /// and uses up to ~16kB of memory.  b=4 yields less than ~100% error and uses less than 1kB. b=16 uses up to ~64kB and usually yields 1%
        /// error or less
        /// </param>
        /// <param name="useDirectCounting">
        /// True if direct count should be used for up to <see cref="DirectCounterMaxElements"/> elements.
        /// False if direct count should be avoided and use always estimation, even for low cardinalities.
        /// Direct counting provides perfect accuracy for small sets.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="b"/> is not in the range [4, 16].
        /// </exception>
        public CardinalityEstimator(GetHashCodeDelegate hashFunction = null, int b = 14, bool useDirectCounting = true)
            : this(hashFunction, CreateEmptyState(b, useDirectCounting))
        { }

        /// <summary>
        /// Copy constructor that creates a new instance as a copy of another estimator
        /// </summary>
        /// <param name="other">The CardinalityEstimator instance to copy</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null</exception>
        public CardinalityEstimator(CardinalityEstimator other)
        {
            bitsPerIndex = other.bitsPerIndex;
            bitsForHll = other.bitsForHll;
            m = other.m;
            alphaM = other.alphaM;
            subAlgorithmSelectionThreshold = other.subAlgorithmSelectionThreshold;
            if (other.lookupDense != null)
            {
                lookupDense = new byte[other.lookupDense.Length];
                Array.Copy(other.lookupDense, lookupDense, other.lookupDense.Length);
            }

            if (other.lookupSparse != null)
            {
                lookupSparse = new Dictionary<ushort, byte>(other.lookupSparse);
            }
            sparseMaxElements = other.sparseMaxElements;
            isSparse = other.isSparse;
            if (other.directCount != null)
            {
                directCount = new HashSet<ulong>(other.directCount, other.directCount.Comparer);
            }
            hashFunction = other.hashFunction;
        }

        /// <summary>
        /// Creates a CardinalityEstimator with the given hash function and state
        /// </summary>
        /// <param name="hashFunction">Hash function to use for element hashing</param>
        /// <param name="state">The state to initialize the estimator with</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null</exception>
        internal CardinalityEstimator(GetHashCodeDelegate hashFunction, CardinalityEstimatorState state)
            : this(state)
        {
            // Init the hash function
            this.hashFunction = hashFunction;
            if (this.hashFunction == null)
            {
                this.hashFunction = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
                this.hashFunctionSpan = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
            }
            else
            {
                this.hashFunctionSpan = (x) => hashFunction(x.ToArray());
            }

        }

        /// <summary>
        /// Creates a CardinalityEstimator with the given span hash function and state
        /// </summary>
        /// <param name="hashFunctionSpan">Span hash function to use for element hashing</param>
        /// <param name="state">The state to initialize the estimator with</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null</exception>
        internal CardinalityEstimator(GetHashCodeSpanDelegate hashFunctionSpan, CardinalityEstimatorState state)
            : this(state)
        {
            // Init the hash function
            this.hashFunctionSpan = hashFunctionSpan;
            if (this.hashFunctionSpan == null)
            {
                this.hashFunction = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
                this.hashFunctionSpan = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
            }
            else
            {
                this.hashFunction = (x) => hashFunctionSpan(x);
            }
        }

        /// <summary>
        /// Creates a CardinalityEstimator from serialized state
        /// </summary>
        /// <param name="state">The state to restore the estimator from</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null</exception>
        internal CardinalityEstimator(CardinalityEstimatorState state)
        {
            bitsPerIndex = state.BitsPerIndex;
            bitsForHll = (byte)(64 - bitsPerIndex);
            m = (int) Math.Pow(2, bitsPerIndex);
            alphaM = GetAlphaM(m);
            subAlgorithmSelectionThreshold = GetSubAlgorithmSelectionThreshold(bitsPerIndex);

            // Init the direct count
            directCount = state.DirectCount != null ? new HashSet<ulong>(state.DirectCount) : null;

            // Init the sparse representation
            isSparse = state.IsSparse;
            lookupSparse = state.LookupSparse != null ? new Dictionary<ushort, byte>(state.LookupSparse) : null;
            lookupDense = state.LookupDense;
            CountAdditions = state.CountAdditions;

            // Each element in the sparse representation takes 15 bytes, and there is some constant overhead
            sparseMaxElements = Math.Max(0, (m / 15) - 10);
            // If necessary, switch to the dense representation
            if (sparseMaxElements <= 0)
            {
                SwitchToDenseRepresentation();
            }

            // if DirectCount is not null, populate the HLL lookup with its elements.  This allows serialization to include only directCount
            if (directCount != null)
            {
                // since we are re-initializing the object, we need to reset isSparse to true and sparse lookup
                isSparse = true;
                lookupSparse = new Dictionary<ushort, byte>();
                foreach (ulong element in directCount)
                {
                    AddElementHash(element);
                }
            }
            else
            {
                directCount = null;
            }
        }
#endregion

        #region Public properties
        /// <summary>
        /// Gets the total number of Add operations that have been performed on this estimator,
        /// including duplicate elements
        /// </summary>
        /// <value>The count of all addition operations performed</value>
        public ulong CountAdditions { get; private set; }
        #endregion

        #region Public methods
        /// <summary>
        /// Add an element of type <see cref="string"/>
        /// </summary>
        /// <param name="element">The string element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null</exception>
        public bool Add(string element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
                
            ulong hashCode = hashFunction(Encoding.UTF8.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="int"/>
        /// </summary>
        /// <param name="element">The integer element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(int element)
        {
            ulong hashCode = hashFunction(BitConverter.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="uint"/>
        /// </summary>
        /// <param name="element">The unsigned integer element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(uint element)
        {
            ulong hashCode = hashFunction(BitConverter.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="long"/>
        /// </summary>
        /// <param name="element">The long integer element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(long element)
        {
            ulong hashCode = hashFunction(BitConverter.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="ulong"/>
        /// </summary>
        /// <param name="element">The unsigned long integer element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(ulong element)
        {
            ulong hashCode = hashFunction(BitConverter.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="float"/>
        /// </summary>
        /// <param name="element">The float element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(float element)
        {
            ulong hashCode = hashFunction(BitConverter.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="double"/>
        /// </summary>
        /// <param name="element">The double element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(double element)
        {
            ulong hashCode = hashFunction(BitConverter.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="byte[]"/>
        /// </summary>
        /// <param name="element">The byte array element to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null</exception>
        public bool Add(byte[] element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
                
            ulong hashCode = hashFunction(element);
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add data from a <see cref="Span{T}"/> of bytes with zero allocations
        /// </summary>
        /// <param name="element">The span of bytes to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(Span<byte> element)
        {
            ulong hashCode = hashFunctionSpan(element);
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add data from a <see cref="ReadOnlySpan{T}"/> of bytes with zero allocations
        /// </summary>
        /// <param name="element">The read-only span of bytes to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(ReadOnlySpan<byte> element)
        {
            ulong hashCode = hashFunctionSpan(element);
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add data from a <see cref="Memory{T}"/> of bytes with optimized allocation patterns
        /// </summary>
        /// <param name="element">The memory of bytes to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(Memory<byte> element)
        {
            ulong hashCode = hashFunctionSpan(element.Span);
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add data from a <see cref="ReadOnlyMemory{T}"/> of bytes with optimized allocation patterns
        /// </summary>
        /// <param name="element">The read-only memory of bytes to add to the set</param>
        /// <returns>True if the estimator's state was modified, false otherwise</returns>
        public bool Add(ReadOnlyMemory<byte> element)
        {
            ulong hashCode = hashFunctionSpan(element.Span);
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Returns the estimated number of items in the estimator
        /// </summary>
        /// <returns>
        /// The estimated count of unique elements. If direct counting is enabled and fewer than
        /// <see cref="DirectCounterMaxElements"/> elements have been added, returns the exact count.
        /// Otherwise, returns an approximation using HyperLogLog or LinearCounting algorithms.
        /// </returns>
        /// <remarks>
        /// The estimation algorithm automatically selects between HyperLogLog and LinearCounting
        /// based on the estimated cardinality to provide the most accurate result for the given range.
        /// </remarks>
        public ulong Count()
        {
            // If only a few elements have been seen, return the exact count
            if (directCount != null)
            {
                return (ulong) directCount.Count;
            }

            double zInverse = 0;
            double v = 0;

            if (isSparse)
            {
                // calc c and Z's inverse
                foreach (KeyValuePair<ushort, byte> kvp in lookupSparse)
                {
                    byte sigma = kvp.Value;
                    zInverse += Math.Pow(2, -sigma);
                }
                v = m - lookupSparse.Count;
                zInverse += m - lookupSparse.Count;
            }
            else
            {
                // calc c and Z's inverse
                for (var i = 0; i < m; i++)
                {
                    byte sigma = lookupDense[i];
                    zInverse += Math.Pow(2, -sigma);
                    if (sigma == 0)
                    {
                        v++;
                    }
                }
            }

            double e = alphaM*m*m/zInverse;
            if (e <= 5.0*m)
            {
                e = BiasCorrection.CorrectBias(e, bitsPerIndex);
            }

            double h;
            if (v > 0)
            {
                // LinearCounting estimate
                h = m*Math.Log(m/v);
            }
            else
            {
                h = e;
            }

            if (h <= subAlgorithmSelectionThreshold)
            {
                return (ulong) Math.Round(h);
            }
            return (ulong) Math.Round(e);
        }

        /// <summary>
        /// Merges the given <paramref name="other" /> CardinalityEstimator instance into this one
        /// </summary>
        /// <param name="other">Another instance of CardinalityEstimator to merge</param>
        /// <remarks>
        /// After merging, this estimator will provide a cardinality estimate equivalent to
        /// the union of both sets. The merge operation is commutative - the order doesn't matter.
        /// Both estimators must have the same accuracy parameter (bitsPerIndex).
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="other"/> has different accuracy/map sizes than this estimator
        /// </exception>
        public void Merge(CardinalityEstimator other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (other.m != m)
            {
                throw new ArgumentOutOfRangeException(nameof(other),
                    "Cannot merge CardinalityEstimator instances with different accuracy/map sizes");
            }

            CountAdditions += other.CountAdditions;
            if (isSparse && other.isSparse)
            {
                // Merge two sparse instances
                foreach (KeyValuePair<ushort, byte> kvp in other.lookupSparse)
                {
                    ushort index = kvp.Key;
                    byte otherRank = kvp.Value;
                    lookupSparse.TryGetValue(index, out byte thisRank);
                    lookupSparse[index] = Math.Max(thisRank, otherRank);
                }

                // Switch to dense if necessary
                if (lookupSparse.Count > sparseMaxElements)
                {
                    SwitchToDenseRepresentation();
                }
            }
            else
            {
                // Make sure this (target) instance is dense, then merge
                SwitchToDenseRepresentation();
                if (other.isSparse)
                {
                    foreach (KeyValuePair<ushort, byte> kvp in other.lookupSparse)
                    {
                        ushort index = kvp.Key;
                        byte rank = kvp.Value;
                        lookupDense[index] = Math.Max(lookupDense[index], rank);
                    }
                }
                else
                {
                    for (var i = 0; i < m; i++)
                    {
                        lookupDense[i] = Math.Max(lookupDense[i], other.lookupDense[i]);
                    }
                }
            }

            if (other.directCount != null)
            {
                // Other instance is using direct counter. If this instance is also using direct counter, merge them.
                if (directCount != null)
                {
                    directCount.UnionWith(other.directCount);
                    if (directCount.Count > DirectCounterMaxElements)
                    {
                        directCount = null;
                    }
                }
            }
            else
            {
                // Other instance is not using direct counter, make sure this instance doesn't either
                directCount = null;
            }
        }
        #endregion

        /// <summary>
        /// Merges <paramref name="estimators" /> into a new <see cref="CardinalityEstimator" />.
        /// </summary>
        /// <param name="estimators">Instances of <see cref="CardinalityEstimator"/> to merge</param>
        /// <returns>
        /// A new <see cref="CardinalityEstimator" /> if there is at least one non-null <see cref="CardinalityEstimator" /> in
        /// <paramref name="estimators" />; otherwise <see langword="null" />.
        /// </returns>
        /// <remarks>
        /// <para>The <c>b</c> and hash function parameters for the result are taken from the first non-null
        /// <see cref="CardinalityEstimator"/> in <paramref name="estimators"/>. The remaining estimators are assumed to use the same parameters.</para>
        /// <para>All non-null estimators in the collection must have the same accuracy parameters.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when estimators have different accuracy/map sizes
        /// </exception>
        public static CardinalityEstimator Merge(IEnumerable<CardinalityEstimator> estimators)
        {
            if (estimators == null)
            {
                return null;
            }

            CardinalityEstimator result = null;
            foreach (CardinalityEstimator estimator in estimators)
            {
                if (estimator == null)
                {
                    continue;
                }

                if (result == null)
                {
                    result = new CardinalityEstimator(estimator);
                }

                result.Merge(estimator);
            }

            return result;
        }

        #region Private/Internal methods
        /// <summary>
        /// Gets the current state of this estimator for serialization purposes
        /// </summary>
        /// <returns>A <see cref="CardinalityEstimatorState"/> representing the current state</returns>
        internal CardinalityEstimatorState GetState()
        {
            return new CardinalityEstimatorState
            {
                BitsPerIndex = bitsPerIndex,
                DirectCount = directCount,
                IsSparse = isSparse,
                LookupDense = lookupDense,
                LookupSparse = lookupSparse,
                CountAdditions = CountAdditions,
            };
        }

        /// <summary>
        /// Creates state for an empty CardinalityEstimator with DirectCount and LookupSparse empty, LookupDense null.
        /// </summary>
        /// <param name="b">Number of bits determining accuracy and memory consumption</param>
        /// <param name="useDirectCount">
        /// True if direct count should be used for up to <see cref="DirectCounterMaxElements"/> elements.
        /// False if direct count should be avoided and use always estimation, even for low cardinalities.
        /// </param>
        /// <returns>A new empty state for a CardinalityEstimator</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="b"/> is not in the range [4, 16]
        /// </exception>
        private static CardinalityEstimatorState CreateEmptyState(int b, bool useDirectCount)
        {
            if (b < 4 || b > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(b), b, "Accuracy out of range, legal range is 4 <= BitsPerIndex <= 16");
            }

            return new CardinalityEstimatorState
            {
                BitsPerIndex = b,
                DirectCount = useDirectCount ? new HashSet<ulong>() : null,
                IsSparse = true,
                LookupSparse = new Dictionary<ushort, byte>(),
                LookupDense = null,
                CountAdditions = 0,
            };
        }

        /// <summary>
        /// Returns the threshold determining whether to use LinearCounting or HyperLogLog for an estimate. 
        /// Values are from the supplementary material of Heule et al.
        /// </summary>
        /// <param name="bits">Number of bits for the estimator</param>
        /// <returns>The threshold value for algorithm selection</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="bits"/> is not in the supported range
        /// </exception>
        /// <see cref="http://docs.google.com/document/d/1gyjfMHy43U9OWBXxfaeG-3MjGzejW1dlpyMwEYAAWEI/view?fullscreen#heading=h.nd379k1fxnux" />
        private double GetSubAlgorithmSelectionThreshold(int bits)
        {
            switch (bits)
            {
                case 4:
                    return 10;
                case 5:
                    return 20;
                case 6:
                    return 40;
                case 7:
                    return 80;
                case 8:
                    return 220;
                case 9:
                    return 400;
                case 10:
                    return 900;
                case 11:
                    return 1800;
                case 12:
                    return 3100;
                case 13:
                    return 6500;
                case 14:
                    return 11500;
                case 15:
                    return 20000;
                case 16:
                    return 50000;
                case 17:
                    return 120000;
                case 18:
                    return 350000;
            }
            throw new ArgumentOutOfRangeException(nameof(bits), "Unexpected number of bits (should never happen)");
        }

        /// <summary>
        /// Adds an element's hash code to the counted set
        /// </summary>
        /// <param name="hashCode">Hash code of the element to add</param>
        /// <returns>True if the state was modified, false if the element was already represented</returns>
        private bool AddElementHash(ulong hashCode)
        {
            var changed = false;
            if (directCount != null)
            {
                changed = directCount.Add(hashCode);
                if (directCount.Count > DirectCounterMaxElements)
                {
                    directCount = null;
                    changed = true;
                }
            }

            var substream = (ushort)(hashCode >> bitsForHll);
            byte sigma = GetSigma(hashCode, bitsForHll);
            if (isSparse)
            {
                lookupSparse.TryGetValue(substream, out byte prevRank);
                lookupSparse[substream] = Math.Max(prevRank, sigma);
                changed = changed || (prevRank != sigma && lookupSparse[substream] == sigma);
                if (lookupSparse.Count > sparseMaxElements)
                {
                    SwitchToDenseRepresentation();
                    changed = true;
                }
            }
            else
            {
                var prevMax = lookupDense[substream];
                lookupDense[substream] = Math.Max(prevMax, sigma);
                changed = changed || (prevMax != sigma && lookupDense[substream] == sigma);
            }
            return changed;
        }

        /// <summary>
        /// Gets the appropriate value of alpha_M for the given <paramref name="m" /> for bias correction
        /// </summary>
        /// <param name="m">Size of the lookup table (2^bitsPerIndex)</param>
        /// <returns>alpha_M value for bias correction in HyperLogLog algorithm</returns>
        private  double GetAlphaM(int m)
        {
            switch (m)
            {
                case 16:
                    return 0.673;
                case 32:
                    return 0.697;
                case 64:
                    return 0.709;
                default:
                    return 0.7213/(1 + (1.079 / m));
            }
        }

        /// <summary>
        /// Returns the number of leading zeroes in the <paramref name="bitsToCount" /> highest bits of <paramref name="hash" />, plus one.
        /// This is the sigma value used in the HyperLogLog algorithm.
        /// </summary>
        /// <param name="hash">Hash value to calculate the statistic on</param>
        /// <param name="bitsToCount">Number of bits from the hash to consider for counting leading zeros</param>
        /// <returns>The number of leading zeroes in the binary representation of <paramref name="hash" />, plus one</returns>
        /// <remarks>
        /// This method is used to compute the rank (sigma) of a hash value for the HyperLogLog algorithm.
        /// The rank represents the position of the first 1 bit in the binary representation.
        /// </remarks>
        public static byte GetSigma(ulong hash, byte bitsToCount)
        {
            if (hash == 0)
            {
                return (byte)(bitsToCount + 1);
            }

            ulong mask = ((1UL << bitsToCount) - 1);
            int knownZeros = 64 - bitsToCount;

            var masked = hash & mask;
            var leadingZeros = (byte)ulong.LeadingZeroCount(masked);
            return (byte)(leadingZeros - knownZeros + 1);
        }

        /// <summary>
        /// Converts this estimator from the sparse to the dense representation.
        /// This is automatically called when the sparse representation becomes too large.
        /// </summary>
        /// <remarks>
        /// The sparse representation is more memory-efficient for small to medium cardinalities,
        /// but becomes less efficient as the number of distinct buckets grows. This method
        /// performs the transition to the dense representation when needed.
        /// </remarks>
        private void SwitchToDenseRepresentation()
        {
            if (!isSparse)
            {
                return;
            }

            lookupDense = new byte[m];
            foreach (KeyValuePair<ushort, byte> kvp in lookupSparse)
            {
                int index = kvp.Key;
                lookupDense[index] = kvp.Value;
            }
            lookupSparse = null;
            isSparse = false;
        }
#endregion

        #region IEquatable implementation
        /// <summary>
        /// Determines whether the specified CardinalityEstimator is equal to the current CardinalityEstimator.
        /// </summary>
        /// <param name="other">The CardinalityEstimator to compare with the current CardinalityEstimator.</param>
        /// <returns>
        /// <c>true</c> if the specified CardinalityEstimator is equal to the current CardinalityEstimator; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// Two CardinalityEstimators are considered equal if they have the same configuration parameters,
        /// internal state, and would produce the same cardinality estimates. This includes comparing
        /// the accuracy settings, hash function, and internal data structures.
        /// </remarks>
        public bool Equals(CardinalityEstimator other)
        {
            if (other == null)
            {
                return false;
            }

            if (bitsPerIndex != other.bitsPerIndex ||
                bitsForHll != other.bitsForHll ||
                m != other.m ||
                alphaM != other.alphaM ||
                subAlgorithmSelectionThreshold != other.subAlgorithmSelectionThreshold ||
                sparseMaxElements != other.sparseMaxElements ||
                isSparse != other.isSparse ||
                hashFunction != other.hashFunction ||
                hashFunctionSpan != other.hashFunctionSpan)
            {
                return false;
            }

            if ((lookupDense != null && other.lookupDense == null) ||
                (lookupDense == null && other.lookupDense != null))
            {
                return false;
            }

            if ((lookupSparse != null && other.lookupSparse == null) ||
                (lookupSparse == null && other.lookupSparse != null))
            {
                return false;
            }

            if ((directCount != null && other.directCount == null) ||
                (directCount == null && other.directCount != null))
            {
                return false;
            }

            if (lookupDense != null &&
                lookupDense.Length != other.lookupDense.Length)
            {
                return false;
            }
            if (lookupSparse != null &&
                lookupSparse.Count != other.lookupSparse.Count)
            {
                return false;
            }
            if (directCount != null &&
                directCount.Count != other.directCount.Count)
            {
                return false;
            }

            if (lookupDense != null)
            {
                for (int i = 0; i < lookupDense.Length; i++)
                {
                    if (lookupDense[i] != other.lookupDense[i])
                    {
                        return false;
                    }
                }
            }

            if (directCount != null &&
                !directCount.SetEquals(other.directCount))
            {
                return false;
            }

            if (lookupSparse != null)
            {
                foreach (var kvp in lookupSparse)
                {
                    if (!other.lookupSparse.TryGetValue(kvp.Key, out var otherValue) ||
                        otherValue != kvp.Value)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        #endregion
    }
}
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

    public delegate ulong GetHashCodeDelegate(byte[] bytes);
    
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
    /// 1. This implementation is not thread-safe
    /// 2. By default, it uses the 128-bit Murmur3 hash function, <see cref="http://github.com/darrenkopp/murmurhash-net"/>.
    ///    For legacy support, the CTOR also allows using the 64-bit Fowler/Noll/Vo-0 FNV-1a hash function, <see cref="http://www.isthe.com/chongo/src/fnv/hash_64a.c" />
    /// 3. Estimation is perfect up to 100 elements, then approximate
    /// </remarks>
    [Serializable]
    public class CardinalityEstimator : ICardinalityEstimator<string>, ICardinalityEstimator<int>, ICardinalityEstimator<uint>,
        ICardinalityEstimator<long>, ICardinalityEstimator<ulong>, ICardinalityEstimator<float>, ICardinalityEstimator<double>,
        ICardinalityEstimator<byte[]>, IEquatable<CardinalityEstimator>
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
        private readonly int bitsForHll;

        /// <summary>
        /// HLL lookup table size
        /// </summary>
        private readonly int m;

        /// <summary>
        /// Fixed bias correction factor
        /// </summary>
        private readonly double alphaM;

        /// <summary>
        /// Threshold determining whether to use LinearCounting or HyperLogLog based on an initial estimate
        /// </summary>
        private readonly double subAlgorithmSelectionThreshold;

        /// <summary>
        /// Lookup table for the dense representation
        /// </summary>
        private byte[] lookupDense;

        /// <summary>
        /// Lookup dictionary for the sparse representation
        /// </summary>
        private IDictionary<ushort, byte> lookupSparse;

        /// <summary>
        /// Max number of elements to hold in the sparse representation
        /// </summary>
        private readonly int sparseMaxElements;

        /// <summary>
        /// Indicates that the sparse representation is currently used
        /// </summary>
        private bool isSparse;

        /// <summary>
        /// Set for direct counting of elements
        /// </summary>
        private HashSet<ulong> directCount;

        /// <summary>
        /// Hash function used
        /// </summary>
        [NonSerialized]
        private GetHashCodeDelegate hashFunction;
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new instance of CardinalityEstimator
        /// </summary>
        /// <param name="b">
        /// Number of bits determining accuracy and memory consumption, in the range [4, 16] (higher = greater accuracy and memory usage).
        /// For large cardinalities, the standard error is 1.04 * 2^(-b/2), and the memory consumption is bounded by 2^b kilobytes.
        /// The default value of 14 typically yields 3% error or less across the entire range of cardinalities (usually much less),
        /// and uses up to ~16kB of memory.  b=4 yields less than ~100% error and uses less than 1kB. b=16 uses up to ~64kB and usually yields 1%
        /// error or less
        /// </param>
        /// <param name="hashFunctionId">Type of hash function to use. Default is Murmur3, and FNV-1a is provided for legacy support</param>
        /// <param name="useDirectCounting">
        /// True if direct count should be used for up to <see cref="DirectCounterMaxElements"/> elements.
        /// False if direct count should be avoided and use always estimation, even for low cardinalities.
        /// </param>
        public CardinalityEstimator(GetHashCodeDelegate hashFunction = null, int b = 14, bool useDirectCounting = true)
            : this(hashFunction, CreateEmptyState(b, useDirectCounting))
        { }

        /// <summary>
        /// Copy constructor
        /// </summary>
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
        /// Creates a CardinalityEstimator with the given <paramref name="state" />
        /// </summary>
        internal CardinalityEstimator(GetHashCodeDelegate hashFunction, CardinalityEstimatorState state)
        {
            bitsPerIndex = state.BitsPerIndex;
            bitsForHll = 64 - bitsPerIndex;
            m = (int) Math.Pow(2, bitsPerIndex);
            alphaM = GetAlphaM(m);
            subAlgorithmSelectionThreshold = GetSubAlgorithmSelectionThreshold(bitsPerIndex);

            // Init the hash function
            this.hashFunction = hashFunction;
            if (this.hashFunction == null)
            {
#if NET8_0_OR_GREATER
                hashFunction = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
#else
                hashFunction = Murmur3.GetHashCode;
#endif
            }

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
        public ulong CountAdditions { get; private set; }
        #endregion

        #region Public methods
        /// <summary>
        /// Add an element of type <see cref="string"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(string element)
        {
            ulong hashCode = hashFunction(Encoding.UTF8.GetBytes(element));
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="int"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
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
        /// <returns>True is estimator's state was modified. False otherwise</returns>
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
        /// <returns>True is estimator's state was modified. False otherwise</returns>
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
        /// <returns>True is estimator's state was modified. False otherwise</returns>
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
        /// <returns>True is estimator's state was modified. False otherwise</returns>
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
        /// <returns>True is estimator's state was modified. False otherwise</returns>
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
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(byte[] element)
        {
            ulong hashCode = hashFunction(element);
            bool changed = AddElementHash(hashCode);
            CountAdditions++;
            return changed;
        }

        /// <summary>
        /// Returns the estimated number of items in the estimator
        /// </summary>
        /// <remarks>
        /// If Direct Count is enabled, and only a few items were added, the exact count is returned
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
        /// <param name="other">another instance of CardinalityEstimator</param>
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
        /// <param name="estimators">Instances of <see cref="CardinalityEstimator"/></param>
        /// <returns>
        /// A new <see cref="CardinalityEstimator" /> if there is at least one non-null <see cref="CardinalityEstimator" /> in
        /// <paramref name="estimators" />; otherwise <see langword="null" />.
        /// </returns>
        /// <remarks>
        /// The <c>b</c> and <c>hashFunctionId</c> provided to the constructor for the result are taken from the first non-null
        /// <see cref="CardinalityEstimator"/> in <paramref name="estimators"/>. The remaining estimators are assumed to use the same parameters.
        /// </remarks>
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
        /// Creates state for an empty CardinalityEstimator : DirectCount and LookupSparse are empty, LookupDense is null.
        /// </summary>
        /// <param name="b"><see cref="CardinalityEstimator(int, HashFunctionId)" /></param>
        /// <param name="hashFunctionId"><see cref="CardinalityEstimator(int, HashFunctionId)" /></param>
        /// <param name="useDirectCount">
        /// True if direct count should be used for up to <see cref="DirectCounterMaxElements"/> elements.
        /// False if direct count should be avoided and use always estimation, even for low cardinalities.
        /// </param>
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
        /// Returns the threshold determining whether to use LinearCounting or HyperLogLog for an estimate. Values are from the supplementary
        /// material of Huele et al.,
        /// <see cref="http://docs.google.com/document/d/1gyjfMHy43U9OWBXxfaeG-3MjGzejW1dlpyMwEYAAWEI/view?fullscreen#heading=h.nd379k1fxnux" />
        /// </summary>
        /// <param name="bits">Number of bits</param>
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
        /// Gets the appropriate value of alpha_M for the given <paramref name="m" />
        /// </summary>
        /// <param name="m">size of the lookup table</param>
        /// <returns>alpha_M for bias correction</returns>
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
        /// Returns the number of leading zeroes in the <paramref name="bitsToCount" /> highest bits of <paramref name="hash" />, plus one
        /// </summary>
        /// <param name="hash">Hash value to calculate the statistic on</param>
        /// <param name="bitsToCount">Lowest bit to count from <paramref name="hash" /></param>
        /// <returns>The number of leading zeroes in the binary representation of <paramref name="hash" />, plus one</returns>
        internal static byte GetSigma(ulong hash, int bitsToCount)
        {
            byte sigma = 1;
            for (int i = bitsToCount - 1; i >= 0; --i)
            {
                if (((hash >> i) & 1) == 0)
                {
                    sigma++;
                }
                else
                {
                    break;
                }
            }
            return sigma;
        }

        /// <summary>
        /// Converts this estimator from the sparse to the dense representation
        /// </summary>
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
                hashFunction != other.hashFunction)
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
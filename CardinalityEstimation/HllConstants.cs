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

    /// <summary>
    /// Shared compile-time/static constants and lookup tables used by both
    /// <see cref="CardinalityEstimator"/> and <see cref="ConcurrentCardinalityEstimator"/>.
    /// </summary>
    internal static class HllConstants
    {
        /// <summary>
        /// Maximum cardinality at which the estimator keeps a direct (exact) hash set
        /// of element hashes. Once exceeded, the estimator transitions to the
        /// sparse/dense HLL representations and the direct count is discarded.
        /// </summary>
        internal const int DirectCounterMaxElements = 100;

        /// <summary>
        /// Maximum number of bytes a string can encode to before <c>Add(string)</c>
        /// falls back from <c>stackalloc</c> to a heap-allocated buffer.
        /// </summary>
        internal const int StackallocByteThreshold = 256;

        /// <summary>
        /// Length of <see cref="InversePowersOfTwo"/>. sigma is bounded by
        /// bitsForHll + 1 = (64 - bitsPerIndex) + 1, which is at most 61 for the
        /// supported range of bitsPerIndex (4..16). The table is sized 65 to safely
        /// cover all sigma values without a bounds check ever firing on real inputs.
        /// </summary>
        internal const int InversePowersOfTwoLength = 65;

        /// <summary>
        /// Precomputed table of 2^-i for i in [0, <see cref="InversePowersOfTwoLength"/>),
        /// used by the HLL summation in <c>Count()</c> to evaluate each bucket in O(1)
        /// instead of calling <see cref="Math.Pow(double, double)"/> (a transcendental
        /// function) up to 2^16 times per call.
        /// </summary>
        /// <remarks>
        /// Each entry is bit-equivalent to <c>Math.Pow(2.0, -i)</c> because every 2^-i
        /// for i in [0, 64] is exactly representable as an IEEE 754 double.
        /// </remarks>
        internal static readonly double[] InversePowersOfTwo = BuildInversePowersOfTwo();

        private static double[] BuildInversePowersOfTwo()
        {
            var table = new double[InversePowersOfTwoLength];
            for (int i = 0; i < table.Length; i++)
            {
                table[i] = Math.Pow(2.0, -i);
            }
            return table;
        }

        /// <summary>
        /// Returns the appropriate value of alpha_M for the given <paramref name="m"/>
        /// for HyperLogLog bias correction.
        /// </summary>
        /// <param name="m">Size of the lookup table (2^bitsPerIndex)</param>
        internal static double GetAlphaM(int m)
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
                    return 0.7213 / (1 + (1.079 / m));
            }
        }

        /// <summary>
        /// Returns the threshold determining whether to use LinearCounting or HyperLogLog
        /// for an estimate. Values are from the supplementary material of Heule et al.
        /// </summary>
        /// <param name="bits">Number of bits for the estimator</param>
        /// <see href="http://docs.google.com/document/d/1gyjfMHy43U9OWBXxfaeG-3MjGzejW1dlpyMwEYAAWEI/view?fullscreen#heading=h.nd379k1fxnux"/>
        internal static double GetSubAlgorithmSelectionThreshold(int bits)
        {
            switch (bits)
            {
                case 4: return 10;
                case 5: return 20;
                case 6: return 40;
                case 7: return 80;
                case 8: return 220;
                case 9: return 400;
                case 10: return 900;
                case 11: return 1800;
                case 12: return 3100;
                case 13: return 6500;
                case 14: return 11500;
                case 15: return 20000;
                case 16: return 50000;
                case 17: return 120000;
                case 18: return 350000;
            }
            throw new ArgumentOutOfRangeException(nameof(bits), "Unexpected number of bits (should never happen)");
        }

        /// <summary>
        /// Creates a new empty <see cref="CardinalityEstimatorState"/> for the given
        /// configuration. Validates <paramref name="b"/> is in the supported range [4, 16].
        /// </summary>
        /// <param name="b">Number of bits determining accuracy and memory consumption (4..16)</param>
        /// <param name="useDirectCount">
        /// True to start with an exact direct counter (used up to
        /// <see cref="DirectCounterMaxElements"/> elements); false to skip direct
        /// counting and always use the HLL estimate.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="b"/> is not in the range [4, 16]
        /// </exception>
        internal static CardinalityEstimatorState CreateEmptyState(int b, bool useDirectCount)
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
    }
}

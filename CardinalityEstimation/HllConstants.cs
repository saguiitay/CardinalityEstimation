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

    /// <summary>
    /// Shared compile-time/static constants and lookup tables used by both
    /// <see cref="CardinalityEstimator"/> and <see cref="ConcurrentCardinalityEstimator"/>.
    /// </summary>
    internal static class HllConstants
    {
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
    }
}

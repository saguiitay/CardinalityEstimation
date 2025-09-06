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

using System;

namespace CardinalityEstimation
{
    /// <summary>
    /// Advanced generic cardinality estimator interface with performance optimizations
    /// </summary>
    public interface ICardinalityEstimatorMemory
    {
        /// <summary>
        /// Add data from a Span&lt;byte&gt; with zero allocations
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        bool Add(Span<byte> data);

        /// <summary>
        /// Add data from a ReadOnlySpan&lt;byte&gt; with zero allocations
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        bool Add(ReadOnlySpan<byte> data);

        /// <summary>
        /// Add data from Memory&lt;byte&gt; with optimized allocation patterns
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        bool Add(Memory<byte> data);

        /// <summary>
        /// Add data from ReadOnlyMemory&lt;byte&gt; with optimized allocation patterns
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        bool Add(ReadOnlyMemory<byte> data);
    }
}
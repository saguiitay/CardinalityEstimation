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
    /// Advanced generic cardinality estimator interface with performance optimizations.
    /// </summary>
    /// <remarks>
    /// The <c>Span&lt;byte&gt;</c> and <c>ReadOnlySpan&lt;byte&gt;</c> overloads are the
    /// true zero-allocation entry points: the implementation hashes the data directly
    /// without copying it to the heap. The <c>Memory&lt;byte&gt;</c> and
    /// <c>ReadOnlyMemory&lt;byte&gt;</c> overloads avoid the byte-array allocation of
    /// the legacy <c>byte[]</c> path, but the underlying <see cref="System.Memory{T}"/>
    /// itself may already reference heap-allocated storage (for example, when it wraps
    /// a managed array). For the lowest-allocation hot paths, prefer the
    /// <see cref="System.Span{T}"/> / <see cref="System.ReadOnlySpan{T}"/> overloads
    /// (e.g. backed by <c>stackalloc</c> or a pooled buffer's span).
    /// </remarks>
    public interface ICardinalityEstimatorMemory
    {
        /// <summary>
        /// Add data from a Span&lt;byte&gt; without allocating; the data is hashed in place.
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        bool Add(Span<byte> data);

        /// <summary>
        /// Add data from a ReadOnlySpan&lt;byte&gt; without allocating; the data is hashed in place.
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        bool Add(ReadOnlySpan<byte> data);

        /// <summary>
        /// Add data from a Memory&lt;byte&gt;.
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        /// <remarks>
        /// The hash itself is computed without additional allocation, but
        /// <see cref="System.Memory{T}"/> can reference heap-allocated storage. For a
        /// truly allocation-free call site (for example over a <c>stackalloc</c> buffer),
        /// prefer the <see cref="Add(System.Span{byte})"/> or
        /// <see cref="Add(System.ReadOnlySpan{byte})"/> overloads.
        /// </remarks>
        bool Add(Memory<byte> data);

        /// <summary>
        /// Add data from a ReadOnlyMemory&lt;byte&gt;.
        /// </summary>
        /// <param name="data">The byte data to add</param>
        /// <returns>True if estimator's state was modified. False otherwise</returns>
        /// <remarks>
        /// The hash itself is computed without additional allocation, but
        /// <see cref="System.ReadOnlyMemory{T}"/> can reference heap-allocated storage.
        /// For a truly allocation-free call site (for example over a <c>stackalloc</c>
        /// buffer), prefer the <see cref="Add(System.Span{byte})"/> or
        /// <see cref="Add(System.ReadOnlySpan{byte})"/> overloads.
        /// </remarks>
        bool Add(ReadOnlyMemory<byte> data);
    }
}
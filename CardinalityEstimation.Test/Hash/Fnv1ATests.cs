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
using CardinalityEstimation.Hash;
using Xunit;

namespace CardinalityEstimation.Test.Hash
{
    public class Fnv1ATests
    {
        [Fact]
        public void Fnv1AProducesRightValues()
        {
            // Check some precomputed values of FNV1A
            Assert.Equal(14695981039346656037, Fnv1A.GetHashCode(new byte[0]));
            Assert.Equal(1109817072422714760UL, Fnv1A.GetHashCode(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.Equal(11047178588169845073UL, Fnv1A.GetHashCode(new byte[] { 255, 255, 255, 255 }));
        }

        /// <summary>
        /// Tests that passing a null byte array to Fnv1A.GetHashCode throws a NullReferenceException.
        /// Given that the method does not include null-checks, a null input should trigger a NullReferenceException.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void GetHashCode_NullInput_ThrowsException()
        {
            // Arrange
            byte[] nullBytes = null;

            // Act & Assert
            Assert.Throws<NullReferenceException>(() => Fnv1A.GetHashCode(nullBytes));
        }
    }
}
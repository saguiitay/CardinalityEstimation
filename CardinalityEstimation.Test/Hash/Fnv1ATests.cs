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

using CardinalityEstimation;
using CardinalityEstimation.Hash;
using System;
using System.Collections.Generic;
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
        /// Tests that Fnv1A.GetHashCode returns the expected hash for various input byte arrays.
        /// The test verifies known outcomes for empty arrays and specific byte sequences.
        /// </summary>
        /// <param name="inputBytes">Input byte array to hash.</param>
        /// <param name="expectedHash">The expected 64-bit FNV-1a hash value.</param>
        [Theory]
        [MemberData(nameof(GetHashCodeTestCases))]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void GetHashCode_ValidInput_ReturnsExpectedHash(byte[] inputBytes, ulong expectedHash)
        {
            // Act
            ulong actualHash = Fnv1A.GetHashCode(inputBytes);

            // Assert
            Assert.Equal(expectedHash, actualHash);
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

        /// <summary>
        /// Provides test cases for Fnv1A.GetHashCode.
        /// Each test case includes an input byte array and its expected 64-bit hash.
        /// </summary>
        public static IEnumerable<object[]> GetHashCodeTestCases
        {
            get
            {
                // Test case: empty array should produce the initial hash value.
                yield return new object[] { new byte[0], 14695981039346656037UL };

                // Test case: specific byte sequence [1,2,3,4,5].
                yield return new object[] { new byte[] { 1, 2, 3, 4, 5 }, 1109817072422714760UL };

                // Test case: specific byte sequence [255,255,255,255].
                yield return new object[] { new byte[] { 255, 255, 255, 255 }, 11047178588169845073UL };
            }
        }
    }
}
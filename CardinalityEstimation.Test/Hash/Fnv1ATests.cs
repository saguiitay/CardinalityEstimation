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

namespace CardinalityEstimation.Test.Hash
{
    using CardinalityEstimation.Hash;
    using Xunit;

    public class Fnv1ATests
    {
        private readonly Fnv1A fnv1A;

        public Fnv1ATests()
        {
            fnv1A = new Fnv1A();
        }

        [Fact]
        public void Fnv1AProducesRightValues()
        {
            // Check some precomputed values of FNV1A
            Assert.Equal(14695981039346656037, fnv1A.GetHashCode(new byte[0]));
            Assert.Equal(1109817072422714760UL, fnv1A.GetHashCode(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.Equal(11047178588169845073UL, fnv1A.GetHashCode(new byte[] { 255, 255, 255, 255 }));
        }

        [Fact]
        public void Fnv1AHasRightId()
        {
            Assert.True(fnv1A.HashFunctionId == 0, "When serialized to a byte, FNV-1A's ID should be 0");
        }
    }
}
// /*  
//     See https://github.com/Microsoft/CardinalityEstimation.
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
        private Fnv1A sut;

        public Fnv1ATests()
        {
            this.sut = new Fnv1A();
        }

        [Fact]
        public void Fnv1AProducesRightValues()
        {
            // Check some precomputed values of FNV1A
            Assert.Equal(14695981039346656037, this.sut.GetHashCode(new byte[0]));
            Assert.Equal(1109817072422714760UL, this.sut.GetHashCode(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.Equal(11047178588169845073UL, this.sut.GetHashCode(new byte[] { 255, 255, 255, 255 }));
        }

        [Fact]
        public void Fnv1AHasRightId()
        {
            Assert.True((byte)this.sut.HashFunctionId == 0, "When serialized to a byte, FNV-1A's ID should be 0");
        }
    }
}
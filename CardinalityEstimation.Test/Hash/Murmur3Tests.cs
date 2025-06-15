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

using CardinalityEstimation.Hash;
using Xunit;

namespace CardinalityEstimation.Test.Hash
{
    public class Murmur3Tests
    {
        [Fact]
        public void Murmur3ProducesRightValues()
        {
            // Check some precomputed values of Murmur3
            Assert.Equal(0UL, Murmur3.GetHashCode(new byte[0]));
            Assert.Equal(18344466521425217038UL, Murmur3.GetHashCode(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.Equal(4889297221962843713UL, Murmur3.GetHashCode(new byte[] { 255, 255, 255, 255 }));
        }
    }
}
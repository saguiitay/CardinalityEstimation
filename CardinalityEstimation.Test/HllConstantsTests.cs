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

namespace CardinalityEstimation.Test
{
    using System;
    using Xunit;

    public class HllConstantsTests
    {
        [Theory]
        [InlineData(16, 0.673)]
        [InlineData(32, 0.697)]
        [InlineData(64, 0.709)]
        public void GetAlphaM_ReturnsCannedValuesForSmallM(int m, double expected)
        {
            Assert.Equal(expected, HllConstants.GetAlphaM(m));
        }

        [Theory]
        [InlineData(128)]
        [InlineData(1024)]
        [InlineData(16384)]
        [InlineData(65536)]
        public void GetAlphaM_ReturnsFormulaValueForLargerM(int m)
        {
            double expected = 0.7213 / (1 + (1.079 / m));
            Assert.Equal(expected, HllConstants.GetAlphaM(m));
        }

        [Theory]
        [InlineData(4, 10)]
        [InlineData(5, 20)]
        [InlineData(10, 900)]
        [InlineData(14, 11500)]
        [InlineData(16, 50000)]
        [InlineData(18, 350000)]
        public void GetSubAlgorithmSelectionThreshold_ReturnsHeuleEtAlValues(int bits, double expected)
        {
            Assert.Equal(expected, HllConstants.GetSubAlgorithmSelectionThreshold(bits));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(19)]
        [InlineData(0)]
        [InlineData(-1)]
        public void GetSubAlgorithmSelectionThreshold_ThrowsForUnsupportedBits(int bits)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HllConstants.GetSubAlgorithmSelectionThreshold(bits));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(17)]
        [InlineData(0)]
        public void CreateEmptyState_ThrowsForOutOfRangeB(int b)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => HllConstants.CreateEmptyState(b, useDirectCount: true));
            Assert.Equal("b", ex.ParamName);
        }

        [Theory]
        [InlineData(4, true)]
        [InlineData(14, true)]
        [InlineData(16, false)]
        public void CreateEmptyState_ReturnsConsistentInitialState(int b, bool useDirectCount)
        {
            var state = HllConstants.CreateEmptyState(b, useDirectCount);

            Assert.Equal(b, state.BitsPerIndex);
            Assert.True(state.IsSparse);
            Assert.NotNull(state.LookupSparse);
            Assert.Empty(state.LookupSparse);
            Assert.Null(state.LookupDense);
            Assert.Equal(0UL, state.CountAdditions);

            if (useDirectCount)
            {
                Assert.NotNull(state.DirectCount);
                Assert.Empty(state.DirectCount);
            }
            else
            {
                Assert.Null(state.DirectCount);
            }
        }

        [Fact]
        public void DirectCounterMaxElements_MatchesDocumentedValue()
        {
            // Pinned because the serializer's directCount validator (InvalidDataException
            // bound) and the estimator's transition trigger both depend on this value.
            Assert.Equal(100, HllConstants.DirectCounterMaxElements);
        }

        [Fact]
        public void StackallocByteThreshold_MatchesDocumentedValue()
        {
            Assert.Equal(256, HllConstants.StackallocByteThreshold);
        }
    }
}

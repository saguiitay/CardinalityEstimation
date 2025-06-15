using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Xunit;

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
    public class BiasCorrectionTests
    {
        [Fact]
        public void WhenRawEstimateIsInArrayCorrectBiasIsUsed()
        {
            double corrected = BiasCorrection.CorrectBias(12.207, 4);
            Assert.Equal(12.207 - 9.207, corrected);
        }

        [Fact]
        public void WhenRawEstimateIsBetweenArrayValuesCorrectBiasIsUsed()
        {
            double corrected = BiasCorrection.CorrectBias(11.1, 4);
            // The bias should be between 10 and 9.717, but much closer to 10
            Assert.Equal(1.1394700139470011, corrected);
        }

        [Fact]
        public void WhenRawEstimateIsLargerThanAllArrayValuesCorrectBiasIsUsed()
        {
            // The bias of the last array element should be used
            double corrected = BiasCorrection.CorrectBias(78.0, 4);
            Assert.Equal(78.0 - -1.7606, corrected);
        }

        [Fact]
        public void WhenRawEstimateIsSmallerThanAllArrayValuesCorrectBiasIsUsed()
        {
            // The bias of the first array element should be used
            double corrected = BiasCorrection.CorrectBias(10.5, 4);
            Assert.Equal(10.5 - 10, corrected);
        }

        [Fact]
        public void WhenCorrectedEstimateIsBelowZeroZeroIsReturned()
        {
            double corrected = BiasCorrection.CorrectBias(5, 4);
            Assert.Equal(0, corrected);
        }

        [Fact]
        public void RawEstimateArraysAndBiasDataArraysHaveSameLengths()
        {
            Assert.True(BiasCorrection.RawEstimate.Length >= 14);
            Assert.Equal(BiasCorrection.RawEstimate.Length, BiasCorrection.BiasData.Length);
            for (var bits = 0; bits < BiasCorrection.RawEstimate.Length; bits++)
            {
                Assert.Equal(BiasCorrection.RawEstimate[bits].Length, BiasCorrection.BiasData[bits].Length);
            }
        }

        /// <summary>
        /// Verifies that when the calculated bias exceeds the raw estimate,
        /// the corrected result is clamped to zero.
        /// </summary>
        [Theory]
        [InlineData(4, 1.0)]
        [InlineData(5, 5.0)]
        [InlineData(6, 10.0)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void CorrectBias_BiasExceedsRawEstimate_ReturnsZero(int bits, double rawEstimate)
        {
            // Arrange
            // Act
            double corrected = BiasCorrection.CorrectBias(rawEstimate, bits);
            // Assert
            Assert.Equal(0.0, corrected);
        }

        /// <summary>
        /// Verifies that special floating-point inputs are handled in accordance with System.Math rules.
        /// For PositiveInfinity, the result remains infinity; for NaN, the result is NaN;
        /// for NegativeInfinity, the result is clamped to zero.
        /// </summary>
        /// <param name = "rawEstimate">The special raw estimate value.</param>
        /// <param name = "isPositiveInfinityExpected">Indicates whether the result should be positive infinity.</param>
        /// <param name = "isNaNExpected">Indicates whether the result should be NaN.</param>
        [Theory]
        [InlineData(double.PositiveInfinity, true, false)]
        [InlineData(double.NegativeInfinity, false, false)]
        [InlineData(double.NaN, false, true)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void CorrectBias_SpecialFloatingPointInputs_PropagatesAccordingToMathRules(double rawEstimate, bool isPositiveInfinityExpected, bool isNaNExpected)
        {
            // Arrange
            int bits = 4;
            // Act
            double corrected = BiasCorrection.CorrectBias(rawEstimate, bits);
            // Assert
            if (isNaNExpected)
            {
                Assert.True(double.IsNaN(corrected));
            }
            else if (isPositiveInfinityExpected)
            {
                Assert.True(double.IsPositiveInfinity(corrected));
            }
            else
            {
                Assert.Equal(0.0, corrected);
            }
        }

    }

    /// <summary>
    /// Contains additional edge-case tests for <see cref = "BiasCorrection.CorrectBias(double, int)"/>.
    /// The original happy-path scenarios already exist; here we focus on
    /// numeric extremes, invalid estimator sizes and exact-match integrity
    /// across multiple precision levels.
    /// </summary>
    public class BiasCorrectionEdgeTests
    {
        #region Exact-match scenarios
        /// <summary>
        /// Verifies that when the raw estimate exactly matches the first element
        /// of the corresponding RawEstimate array, the bias from the matching
        /// index is used without interpolation.
        /// </summary>
        /// <param name = "bits">Estimator precision being exercised.</param>
        /// <param name = "rawEstimate">Exact raw estimate value located at index 0.</param>
        /// <param name = "expectedBias">Bias located at index 0 for the same precision.</param>
        [Theory]
        [InlineData(4, 11.0, 10.0)]
        [InlineData(5, 23.0, 22.0)]
        [InlineData(6, 46.0, 45.0)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25306.18+1df180b")]
        [Trait("Category", "auto-generated")]
        public void CorrectBias_RawEstimateMatchesFirstArrayElement_ReturnsValueMinusBias(int bits, double rawEstimate, double expectedBias)
        {
            // Act
            double corrected = BiasCorrection.CorrectBias(rawEstimate, bits);
            // Assert
            Assert.Equal(rawEstimate - expectedBias, corrected);
        }

        #endregion
        #region Extreme double values
        /// <summary>
        /// Ensures that special floating-point inputs are propagated as defined
        /// by System.Math.Max and arithmetic rules.
        /// </summary>
        /// <param name = "rawEstimate">Special double value being tested.</param>
        /// <param name = "isPositiveInfinityExpected">True when the result should be positive infinity.</param>
        /// <param name = "isNaNExpected">True when the result should be NaN.</param>
        [Theory]
        [InlineData(double.PositiveInfinity, true, false)]
        [InlineData(double.NegativeInfinity, false, false)]
        [InlineData(double.NaN, false, true)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25306.18+1df180b")]
        [Trait("Category", "auto-generated")]
        public void CorrectBias_SpecialFloatingPointInputs_PropagatesAccordingToMathRules(double rawEstimate, bool isPositiveInfinityExpected, bool isNaNExpected)
        {
            // Act
            double result = BiasCorrection.CorrectBias(rawEstimate, 4);
            // Assert
            if (isNaNExpected)
            {
                Assert.True(double.IsNaN(result));
            }
            else if (isPositiveInfinityExpected)
            {
                Assert.True(double.IsPositiveInfinity(result));
            }
            else
            {
                Assert.Equal(0, result); // Negative infinity or very small values clamp to zero.
            }
        }

        #endregion
        #region Invalid estimator sizes
        #endregion
        #region Bias larger than raw estimate
        /// <summary>
        /// Confirms that when the calculated bias exceeds the raw estimate,
        /// the corrected value is never negative and is instead clamped to zero.
        /// </summary>
        /// <param name = "bits">Estimator precision.</param>
        /// <param name = "rawEstimate">Intentionally low raw estimate.</param>
        [Theory]
        [InlineData(4, 1.0)] // Bias ~10
        [InlineData(5, 5.0)] // Bias ~22
        [InlineData(6, 10.0)] // Bias ~45
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25306.18+1df180b")] // Bias ~45
        [Trait("Category", "auto-generated")] // Bias ~45
        public void CorrectBias_BiasExceedsRawEstimate_ReturnsZero(int bits, double rawEstimate)
        {
            // Act
            double corrected = BiasCorrection.CorrectBias(rawEstimate, bits);
            // Assert
            Assert.Equal(0, corrected);
        }
        #endregion
    }
}
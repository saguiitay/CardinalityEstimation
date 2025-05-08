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
    using System.Collections.Generic;
    using Xunit;

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

        [Theory]
        [InlineData(4)]
        [InlineData(5)] 
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(18)]
        public void BiasCorrection_WithDifferentPrecisions_ProducesValidResults(int precision)
        {
            // Test with different raw estimates for each precision level
            double[] testValues = { 10.0, 100.0, 1000.0, 10000.0 };

            foreach (double rawEstimate in testValues)
            {
                double corrected = BiasCorrection.CorrectBias(rawEstimate, precision);
                
                // Basic validation
                Assert.True(corrected >= 0, $"Corrected value should not be negative for precision {precision}");
                Assert.True(!double.IsNaN(corrected), $"Corrected value should not be NaN for precision {precision}");
                Assert.True(!double.IsInfinity(corrected), $"Corrected value should not be infinity for precision {precision}");
            }
        }

        [Theory]
        [InlineData(3)] // Too small
        [InlineData(19)] // Too large
        public void BiasCorrection_WithInvalidPrecision_ThrowsException(int precision)
        {
            Assert.Throws<IndexOutOfRangeException>(() => BiasCorrection.CorrectBias(100.0, precision));
        }

        [Fact]
        public void BiasCorrection_WithExtremeValues_HandlesGracefully()
        {
            // Test with very large value
            double corrected = BiasCorrection.CorrectBias(double.MaxValue / 2, 14);
            Assert.True(corrected > 0);
            Assert.True(!double.IsInfinity(corrected));

            // Test with very small positive value
            corrected = BiasCorrection.CorrectBias(double.Epsilon, 14);
            Assert.Equal(0, corrected);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(12)]
        [InlineData(16)]
        [InlineData(18)]
        public void BiasCorrectionWorks_ForSpecificPrecisions(int precision)
        {
            // Get a raw estimate within the expected range for this precision
            double rawEstimate = 100.0 * Math.Pow(2, precision - 4); // Scale based on precision
            double corrected = BiasCorrection.CorrectBias(rawEstimate, precision);
            
            // Verify corrections meet basic requirements
            Assert.True(corrected >= 0);
            Assert.True(!double.IsNaN(corrected));
            Assert.True(!double.IsInfinity(corrected));
            
            // For higher precisions, correction should be closer to raw estimate
            if (precision > 8)
            {
                Assert.True(Math.Abs(corrected - rawEstimate) / rawEstimate < 0.5);
            }
        }

        [Fact] 
        public void BiasCorrection_ShowsExpectedTrends()
        {
            // Test that bias correction has expected behavior across precisions
            const int testPrecision = 8; // Use mid-range precision for testing
            double[] testValues = { 100.0, 1000.0, 10000.0 };

            var corrections = new List<double>();
            foreach (double value in testValues)
            {
                double corrected = BiasCorrection.CorrectBias(value, testPrecision);
                corrections.Add(corrected);
            }

            // Verify corrections increase with input values
            for (int i = 1; i < corrections.Count; i++)
            {
                Assert.True(corrections[i] > corrections[i-1], 
                    "Corrections should increase with larger input values");
            }

            // Verify bias impact decreases proportionally
            double firstRatio = Math.Abs(corrections[0] - testValues[0]) / testValues[0];
            double lastRatio = Math.Abs(corrections[^1] - testValues[^1]) / testValues[^1];
            Assert.True(firstRatio > lastRatio, 
                "Relative bias impact should decrease with larger values");
        }
    }
}

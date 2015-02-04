/*  
    See https://github.com/Microsoft/CardinalityEstimation.
    The MIT License (MIT)

    Copyright (c) 2015 Microsoft

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

namespace CardinalityEstimation.Test
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class BiasCorrectionTests
    {
        [TestMethod]
        public void WhenRawEstimateIsInArrayCorrectBiasIsUsed()
        {
            double corrected = BiasCorrection.CorrectBias(12.207, 4);
            Assert.AreEqual(12.207 - 9.207, corrected);
        }

        [TestMethod]
        public void WhenRawEstimateIsBetweenArrayValuesCorrectBiasIsUsed()
        {
            double corrected = BiasCorrection.CorrectBias(11.1, 4);
            // The bias should be between 10 and 9.717, but much closer to 10
            Assert.AreEqual(1.1394700139470011, corrected);
        }

        [TestMethod]
        public void WhenRawEstimateIsLargerThanAllArrayValuesCorrectBiasIsUsed()
        {
            // The bias of the last array element should be used
            double corrected = BiasCorrection.CorrectBias(78.0, 4);
            Assert.AreEqual(78.0 - -1.7606, corrected);
        }

        [TestMethod]
        public void WhenRawEstimateIsSmallerThanAllArrayValuesCorrectBiasIsUsed()
        {
            // The bias of the first array element should be used
            double corrected = BiasCorrection.CorrectBias(10.5, 4);
            Assert.AreEqual(10.5 - 10, corrected);
        }

        [TestMethod]
        public void WhenCorrectedEstimateIsBelowZeroZeroIsReturned()
        {
            double corrected = BiasCorrection.CorrectBias(5, 4);
            Assert.AreEqual(0, corrected);
        }

        [TestMethod]
        public void RawEstimateArraysAndBiasDataArraysHaveSameLengths()
        {
            Assert.IsTrue(BiasCorrection.RawEstimate.Length >= 14);
            Assert.AreEqual(BiasCorrection.RawEstimate.Length, BiasCorrection.BiasData.Length);

            for (int bits = 0; bits < BiasCorrection.RawEstimate.Length; bits++)
            {
                Assert.AreEqual(BiasCorrection.RawEstimate[bits].Length, BiasCorrection.BiasData[bits].Length);
            }
        }
    }
}
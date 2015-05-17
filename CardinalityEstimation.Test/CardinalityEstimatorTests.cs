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

namespace CardinalityEstimation.Test
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class CardinalityEstimatorTests
    {
        private const int ElementSizeInBytes = 20;
        public static readonly Random Rand = new Random();

        private Stopwatch stopwatch;

        [TestInitialize]
        public void Init()
        {
            this.stopwatch = new Stopwatch();
            this.stopwatch.Start();
        }

        [TestCleanup]
        public void Cleanup()
        {
            this.stopwatch.Stop();
            Console.WriteLine("Total test time: {0}", this.stopwatch.Elapsed);
        }

        [TestMethod]
        public void TestGetSigma()
        {
            // simulate a 64 bit hash and 14 bits for indexing
            const int bitsToCount = 64 - 14;
            Assert.AreEqual(51, CardinalityEstimator.GetSigma(0, bitsToCount));
            Assert.AreEqual(50, CardinalityEstimator.GetSigma(1, bitsToCount));
            Assert.AreEqual(47, CardinalityEstimator.GetSigma(8, bitsToCount));
            Assert.AreEqual(1, CardinalityEstimator.GetSigma((ulong) (Math.Pow(2, bitsToCount) - 1), bitsToCount));
            Assert.AreEqual(51, CardinalityEstimator.GetSigma((ulong) (Math.Pow(2, bitsToCount + 1)), bitsToCount));
        }

        [TestMethod]
        public void TestDifferentAccuracies()
        {
            const double stdError4Bits = 0.26;
            RunTest(stdError4Bits, 1000000);

            const double stdError12Bits = 0.01625;
            RunTest(stdError12Bits, 1000000);

            const double stdError14Bits = 0.008125;
            RunTest(stdError14Bits, 1000000);

            const double stdError16Bits = 0.0040625;
            RunTest(stdError16Bits, 1000000);
        }

        [TestMethod]
        public void AccuracyIsPerfectUnder100Members()
        {
            for (var i = 1; i < 100; i++)
            {
                RunTest(0.1, i, maxAcceptedError: 0);
            }
        }


        [TestMethod]
        public void TestAccuracySmallCardinality()
        {
            for (var i = 1; i < 10000; i = i*2)
            {
                RunTest(0.26, i, 1.5);
                RunTest(0.008125, i, 0.05);
                RunTest(0.0040625, i, 0.05);
            }
        }

        [TestMethod]
        public void TestMergeCardinalityUnder100()
        {
            const double stdError = 0.008125;
            const int cardinality = 99;
            RunTest(stdError, cardinality, numHllInstances: 60, maxAcceptedError: 0);
        }

        [TestMethod]
        public void TestMergeLargeCardinality()
        {
            const double stdError = 0.008125;
            const int cardinality = 1000000;
            RunTest(stdError, cardinality, numHllInstances: 60);
        }

        [TestMethod]
        public void TestRecreationFromData()
        {
            RunRecreationFromData(10);
            RunRecreationFromData(100);
            RunRecreationFromData(1000);
            RunRecreationFromData(10000);
            RunRecreationFromData(100000);
            RunRecreationFromData(1000000);
        }

        private void RunRecreationFromData(int cardinality = 1000000)
        {
            var hll = new CardinalityEstimator();

            var nextMember = new byte[ElementSizeInBytes];
            for (var i = 0; i < cardinality; i++)
            {
                Rand.NextBytes(nextMember);
                hll.Add(nextMember);
            }

            CardinalityEstimatorState data = hll.GetState();

            var hll2 = new CardinalityEstimator(data);
            CardinalityEstimatorState data2 = hll2.GetState();

            Assert.AreEqual(data.BitsPerIndex, data2.BitsPerIndex);
            Assert.AreEqual(data.IsSparse, data2.IsSparse);

            Assert.IsTrue((data.DirectCount != null && data2.DirectCount != null) || (data.DirectCount == null && data2.DirectCount == null));
            Assert.IsTrue((data.LookupSparse != null && data2.LookupSparse != null) ||
                          (data.LookupSparse == null && data2.LookupSparse == null));
            Assert.IsTrue((data.LookupDense != null && data2.LookupDense != null) || (data.LookupDense == null && data2.LookupDense == null));

            if (data.DirectCount != null)
            {
                // DirectCount are subsets of each-other => they are the same set
                Assert.IsTrue(data.DirectCount.IsSubsetOf(data2.DirectCount) && data2.DirectCount.IsSubsetOf(data.DirectCount));
            }
            if (data.LookupSparse != null)
            {
                Assert.IsTrue(data.LookupSparse.DictionaryEqual(data2.LookupSparse));
            }
            if (data.LookupDense != null)
            {
                Assert.IsTrue(data.LookupDense.SequenceEqual(data2.LookupDense));
            }
        }

        [TestMethod]
        [Timeout(90*60*1000)] // 90 minutes
        [Ignore] // Test runtime is long
        public void TestPast32BitLimit()
        {
            const double stdError = 0.008125;
            var cardinality = (long) (Math.Pow(2, 32) + 1703); // just some big number beyond 32 bits
            RunTest(stdError, cardinality);
        }

        [TestMethod]
        [Ignore] // Test runtime is long
        public void TestAccuracyLargeCardinality()
        {
            for (var i = 10007; i < 10000000; i *= 2)
            {
                RunTest(0.26, i);
                RunTest(0.008125, i);
                RunTest(0.0040625, i);
            }

            RunTest(0.008125, 100000000);
        }

        [TestMethod]
        [Ignore] // Test runtime is long
        public void ReportAccuracy()
        {
            var hll = new CardinalityEstimator();
            double maxError = 0;
            var worstMember = 0;
            var nextMember = new byte[ElementSizeInBytes];
            for (var i = 0; i < 10000000; i++)
            {
                Rand.NextBytes(nextMember);
                hll.Add(nextMember);

                if (i%1007 == 0) // just some interval to sample error at, can be any number
                {
                    double error = (hll.Count() - (double) (i + 1))/((double) i + 1);
                    if (error > maxError)
                    {
                        maxError = error;
                        worstMember = i + 1;
                    }
                }
            }

            Console.WriteLine("Worst: {0}", worstMember);
            Console.WriteLine("Max error: {0}", maxError);

            Assert.IsTrue(true);
        }

        private void RunTest(double stdError, long expectedCount, double? maxAcceptedError = null, int numHllInstances = 1)
        {
            maxAcceptedError = maxAcceptedError ?? 5*stdError; // should fail appx once in 1.7 million runs
            int b = GetAccuracyInBits(stdError);

            var runStopwatch = new Stopwatch();
            long gcMemoryAtStart = GetGcMemory();

            // init HLLs
            var hlls = new CardinalityEstimator[numHllInstances];
            for (var i = 0; i < numHllInstances; i++)
            {
                hlls[i] = new CardinalityEstimator(b);
            }

            var nextMember = new byte[ElementSizeInBytes];
            runStopwatch.Start();
            for (long i = 0; i < expectedCount; i++)
            {
                // pick random hll, add member
                int chosenHll = Rand.Next(numHllInstances);
                Rand.NextBytes(nextMember);
                hlls[chosenHll].Add(nextMember);
            }

            runStopwatch.Stop();
            ReportMemoryCost(gcMemoryAtStart); // done here so references can't be GC'ed yet

            // Merge
            CardinalityEstimator mergedHll = CardinalityEstimator.Merge(hlls);
            Console.WriteLine("Run time: {0}", runStopwatch.Elapsed);
            Console.WriteLine("Expected {0}, got {1}", expectedCount, mergedHll.Count());

            double obsError = Math.Abs(mergedHll.Count()/(double) (expectedCount) - 1.0);
            Console.WriteLine("StdErr: {0}.  Observed error: {1}", stdError, obsError);
            Assert.IsTrue(obsError <= maxAcceptedError, string.Format("Observed error was over {0}", maxAcceptedError));
            Console.WriteLine();
        }

        /// <summary>
        ///     Gets the number of indexing bits required to produce a given standard error
        /// </summary>
        /// <param name="stdError">
        ///     Standard error, which determines accuracy and memory consumption. For large cardinalities, the observed error is usually less than
        ///     3 * <paramref name="stdError" />.
        /// </param>
        /// <returns></returns>
        private static int GetAccuracyInBits(double stdError)
        {
            double sqrtm = 1.04/stdError;
            var b = (int) Math.Ceiling(CardinalityEstimator.Log2(sqrtm*sqrtm));
            return b;
        }

        private static long GetGcMemory()
        {
            GC.Collect();
            return GC.GetTotalMemory(true);
        }

        private static void ReportMemoryCost(long gcMemoryAtStart)
        {
            long memoryCost = GetGcMemory() - gcMemoryAtStart;
            Console.WriteLine("Appx. memory cost: {0} bytes", memoryCost);
        }
    }
}
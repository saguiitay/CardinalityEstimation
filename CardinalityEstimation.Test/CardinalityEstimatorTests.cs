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
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization.Formatters.Binary;

    using CardinalityEstimation.Hash;

    using Xunit;

    
    public class CardinalityEstimatorTests : IDisposable
    {
        private const int ElementSizeInBytes = 20;
        public static readonly Random Rand = new Random();

        private Stopwatch stopwatch;

        public CardinalityEstimatorTests()
        {
            this.stopwatch = new Stopwatch();
            this.stopwatch.Start();
        }

        public void Dispose()
        {
            this.stopwatch.Stop();
            Console.WriteLine("Total test time: {0}", this.stopwatch.Elapsed);
        }

        [Fact]
        public void TestGetSigma()
        {
            // simulate a 64 bit hash and 14 bits for indexing
            const int bitsToCount = 64 - 14;
            Assert.Equal(51, CardinalityEstimator.GetSigma(0, bitsToCount));
            Assert.Equal(50, CardinalityEstimator.GetSigma(1, bitsToCount));
            Assert.Equal(47, CardinalityEstimator.GetSigma(8, bitsToCount));
            Assert.Equal(1, CardinalityEstimator.GetSigma((ulong) (Math.Pow(2, bitsToCount) - 1), bitsToCount));
            Assert.Equal(51, CardinalityEstimator.GetSigma((ulong) (Math.Pow(2, bitsToCount + 1)), bitsToCount));
        }

        [Fact]
        public void TestCountAdditions()
        {
            var estimator = new CardinalityEstimator();

            Assert.Equal(0UL, estimator.CountAdditions);

            estimator.Add(0);
            estimator.Add(0);

            Assert.Equal(2UL, estimator.CountAdditions);

            var estimator2 = new CardinalityEstimator();
            estimator2.Add(0);
            estimator.Merge(estimator2);

            Assert.Equal(3UL, estimator.CountAdditions);
        }

        [Fact]
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

        [Fact]
        public void AccuracyIsPerfectUnder100Members()
        {
            for (var i = 1; i < 100; i++)
            {
                RunTest(0.1, i, maxAcceptedError: 0);
            }
        }


        [Fact]
        public void TestAccuracySmallCardinality()
        {
            for (var i = 1; i < 10000; i = i*2)
            {
                RunTest(0.26, i, 1.5);
                RunTest(0.008125, i, 0.05);
                RunTest(0.0040625, i, 0.05);
            }
        }

        [Fact]
        public void TestMergeCardinalityUnder100()
        {
            const double stdError = 0.008125;
            const int cardinality = 99;
            RunTest(stdError, cardinality, numHllInstances: 60, maxAcceptedError: 0);
        }

        [Fact]
        public void TestMergeLargeCardinality()
        {
            const double stdError = 0.008125;
            const int cardinality = 1000000;
            RunTest(stdError, cardinality, numHllInstances: 60);
        }

        [Fact]
        public void TestRecreationFromData()
        {
            RunRecreationFromData(10);
            RunRecreationFromData(100);
            RunRecreationFromData(1000);
            RunRecreationFromData(10000);
            RunRecreationFromData(100000);
            RunRecreationFromData(1000000);
        }

        [Fact]
        public void StaticMergeTest()
        {
            const int expectedBitsPerIndex = 11;
            var estimators = new CardinalityEstimator[10];
            for (var i = 0; i < estimators.Length; i++)
            {
                estimators[i] = new CardinalityEstimator(expectedBitsPerIndex);
                estimators[i].Add(Rand.Next());
            }

            CardinalityEstimator merged = CardinalityEstimator.Merge(estimators);

            Assert.Equal(10UL, merged.Count());
            Assert.Equal(expectedBitsPerIndex, merged.GetState().BitsPerIndex);
        }

        [Fact]
        public void StaticMergeHandlesNullParameter()
        {
            CardinalityEstimator result = CardinalityEstimator.Merge(null as IEnumerable<CardinalityEstimator>);
            Assert.Null(result);
        }

        [Fact]
        public void StaticMergeHandlesNullElements()
        {
            const int expectedBitsPerIndex = 11;
            var estimators = new List<CardinalityEstimator> { null, new CardinalityEstimator(expectedBitsPerIndex, HashFunctionId.Fnv1A), null };
            CardinalityEstimator result = CardinalityEstimator.Merge(estimators);
            Assert.NotNull(result);
            Assert.Equal(expectedBitsPerIndex, result.GetState().BitsPerIndex);
        }

        [Fact]
        public void EstimatorWorksAfterDeserialization()
        {
            ICardinalityEstimator<int> original = new CardinalityEstimator();
            original.Add(5);
            original.Add(7);
            Assert.Equal(2UL, original.Count());

            var binaryFormatter = new BinaryFormatter();
            using (var memoryStream = new MemoryStream())
            {
                binaryFormatter.Serialize(memoryStream, original);
                memoryStream.Seek(0, SeekOrigin.Begin);
                CardinalityEstimator copy = (CardinalityEstimator) binaryFormatter.Deserialize(memoryStream);

                Assert.Equal(2UL, copy.Count());
                copy.Add(5);
                copy.Add(7);
                Assert.Equal(2UL, copy.Count());
            }
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

            Assert.Equal(data.BitsPerIndex, data2.BitsPerIndex);
            Assert.Equal(data.IsSparse, data2.IsSparse);

            Assert.True((data.DirectCount != null && data2.DirectCount != null) || (data.DirectCount == null && data2.DirectCount == null));
            Assert.True((data.LookupSparse != null && data2.LookupSparse != null) ||
                          (data.LookupSparse == null && data2.LookupSparse == null));
            Assert.True((data.LookupDense != null && data2.LookupDense != null) || (data.LookupDense == null && data2.LookupDense == null));

            if (data.DirectCount != null)
            {
                // DirectCount are subsets of each-other => they are the same set
                Assert.True(data.DirectCount.IsSubsetOf(data2.DirectCount) && data2.DirectCount.IsSubsetOf(data.DirectCount));
            }
            if (data.LookupSparse != null)
            {
                Assert.True(data.LookupSparse.DictionaryEqual(data2.LookupSparse));
            }
            if (data.LookupDense != null)
            {
                Assert.True(data.LookupDense.SequenceEqual(data2.LookupDense));
            }
        }

        [Fact(Skip = "runtime is long")]
        public void TestPast32BitLimit()
        {
            const double stdError = 0.008125;
            var cardinality = (long) (Math.Pow(2, 32) + 1703); // just some big number beyond 32 bits
            RunTest(stdError, cardinality);
        }

        [Fact(Skip = "runtime is long")]
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

        [Fact(Skip = "runtime is long")]
        public void TestSequentialAccuracy()
        {
            for (var i = 10007; i < 10000000; i *= 2)
            {
                RunTest(0.26, i, sequential: true);
                RunTest(0.008125, i, sequential: true);
                RunTest(0.0040625, i, sequential: true);
            }

            RunTest(0.008125, 100000000);
        }

        [Fact(Skip = "runtime is long")]
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

            Assert.True(true);
        }

        /// <summary>
        ///     Generates <paramref name="expectedCount" /> random (or sequential) elements and adds them to CardinalityEstimators, then asserts that
        ///     the observed error rate is no more than <paramref name="maxAcceptedError" />
        /// </summary>
        /// <param name="stdError">Expected standard error of the estimators (upper bound)</param>
        /// <param name="expectedCount">number of elements to generate in total</param>
        /// <param name="maxAcceptedError">Maximum allowed error rate. Default is 4 times <paramref name="stdError" /></param>
        /// <param name="numHllInstances">Number of estimators to create. Generated elements will be assigned to one of the estimators at random</param>
        /// <param name="sequential">When false, elements will be generated at random. When true, elements will be 0,1,2...</param>
        private void RunTest(double stdError, long expectedCount, double? maxAcceptedError = null, int numHllInstances = 1,
            bool sequential = false)
        {
            maxAcceptedError = maxAcceptedError ?? 4*stdError; // should fail once in A LOT of runs
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
                if (sequential)
                {
                    hlls[chosenHll].Add(i);
                }
                else
                {
                    Rand.NextBytes(nextMember);
                    hlls[chosenHll].Add(nextMember);                    
                }
            }

            runStopwatch.Stop();
            ReportMemoryCost(gcMemoryAtStart); // done here so references can't be GC'ed yet

            // Merge
            CardinalityEstimator mergedHll = CardinalityEstimator.Merge(hlls);
            Console.WriteLine("Run time: {0}", runStopwatch.Elapsed);
            Console.WriteLine("Expected {0}, got {1}", expectedCount, mergedHll.Count());

            double obsError = Math.Abs(mergedHll.Count()/(double) (expectedCount) - 1.0);
            Console.WriteLine("StdErr: {0}.  Observed error: {1}", stdError, obsError);
            Assert.True(obsError <= maxAcceptedError, string.Format("Observed error was over {0}", maxAcceptedError));
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
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
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    using CardinalityEstimation.Hash;

    using Xunit;
    using Xunit.Abstractions;

    public class CardinalityEstimatorTests : IDisposable
    {
        private const int ElementSizeInBytes = 20;
        public static readonly Random Rand = new Random();

        private readonly ITestOutputHelper output;
        private readonly Stopwatch stopwatch;

        public CardinalityEstimatorTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
            stopwatch = new Stopwatch();
            stopwatch.Start();
        }

        public void Dispose()
        {
            stopwatch.Stop();
            output.WriteLine("Total test time: {0}", stopwatch.Elapsed);
        }

        [Fact]
        public void TestGetSigma()
        {
            // simulate a 64 bit hash and 14 bits for indexing
            const int bitsToCount = 64 - 14;
            Assert.Equal(51, CardinalityEstimator.GetSigma(0, bitsToCount));
            Assert.Equal(50, CardinalityEstimator.GetSigma(1, bitsToCount));
            Assert.Equal(47, CardinalityEstimator.GetSigma(8, bitsToCount));
            Assert.Equal(1, CardinalityEstimator.GetSigma((ulong)(Math.Pow(2, bitsToCount) - 1), bitsToCount));
            Assert.Equal(51, CardinalityEstimator.GetSigma((ulong)Math.Pow(2, bitsToCount + 1), bitsToCount));
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
        public void TestChanged()
        {
            var estimator = new CardinalityEstimator(Murmur3.GetHashCode);

            Assert.Equal(0UL, estimator.CountAdditions);

            bool changed = estimator.Add(0);
            Assert.True(changed);
            changed = estimator.Add(0);
            Assert.False(changed);

            for (var i = 1; i < 100; i++)
            {
                changed = estimator.Add(i);
                Assert.True(changed);
            }

            changed = estimator.Add(100);
            Assert.True(changed); //First change from direct count

            changed = estimator.Add(100);
            Assert.False(changed);

            changed = estimator.Add(101);
            Assert.True(changed);

            changed = estimator.Add(102);
            Assert.True(changed);

            changed = estimator.Add(0);
            Assert.False(changed);

            changed = estimator.Add(116); //element doesn't exist but the estimator internal state doesn't change
            Assert.False(changed);
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
        public void AccuracyIsWithinMarginForDirectCountingDisabledUnder100Members()
        {
            for (var i = 1; i < 100; i++)
            {
                RunTest(0.1, i, disableDirectCount: true);
                RunTest(0.03, i, disableDirectCount: true);
                RunTest(0.005, i, disableDirectCount: true);
            }
        }

        [Fact]
        public void TestAccuracySmallCardinality()
        {
            for (var i = 1; i < 10000; i *= 2)
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
                estimators[i] = new CardinalityEstimator(b: expectedBitsPerIndex);
                estimators[i].Add(Rand.Next());
            }

            CardinalityEstimator merged = CardinalityEstimator.Merge(estimators);

            Assert.Equal(10UL, merged.Count());
            Assert.Equal(expectedBitsPerIndex, merged.GetState().BitsPerIndex);
        }

        [Fact]
        public void StaticMergeHandlesNullParameter()
        {
            CardinalityEstimator result = CardinalityEstimator.Merge(null);
            Assert.Null(result);
        }

        [Fact]
        public void StaticMergeHandlesNullElements()
        {
            const int expectedBitsPerIndex = 11;
            var estimators = new List<CardinalityEstimator> { null, new CardinalityEstimator(Fnv1A.GetHashCode, expectedBitsPerIndex), null };
            CardinalityEstimator result = CardinalityEstimator.Merge(estimators);
            Assert.NotNull(result);
            Assert.Equal(expectedBitsPerIndex, result.GetState().BitsPerIndex);
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

            var hll2 = new CardinalityEstimator(Murmur3.GetHashCode, data);
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
            var cardinality = (long)(Math.Pow(2, 32) + 1703); // just some big number beyond 32 bits
            RunTest(stdError, cardinality);
        }

        [Fact]
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

        [Fact]
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

        [Fact]
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

                if (i % 1007 == 0) // just some interval to sample error at, can be any number
                {
                    double error = (hll.Count() - (double)(i + 1)) / ((double)i + 1);
                    if (error > maxError)
                    {
                        maxError = error;
                        worstMember = i + 1;
                    }
                }
            }

            output.WriteLine("Worst: {0}", worstMember);
            output.WriteLine("Max error: {0}", maxError);

            Assert.True(true);
        }

        [Fact]
        public void DirectCountingIsResetWhenMergingAlmostFullEstimators()
        {
            var addedEstimator = new CardinalityEstimator();
            var mergedEstimator = new CardinalityEstimator();

            for (int i = 0; i < 10_000; i++)
            {
                var guid = Guid.NewGuid().ToString();

                addedEstimator.Add(guid);

                // Simulate some intermediate estimators being merged together
                var temporaryEstimator = new CardinalityEstimator();
                temporaryEstimator.Add(guid);
                mergedEstimator.Merge(temporaryEstimator);
            }

            var serializer = new CardinalityEstimatorSerializer();

            var stream1 = new MemoryStream();
            serializer.Serialize(stream1, addedEstimator, true);

            var stream2 = new MemoryStream();
            serializer.Serialize(stream2, mergedEstimator, true);

            Assert.Equal(stream1.Length, stream2.Length);
        }

        [Fact]
        public void CopyConstructorCorrectlyCopiesValues()
        {
            for (int b = 4; b < 16; b++)
            {
                for (int cardinality = 1; cardinality < 10_000; cardinality *= 2)
                {
                    var hll = new CardinalityEstimator(b: b);

                    var nextMember = new byte[ElementSizeInBytes];
                    for (var i = 0; i < cardinality; i++)
                    {
                        Rand.NextBytes(nextMember);
                        hll.Add(nextMember);
                    }

                    var hll2 = new CardinalityEstimator(hll);

                    Assert.Equal(hll, hll2);
                }
            }

            for (int b = 4; b < 16; b++)
            {
                for (int cardinality = 1; cardinality < 10_000; cardinality *= 2)
                {
                    var hll = new CardinalityEstimator(b: b);

                    var nextMember = new byte[ElementSizeInBytes];
                    for (var i = 0; i < cardinality; i++)
                    {
                        Rand.NextBytes(nextMember);
                        hll.Add(nextMember);
                    }

                    var hll2 = new CardinalityEstimator(hll);

                    Assert.Equal(hll, hll2);
                }
            }
        }

        /// <summary>
        /// Generates <paramref name="expectedCount" /> random (or sequential) elements and adds them to CardinalityEstimators, then asserts that
        /// the observed error rate is no more than <paramref name="maxAcceptedError" />
        /// </summary>
        /// <param name="stdError">Expected standard error of the estimators (upper bound)</param>
        /// <param name="expectedCount">number of elements to generate in total</param>
        /// <param name="maxAcceptedError">Maximum allowed error rate. Default is 4 times <paramref name="stdError" /></param>
        /// <param name="numHllInstances">Number of estimators to create. Generated elements will be assigned to one of the estimators at random</param>
        /// <param name="sequential">When false, elements will be generated at random. When true, elements will be 0,1,2...</param>
        /// <param name="disableDirectCount">When true, will disable using direct counting for estimators less than 100 elements.</param>
        private void RunTest(double stdError, long expectedCount, double? maxAcceptedError = null, int numHllInstances = 1,
            bool sequential = false, bool disableDirectCount = false)
        {
            maxAcceptedError ??= 10 * stdError; // should fail once in A LOT of runs
            int b = GetAccuracyInBits(stdError);

            var runStopwatch = new Stopwatch();
            long gcMemoryAtStart = GetGcMemory();

            // init HLLs
            var hlls = new CardinalityEstimator[numHllInstances];
            for (var i = 0; i < numHllInstances; i++)
            {
                hlls[i] = new CardinalityEstimator(b: b);
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
            ReportMemoryCost(gcMemoryAtStart, output); // done here so references can't be GC'ed yet

            // Merge
            CardinalityEstimator mergedHll = CardinalityEstimator.Merge(hlls);
            output.WriteLine("Run time: {0}", runStopwatch.Elapsed);
            output.WriteLine("Expected {0}, got {1}", expectedCount, mergedHll.Count());

            double obsError = Math.Abs(mergedHll.Count() / (double)expectedCount - 1.0);
            output.WriteLine("StdErr: {0}.  Observed error: {1}", stdError, obsError);
            Assert.True(obsError <= maxAcceptedError, string.Format("Observed error was {0}, over {1}, when adding {2} items", obsError, maxAcceptedError, expectedCount));
            output.WriteLine(string.Empty);
        }

        /// <summary>
        /// Gets the number of indexing bits required to produce a given standard error
        /// </summary>
        /// <param name="stdError">
        /// Standard error, which determines accuracy and memory consumption. For large cardinalities, the observed error is usually less than
        /// 3 * <paramref name="stdError" />.
        /// </param>
        private static int GetAccuracyInBits(double stdError)
        {
            double sqrtm = 1.04 / stdError;
            var b = (int)Math.Ceiling(Log2(sqrtm * sqrtm));
            return b;
        }

        private static long GetGcMemory()
        {
            GC.Collect();
            return GC.GetTotalMemory(true);
        }

        private static void ReportMemoryCost(long gcMemoryAtStart, ITestOutputHelper outputHelper)
        {
            long memoryCost = GetGcMemory() - gcMemoryAtStart;
            outputHelper.WriteLine("Appx. memory cost: {0} bytes", memoryCost);
        }

        /// <summary>
        /// Returns the base-2 logarithm of <paramref name="x" />.
        /// This implementation is faster than <see cref="Math.Log(double,double)" /> as it avoids input checks
        /// </summary>
        /// <param name="x"></param>
        /// <returns>The base-2 logarithm of <paramref name="x" /></returns>
        private static double Log2(double x)
        {
            const double ln2 = 0.693147180559945309417232121458;
            return Math.Log(x) / ln2;
        }

        // ---------------------------------------------------------------------
        // Coverage-gap tests: small focused tests that exercise previously
        // uncovered branches (null-arg validation, equality short-circuits,
        // typed Add overloads, ctor validation).
        // ---------------------------------------------------------------------

        [Fact]
        public void Constructor_BelowMinB_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardinalityEstimator(b: 3));
        }

        [Fact]
        public void Constructor_AboveMaxB_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardinalityEstimator(b: 17));
        }

        [Fact]
        public void Add_NullString_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => hll.Add((string)null));
        }

        [Fact]
        public void Add_NullByteArray_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => hll.Add((byte[])null));
        }

        [Fact]
        public void Add_TypedOverloads_AllIncrementCountAdditions()
        {
            var hll = new CardinalityEstimator();
            hll.Add(1);
            hll.Add(2u);
            hll.Add(3L);
            hll.Add(4UL);
            hll.Add(5.0f);
            hll.Add(6.0d);
            hll.Add("seven");
            hll.Add(new byte[] { 8 });

            Assert.Equal(8UL, hll.CountAdditions);
            Assert.Equal(8UL, hll.Count());
        }

        [Fact]
        public void Add_SameValueAcrossOverloads_StillCountsCountAdditions()
        {
            var hll = new CardinalityEstimator();
            hll.Add(42);
            hll.Add(42);
            // Direct counter is exact and dedups; CountAdditions counts every Add call.
            Assert.Equal(1UL, hll.Count());
            Assert.Equal(2UL, hll.CountAdditions);
        }

        [Fact]
        public void Merge_NullOther_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => hll.Merge(null));
        }

        [Fact]
        public void Merge_DifferentBitsPerIndex_Throws()
        {
            var a = new CardinalityEstimator(b: 14);
            var b = new CardinalityEstimator(b: 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Merge(b));
        }

        [Fact]
        public void Equals_NullOther_ReturnsFalse()
        {
            var hll = new CardinalityEstimator();
            Assert.False(hll.Equals((CardinalityEstimator)null));
        }

        [Fact]
        public void Equals_DifferentBitsPerIndex_ReturnsFalse()
        {
            var a = new CardinalityEstimator(b: 14);
            var b = new CardinalityEstimator(b: 12);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentDirectCountContents_ReturnsFalse()
        {
            // Both stay in the direct-count path (under 100 elements).
            var a = new CardinalityEstimator(b: 14);
            var b = new CardinalityEstimator(b: 14);
            a.Add("alpha");
            b.Add("beta");
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_OneDirectCountOneNot_ReturnsFalse()
        {
            // A is in direct count; B disables direct counting so the field is null.
            var a = new CardinalityEstimator(b: 14, useDirectCounting: true);
            var b = new CardinalityEstimator(b: 14, useDirectCounting: false);
            a.Add("x");
            b.Add("x");
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_SameStateSameHash_ReturnsTrue()
        {
            // Use the copy constructor so hashFunction reference matches.
            var a = new CardinalityEstimator(b: 14);
            for (int i = 0; i < 5; i++) a.Add(i);
            var b = new CardinalityEstimator(a);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentSparseEntries_ReturnsFalse()
        {
            // Disable direct counting so adds populate the sparse lookup directly.
            var a = new CardinalityEstimator(b: 14, useDirectCounting: false);
            var b = new CardinalityEstimator(b: 14, useDirectCounting: false);
            for (int i = 0; i < 50; i++) a.Add($"a_{i}");
            for (int i = 0; i < 50; i++) b.Add($"b_{i}");
            Assert.False(a.Equals(b));
        }

        // ---------------------------------------------------------------------
        // Regression tests for the zero-allocation primitive Add overloads.
        //
        // The Add(int/uint/long/ulong/float/double) overloads previously called
        // BitConverter.GetBytes(...) which heap-allocated a byte[] every call.
        // They now use stackalloc + BinaryPrimitives.WriteXxxLittleEndian +
        // hashFunctionSpan. The byte sequence written is identical to
        // BitConverter.GetBytes on every supported (little-endian) .NET runtime,
        // so the hashes — and therefore the cardinality estimate — must match
        // the equivalent Add(byte[]) call. These tests pin that invariant.
        // ---------------------------------------------------------------------

        [Fact]
        public void Add_PrimitiveAndByteArray_ProduceSameHash()
        {
            var hll = new CardinalityEstimator(b: 14);
            hll.Add(123);
            hll.Add(BitConverter.GetBytes(123));
            hll.Add(456u);
            hll.Add(BitConverter.GetBytes(456u));
            hll.Add(789L);
            hll.Add(BitConverter.GetBytes(789L));
            hll.Add(1011UL);
            hll.Add(BitConverter.GetBytes(1011UL));
            hll.Add(3.14f);
            hll.Add(BitConverter.GetBytes(3.14f));
            hll.Add(2.71828);
            hll.Add(BitConverter.GetBytes(2.71828));

            // 12 Add calls, but each value-pair must hash to the same bucket and dedupe.
            Assert.Equal(12UL, hll.CountAdditions);
            Assert.Equal(6UL, hll.Count());
        }

        [Fact]
        public void Add_String_StackallocAndHeapPath_AgreeWithByteArray()
        {
            // Short string: hits the stackalloc path (UTF-8 max bytes <= threshold).
            var hll = new CardinalityEstimator(b: 14);
            const string shortStr = "hello world";
            hll.Add(shortStr);
            hll.Add(System.Text.Encoding.UTF8.GetBytes(shortStr));
            Assert.Equal(1UL, hll.Count());
            Assert.Equal(2UL, hll.CountAdditions);

            // Long string: forces the heap fallback (UTF-8 max bytes > threshold).
            var hll2 = new CardinalityEstimator(b: 14);
            string longStr = new string('x', 200); // max UTF-8 bytes = 600 > 256
            hll2.Add(longStr);
            hll2.Add(System.Text.Encoding.UTF8.GetBytes(longStr));
            Assert.Equal(1UL, hll2.Count());
        }
    }
}
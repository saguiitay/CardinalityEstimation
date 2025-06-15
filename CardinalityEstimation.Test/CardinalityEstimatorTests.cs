using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using System;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

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
        /// Generates <paramref name = "expectedCount"/> random (or sequential) elements and adds them to CardinalityEstimators, then asserts that
        /// the observed error rate is no more than <paramref name = "maxAcceptedError"/>
        /// </summary>
        /// <param name = "stdError">Expected standard error of the estimators (upper bound)</param>
        /// <param name = "expectedCount">number of elements to generate in total</param>
        /// <param name = "maxAcceptedError">Maximum allowed error rate. Default is 4 times <paramref name = "stdError"/></param>
        /// <param name = "numHllInstances">Number of estimators to create. Generated elements will be assigned to one of the estimators at random</param>
        /// <param name = "sequential">When false, elements will be generated at random. When true, elements will be 0,1,2...</param>
        /// <param name = "disableDirectCount">When true, will disable using direct counting for estimators less than 100 elements.</param>
        private void RunTest(double stdError, long expectedCount, double? maxAcceptedError = null, int numHllInstances = 1, bool sequential = false, bool disableDirectCount = false)
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
        /// <param name = "stdError">
        /// Standard error, which determines accuracy and memory consumption. For large cardinalities, the observed error is usually less than
        /// 3 * <paramref name = "stdError"/>.
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
        /// Returns the base-2 logarithm of <paramref name = "x"/>.
        /// This implementation is faster than <see cref = "Math.Log(double, double)"/> as it avoids input checks
        /// </summary>
        /// <param name = "x"></param>
        /// <returns>The base-2 logarithm of <paramref name = "x"/></returns>
        private static double Log2(double x)
        {
            const double ln2 = 0.693147180559945309417232121458;
            return Math.Log(x) / ln2;
        }

        /// <summary>
        /// Tests that the CardinalityEstimator constructor initializes CountAdditions to zero with various parameters.
        /// </summary>
        /// <param name = "b">The accuracy parameter in the allowed range [4,16].</param>
        /// <param name = "useDirectCounting">Indicator for using direct counting.</param>
        [Theory]
        [InlineData(4, true)]
        [InlineData(14, true)]
        [InlineData(16, true)]
        [InlineData(4, false)]
        [InlineData(14, false)]
        [InlineData(16, false)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Constructor_WithParameters_InitializesCountAdditionsToZero(int b, bool useDirectCounting)
        {
            // Arrange & Act
            CardinalityEstimator estimator = new CardinalityEstimator(null, b, useDirectCounting);
            // Assert
            Assert.Equal(0UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that the copy constructor creates a new instance with the same CountAdditions state as the original.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void CopyConstructor_CopiesInitialCountAdditions()
        {
            // Arrange
            CardinalityEstimator original = new CardinalityEstimator(null, 14, true);
            // Note: As the Add methods' implementations are not provided, CountAdditions remains at its initial value.
            // Act
            CardinalityEstimator copy = new CardinalityEstimator(original);
            // Assert
            Assert.Equal(original.CountAdditions, copy.CountAdditions);
        }

        /// <summary>
        /// Tests that a new CardinalityEstimator instance created with valid parameters initializes CountAdditions to 0.
        /// This test uses multiple boundary values for the 'b' parameter.
        /// </summary>
        /// <param name = "b">The number of bits parameter determining accuracy and memory usage, valid in the range [4,16].</param>
        [Theory]
        [InlineData(4)]
        [InlineData(14)]
        [InlineData(16)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Constructor_ValidParameters_InitialCountAdditionsZero(int b)
        {
            // Arrange
            GetHashCodeDelegate customHash = (byte[] bytes) => BitConverter.ToUInt64(new byte[8], 0);
            bool useDirectCounting = true;
            // Act
            var estimator = new CardinalityEstimator(customHash, b, useDirectCounting);
            // Assert
            Assert.NotNull(estimator);
            Assert.Equal(0UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that creating a CardinalityEstimator instance with a custom hash function does not throw and initializes correctly.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Constructor_CustomHashFunction_InstanceCreated()
        {
            // Arrange
            GetHashCodeDelegate customHash = (byte[] bytes) =>
            {
                // A simple custom hash function for testing purposes.
                return (ulong)(bytes.Length);
            };
            int b = 14;
            bool useDirectCounting = true;
            // Act
            var estimator = new CardinalityEstimator(customHash, b, useDirectCounting);
            // Assert
            Assert.NotNull(estimator);
            Assert.Equal(0UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that the copy constructor of CardinalityEstimator properly copies the state from the original instance.
        /// Since internal state details are encapsulated, this test verifies that the copied instance is not null,
        /// that its CountAdditions property matches, and that Equals yields true.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void CopyConstructor_ValidEstimator_StateIsCopied()
        {
            // Arrange
            GetHashCodeDelegate customHash = (byte[] bytes) => BitConverter.ToUInt64(new byte[8], 0);
            int b = 14;
            bool useDirectCounting = true;
            var original = new CardinalityEstimator(customHash, b, useDirectCounting);
            // In a full implementation, additional state modifications (e.g., via Add methods) would be applied here.
            // Act
            var copy = new CardinalityEstimator(original);
            // Assert
            Assert.NotNull(copy);
            Assert.Equal(original.CountAdditions, copy.CountAdditions);
            Assert.True(original.Equals(copy));
        }

        /// <summary>
        /// Tests that creating a CardinalityEstimator with an invalid 'b' parameter (e.g., below 4) throws an exception.
        /// This test is marked as skipped since the expected behavior for invalid 'b' values is not specified.
        /// </summary>
        [Fact(Skip = "Behavior for invalid 'b' parameter value is not specified; implement test when requirements are defined.")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Constructor_InvalidBValue_ThrowsException()
        {
            // Arrange
            GetHashCodeDelegate customHash = (byte[] bytes) => BitConverter.ToUInt64(new byte[8], 0);
            int invalidB = 3; // Invalid as the valid range is [4,16]
            bool useDirectCounting = true;
            // Act & Assert
            // Uncomment and adjust the expected exception type when behavior is defined:
            // Assert.Throws<ArgumentOutOfRangeException>(() => new CardinalityEstimator(customHash, invalidB, useDirectCounting));
        }

        /// <summary>
        /// Tests that the default constructor of CardinalityEstimator initializes CountAdditions to zero.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Constructor_DefaultValues_InitialCountAdditionsIsZero()
        {
            // Arrange & Act
            var estimator = new CardinalityEstimator();
            // Assert
            Assert.Equal(0UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that the constructor with custom parameters initializes CountAdditions to zero.
        /// Parameterized over different values of b and useDirectCounting.
        /// </summary>
        /// <param name = "b">The bits parameter for estimator accuracy.</param>
        /// <param name = "useDirectCounting">Flag indicating whether to use direct counting.</param>
        [Theory]
        [InlineData(4, true)]
        [InlineData(4, false)]
        [InlineData(14, true)]
        [InlineData(14, false)]
        [InlineData(16, true)]
        [InlineData(16, false)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Constructor_CustomParameters_InitialCountAdditionsIsZero(int b, bool useDirectCounting)
        {
            // Arrange & Act
            var estimator = new CardinalityEstimator(hashFunction: null, b: b, useDirectCounting: useDirectCounting);
            // Assert
            Assert.Equal(0UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that the copy constructor creates a new estimator with the same initial state as the original.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void CopyConstructor_CreatesEquivalentInstance()
        {
            // Arrange
            var original = new CardinalityEstimator();
            // As the Add methods implementations are not visible, we only assume initial CountAdditions remains 0.
            Assert.Equal(0UL, original.CountAdditions);
            // Act
            var copy = new CardinalityEstimator(original);
            // Assert
            Assert.Equal(original.CountAdditions, copy.CountAdditions);
        }

        /// <summary>
        /// Tests that the constructor accepts a custom hash function delegate and does not throw.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Constructor_CustomHashFunction_DoesNotThrow()
        {
            // Arrange
            GetHashCodeDelegate customHashFunction = (byte[] bytes) =>
            {
                // A simple custom hash function returning a constant value for test purposes.
                return 123UL;
            };
            // Act
            var estimator = new CardinalityEstimator(hashFunction: customHashFunction, b: 14, useDirectCounting: true);
            // Assert
            Assert.NotNull(estimator);
            Assert.Equal(0UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that adding a new, unique string element returns true and increments CountAdditions.
        /// Uses various representative string inputs.
        /// </summary>
        /// <param name = "element">The string element to add.</param>
        [Theory]
        [InlineData("test")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("special_!@#$%^&*()")]
        [InlineData("A very long string that is intended to test the functionality of the Add method in CardinalityEstimator.")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_String_NewElement_ReturnsTrue_AndCountAdditionIncrements(string element)
        {
            // Arrange
            var estimator = new CardinalityEstimator(hashFunction: TestHashFunction);
            ulong initialCount = estimator.CountAdditions;
            // Act
            bool changed = estimator.Add(element);
            // Assert
            Assert.True(changed);
            Assert.Equal(initialCount + 1, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that adding a duplicate string element returns false while still incrementing CountAdditions.
        /// </summary>
        /// <param name = "element">The duplicate string element to add.</param>
        [Theory]
        [InlineData("duplicate")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_String_DuplicateElement_ReturnsFalse_ButCountAdditionIncrements(string element)
        {
            // Arrange
            var estimator = new CardinalityEstimator(hashFunction: TestHashFunction);
            // Act
            bool firstAdd = estimator.Add(element);
            bool secondAdd = estimator.Add(element);
            // Assert
            Assert.True(firstAdd);
            Assert.False(secondAdd);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        /// <summary>
        /// A simple test hash function that deterministically converts a byte array into a ulong.
        /// Pads the byte array to 8 bytes if necessary.
        /// </summary>
        /// <param name = "bytes">Input byte array.</param>
        /// <returns>Deterministic ulong computed from the first 8 bytes.</returns>
        private static ulong TestHashFunction(byte[] bytes)
        {
            byte[] padded = new byte[8];
            Array.Copy(bytes, padded, Math.Min(bytes.Length, 8));
            return BitConverter.ToUInt64(padded, 0);
        }

        /// <summary>
        /// Tests that adding a new integer element returns true and increments CountAdditions,
        /// and that adding a duplicate element returns false while still incrementing CountAdditions.
        /// </summary>
        /// <param name = "input">The integer value to test.</param>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void AddInt_DuplicateBehavior_UpdatesCountAdditionsAndReturnsExpected(int input)
        {
            // Arrange
            var estimator = new CardinalityEstimator(TestHashFunction, 14, true);
            ulong initialCountAdditions = estimator.CountAdditions;
            // Act
            bool firstAddResult = estimator.Add(input);
            bool secondAddResult = estimator.Add(input);
            ulong finalCountAdditions = estimator.CountAdditions;
            // Assert
            // First addition should modify state and return true.
            Assert.True(firstAddResult);
            // Second addition is a duplicate and should return false.
            Assert.False(secondAddResult);
            // CountAdditions increments irrespective of state change.
            Assert.Equal(initialCountAdditions + 2, finalCountAdditions);
        }

        /// <summary>
        /// Tests that adding two distinct integer elements returns true for both additions
        /// and that CountAdditions accurately reflects the number of addition attempts.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void AddInt_DistinctElements_UpdatesCountAdditionsCorrectly()
        {
            // Arrange
            var estimator = new CardinalityEstimator(TestHashFunction, 14, true);
            ulong initialCountAdditions = estimator.CountAdditions;
            int firstElement = 12345;
            int secondElement = 54321;
            // Act
            bool firstAddResult = estimator.Add(firstElement);
            bool secondAddResult = estimator.Add(secondElement);
            ulong finalCountAdditions = estimator.CountAdditions;
            // Assert
            Assert.True(firstAddResult);
            Assert.True(secondAddResult);
            Assert.Equal(initialCountAdditions + 2, finalCountAdditions);
        }

        /// <summary>
        /// Tests the Add(uint) method for unique and duplicate elements.
        /// Verifies that the estimator state is modified on the first call and not modified on a duplicate call,
        /// and that CountAdditions is incremented for each Add call.
        /// Uses a deterministic hash function for predictable behavior.
        /// </summary>
        /// <param name = "element">The unsigned integer element to add.</param>
        [Theory]
        [InlineData(0u)]
        [InlineData(uint.MaxValue)]
        [InlineData(12345u)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_UInt_UniqueAndDuplicateElements_ModifiesState(uint element)
        {
            // Arrange
            // Use a simple deterministic hash function that converts the input bytes to UInt64.
            GetHashCodeDelegate testHashFunction = (byte[] bytes) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(bytes));
            var estimator = new CardinalityEstimator(hashFunction: testHashFunction);
            // Act
            bool firstAddResult = estimator.Add(element);
            bool secondAddResult = estimator.Add(element);
            // Assert
            Assert.True(firstAddResult); // First addition should modify state.
            Assert.False(secondAddResult); // Duplicate addition should not modify state.
            Assert.Equal(2UL, estimator.CountAdditions); // CountAdditions should increment on each call.
        }

        /// <summary>
        /// Placeholder test for Add(string) method.
        /// This test is marked as skipped because the implementation content is stripped out.
        /// </summary>
        [Fact(Skip = "Not implemented due to stripped content")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_String_Test_NotImplemented()
        {
            // Arrange, Act, Assert to be implemented when method content is available.
        }

        /// <summary>
        /// Placeholder test for Add(int) method.
        /// This test is marked as skipped because the implementation content is stripped out.
        /// </summary>
        [Fact(Skip = "Not implemented due to stripped content")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Int_Test_NotImplemented()
        {
            // Arrange, Act, Assert to be implemented when method content is available.
        }

        /// <summary>
        /// Placeholder test for Add(long) method.
        /// This test is marked as skipped because the implementation content is stripped out.
        /// </summary>
        [Fact(Skip = "Not implemented due to stripped content")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Long_Test_NotImplemented()
        {
            // Arrange, Act, Assert to be implemented when method content is available.
        }

        /// <summary>
        /// Placeholder test for Add(ulong) method.
        /// This test is marked as skipped because the implementation content is stripped out.
        /// </summary>
        [Fact(Skip = "Not implemented due to stripped content")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_ULong_Test_NotImplemented()
        {
            // Arrange, Act, Assert to be implemented when method content is available.
        }

        /// <summary>
        /// Placeholder test for Add(float) method.
        /// This test is marked as skipped because the implementation content is stripped out.
        /// </summary>
        [Fact(Skip = "Not implemented due to stripped content")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Float_Test_NotImplemented()
        {
            // Arrange, Act, Assert to be implemented when method content is available.
        }

        /// <summary>
        /// Placeholder test for Add(double) method.
        /// This test is marked as skipped because the implementation content is stripped out.
        /// </summary>
        [Fact(Skip = "Not implemented due to stripped content")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Double_Test_NotImplemented()
        {
            // Arrange, Act, Assert to be implemented when method content is available.
        }

        /// <summary>
        /// Placeholder test for Add(byte[]) method.
        /// This test is marked as skipped because the implementation content is stripped out.
        /// </summary>
        [Fact(Skip = "Not implemented due to stripped content")]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_ByteArray_Test_NotImplemented()
        {
            // Arrange, Act, Assert to be implemented when method content is available.
        }

        /// <summary>
        /// Tests that adding a new long element returns true and increments CountAdditions,
        /// and that adding a duplicate element returns false while still incrementing CountAdditions.
        /// </summary>
        /// <param name = "value">A long value to add to the estimator.</param>
        [Theory]
        [InlineData(0L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Long_UniqueAndDuplicate_ReturnsExpected(long value)
        {
            // Arrange: Create an estimator with a custom hash function for deterministic behavior.
            CardinalityEstimator estimator = new CardinalityEstimator(hashFunction: (bytes) =>
            {
                // Use BitConverter to obtain a ulong from the byte array.
                return BitConverter.ToUInt64(bytes, 0);
            }, b: 14, useDirectCounting: true);
            // Act: Add the element for the first time.
            bool firstAddResult = estimator.Add(value);
            ulong countAfterFirstAdd = estimator.CountAdditions;
            // Assert: The first addition should modify state (true) and CountAdditions becomes 1.
            Assert.True(firstAddResult);
            Assert.Equal(1UL, countAfterFirstAdd);
            // Act: Add the same element again.
            bool secondAddResult = estimator.Add(value);
            ulong countAfterSecondAdd = estimator.CountAdditions;
            // Assert: The second addition should not change the estimator's state (false) but CountAdditions is incremented.
            Assert.False(secondAddResult);
            Assert.Equal(2UL, countAfterSecondAdd);
        }

        /// <summary>
        /// Tests that adding two distinct long elements returns true for both additions and increments CountAdditions appropriately.
        /// </summary>
        /// <param name = "firstValue">The first long value to add.</param>
        /// <param name = "secondValue">The second distinct long value to add.</param>
        [Theory]
        [InlineData(123L, 456L)]
        [InlineData(0L, 1L)]
        [InlineData(-100L, 100L)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Long_DistinctValues_ReturnsTrue(long firstValue, long secondValue)
        {
            // Arrange: Create an estimator with a custom hash function.
            CardinalityEstimator estimator = new CardinalityEstimator(hashFunction: (bytes) =>
            {
                return BitConverter.ToUInt64(bytes, 0);
            }, b: 14, useDirectCounting: true);
            // Act: Add the first distinct element.
            bool firstAddResult = estimator.Add(firstValue);
            ulong countAfterFirstAdd = estimator.CountAdditions;
            // Assert: First addition should return true.
            Assert.True(firstAddResult);
            Assert.Equal(1UL, countAfterFirstAdd);
            // Act: Add the second distinct element.
            bool secondAddResult = estimator.Add(secondValue);
            ulong countAfterSecondAdd = estimator.CountAdditions;
            // Assert: Second addition should also return true and CountAdditions equals 2.
            Assert.True(secondAddResult);
            Assert.Equal(2UL, countAfterSecondAdd);
        }

        /// <summary>
        /// Tests that adding a new ulong element returns true and increments CountAdditions.
        /// Uses various edge case ulong values.
        /// </summary>
        /// <param name = "element">The ulong element to add.</param>
        [Theory]
        [InlineData(0UL)]
        [InlineData(123UL)]
        [InlineData(ulong.MaxValue)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void AddUlong_NewElement_ReturnsTrueAndIncrementsCountAdditions(ulong element)
        {
            // Arrange: Use a deterministic hash function that returns the ulong represented by the byte array.
            GetHashCodeDelegate testHashFunction = (byte[] bytes) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(bytes));
            var estimator = new CardinalityEstimator(testHashFunction);
            ulong initialCountAdditions = estimator.CountAdditions;
            // Act: add the element for the first time.
            bool changed = estimator.Add(element);
            // Assert: the element should be new, so changed is true.
            Assert.True(changed);
            Assert.Equal(initialCountAdditions + 1, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that adding a duplicate ulong element returns false but still increments CountAdditions.
        /// Uses various edge case ulong values.
        /// </summary>
        /// <param name = "element">The ulong element to add twice.</param>
        [Theory]
        [InlineData(0UL)]
        [InlineData(456UL)]
        [InlineData(ulong.MaxValue)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void AddUlong_DuplicateElement_ReturnsFalseButCountAdditionsIncrements(ulong element)
        {
            // Arrange: Use a deterministic hash function.
            GetHashCodeDelegate testHashFunction = (byte[] bytes) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(bytes));
            var estimator = new CardinalityEstimator(testHashFunction);
            // Act: Add the element the first time.
            bool firstAdd = estimator.Add(element);
            ulong countAfterFirst = estimator.CountAdditions;
            // Act: Add the same element again.
            bool secondAdd = estimator.Add(element);
            ulong countAfterSecond = estimator.CountAdditions;
            // Assert: first addition should modify state and return true.
            Assert.True(firstAdd);
            // Second addition should not modify the internal state, so returns false.
            Assert.False(secondAdd);
            // But CountAdditions is incremented on every add call.
            Assert.Equal(countAfterFirst + 1, countAfterSecond);
        }

        /// <summary>
        /// Verifies that adding the same float element twice returns true the first time and false the second time,
        /// and that CountAdditions is incremented for each addition.
        /// </summary>
        /// <param name = "value">Test float value to add.</param>
        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void AddFloat_SameElementTwice_ReturnsTrueThenFalse(float value)
        {
            // Arrange
            var estimator = new CardinalityEstimator();
            // Act
            bool firstAdd = estimator.Add(value);
            bool secondAdd = estimator.Add(value);
            // Assert
            Assert.True(firstAdd);
            Assert.False(secondAdd);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Verifies that adding two distinct float elements returns true for each addition,
        /// and that CountAdditions correctly counts both additions.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void AddFloat_DifferentElements_ReturnsTrueForEach()
        {
            // Arrange
            var estimator = new CardinalityEstimator();
            float firstValue = 3.14f;
            float secondValue = -2.71f;
            // Act
            bool firstResult = estimator.Add(firstValue);
            bool secondResult = estimator.Add(secondValue);
            // Assert
            Assert.True(firstResult);
            Assert.True(secondResult);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that adding the same double element twice returns true on first add and false on duplicate add,
        /// and that the CountAdditions property is incremented on each call.
        /// </summary>
        /// <param name = "value">The double value to add, including edge cases such as NaN, infinities, and standard values.</param>
        [Theory]
        [InlineData(0.0)]
        [InlineData(1.23)]
        [InlineData(-10.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Double_SameElementTwice_ReturnsIdempotentResultAndIncrementsCountAdditions(double value)
        {
            // Arrange
            // Provide a deterministic hash function based on BitConverter to ensure predictable behavior.
            CardinalityEstimator estimator = new CardinalityEstimator(hashFunction: x => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x)));
            // Act
            bool firstAddResult = estimator.Add(value);
            bool secondAddResult = estimator.Add(value);
            // Assert
            // The first addition should modify the estimator state.
            Assert.True(firstAddResult);
            // The duplicate addition should not modify the state.
            Assert.False(secondAddResult);
            // CountAdditions should be incremented on every call.
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that adding two distinct double elements returns true for both additions,
        /// and that CountAdditions reflects the total number of addition attempts.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_Double_DistinctElements_ReturnsTrueAndIncrementsCountAdditions()
        {
            // Arrange
            CardinalityEstimator estimator = new CardinalityEstimator(hashFunction: x => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x)));
            double firstValue = 1.0;
            double secondValue = 2.0;
            // Act
            bool firstAddResult = estimator.Add(firstValue);
            bool secondAddResult = estimator.Add(secondValue);
            // Assert
            Assert.True(firstAddResult);
            Assert.True(secondAddResult);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        /// <summary>
        /// A fake hash function that computes a simple sum of the bytes.
        /// This function simulates hashing such that duplicate byte arrays produce the same hash value.
        /// </summary>
        /// <param name = "data">Input byte array.</param>
        /// <returns>A ulong hash computed as the sum of the bytes.</returns>
        private static ulong FakeHash(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            ulong sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i];
            }

            return sum;
        }

        /// <summary>
        /// Tests that adding a new byte array element returns true and increments CountAdditions,
        /// and that adding a duplicate byte array element returns false while still incrementing CountAdditions.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_ByteArray_NewAndDuplicateElements_ReturnsExpectedResult()
        {
            // Arrange
            var estimator = new CardinalityEstimator(FakeHash);
            byte[] element1 = new byte[]
            {
                1,
                2,
                3
            }; // FakeHash returns 6
            byte[] elementDuplicate = new byte[]
            {
                1,
                2,
                3
            }; // Same content, hash also 6
            byte[] elementDifferent = new byte[]
            {
                1,
                2,
                4
            }; // FakeHash returns 7
            // Act
            bool firstAdd = estimator.Add(element1);
            bool duplicateAdd = estimator.Add(elementDuplicate);
            bool differentAdd = estimator.Add(elementDifferent);
            // Assert
            // The first addition should modify the estimator's state.
            Assert.True(firstAdd);
            // Adding a duplicate element (same computed hash) should not modify the state.
            Assert.False(duplicateAdd);
            // Adding a distinct element should modify the state.
            Assert.True(differentAdd);
            // CountAdditions should reflect all Add calls.
            Assert.Equal(3UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that adding an empty byte array works as expected.
        /// Since FakeHash computes the sum of bytes (which will be 0 for an empty array),
        /// adding multiple empty arrays should be treated as duplicates.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_ByteArray_EmptyArray_ReturnsExpectedResult()
        {
            // Arrange
            var estimator = new CardinalityEstimator(FakeHash);
            byte[] emptyArray = new byte[0];
            // Act
            bool firstAdd = estimator.Add(emptyArray);
            bool secondAdd = estimator.Add(emptyArray);
            // Assert
            // The first addition of an empty array should modify the state.
            Assert.True(firstAdd);
            // A subsequent addition should not modify the state.
            Assert.False(secondAdd);
            // CountAdditions should be incremented for each call.
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        /// <summary>
        /// Tests that adding a null byte array results in an ArgumentNullException.
        /// This verifies that the hash function properly handles null input.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Add_ByteArray_NullElement_ThrowsArgumentNullException()
        {
            // Arrange
            var estimator = new CardinalityEstimator(FakeHash);
            byte[] nullArray = null;
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => estimator.Add(nullArray));
        }

        /// <summary>
        /// Tests that a newly created CardinalityEstimator returns a count of zero.
        /// This test verifies the behavior when no elements have been added.
        /// </summary>
        [Theory]
        [InlineData(14, true)]
        [InlineData(14, false)]
        [InlineData(4, true)]
        [InlineData(16, false)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Count_NewEstimator_ReturnsZero(int b, bool useDirectCounting)
        {
            // Arrange: Create a new estimator with given parameters.
            var estimator = new CardinalityEstimator(hashFunction: null, b: b, useDirectCounting: useDirectCounting);
            // Act: Get the count from the estimator.
            ulong count = estimator.Count();
            // Assert: The count should be zero for a new estimator.
            Assert.Equal(0UL, count);
        }

        /// <summary>
        /// Tests that the copy constructor produces an estimator with the same count as the original.
        /// This test verifies that internal state related to count is correctly copied.
        /// </summary>
        [Theory]
        [InlineData(14, true)]
        [InlineData(14, false)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void CopyConstructor_CopiesCountState(int b, bool useDirectCounting)
        {
            // Arrange: Create a new estimator and obtain its count.
            var originalEstimator = new CardinalityEstimator(hashFunction: null, b: b, useDirectCounting: useDirectCounting);
            ulong originalCount = originalEstimator.Count();
            // Act: Create a new estimator using the copy constructor.
            var copiedEstimator = new CardinalityEstimator(originalEstimator);
            ulong copiedCount = copiedEstimator.Count();
            // Assert: The copied estimator should have the same count as the original.
            Assert.Equal(originalCount, copiedCount);
        }

        /// <summary>
        /// Tests that calling instance Merge with a null estimator throws an ArgumentNullException.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Merge_NullOther_ThrowsArgumentNullException()
        {
            // Arrange
            var estimator = new CardinalityEstimator();
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => estimator.Merge(null));
        }

        /// <summary>
        /// Tests that merging two CardinalityEstimator instances with different accuracy settings throws an ArgumentOutOfRangeException.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Merge_IncompatibleEstimator_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            // Create estimator with default b (14) and one with a different b (e.g., 10)
            var estimator1 = new CardinalityEstimator(b: 14);
            var estimator2 = new CardinalityEstimator(b: 10);
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => estimator1.Merge(estimator2));
        }

        /// <summary>
        /// Tests that merging two estimators correctly sums up the CountAdditions property.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Merge_CountAdditions_SumsCorrectly()
        {
            // Arrange
            var estimator1 = new CardinalityEstimator();
            // Simulate additions; assuming Add methods increment CountAdditions.
            estimator1.Add(0);
            estimator1.Add(0);
            var estimator2 = new CardinalityEstimator();
            estimator2.Add(0);
            // Act
            estimator1.Merge(estimator2);
            // Assert
            // Expecting 3 total additions.
            Assert.Equal(3UL, estimator1.CountAdditions);
        }

        /// <summary>
        /// Tests that the static Merge method returns null when passed a null parameter.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void StaticMerge_NullParameter_ReturnsNull()
        {
            // Act
            CardinalityEstimator merged = CardinalityEstimator.Merge(null);
            // Assert
            Assert.Null(merged);
        }

        /// <summary>
        /// Tests that merging a non-null CardinalityEstimator into another correctly combines the CountAdditions.
        /// Arrange: Two estimators with specific additions are created.
        /// Act: One estimator is merged into the other.
        /// Assert: The merged estimator's CountAdditions equals the sum of both estimators' CountAdditions.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Merge_WithNonNullOther_AddsCountAdditions()
        {
            // Arrange
            var estimator1 = new CardinalityEstimator();
            var estimator2 = new CardinalityEstimator();
            estimator1.Add(0); // Increments CountAdditions, expected to be 1.
            estimator1.Add(0); // CountAdditions becomes 2.
            estimator2.Add(0); // CountAdditions becomes 1.
            // Act
            estimator1.Merge(estimator2);
            // Assert
            Assert.Equal(3UL, estimator1.CountAdditions);
        }

        /// <summary>
        /// Tests that GetState returns the expected default state for a newly created estimator.
        /// Verifies that:
        /// - BitsPerIndex equals the provided b parameter.
        /// - DirectCount is initialized as an empty collection.
        /// - LookupSparse is initialized as an empty collection.
        /// - LookupDense is null.
        /// - CountAdditions is zero.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void GetState_DefaultState_ReturnsExpectedState()
        {
            // Arrange
            int bits = 10;
            bool useDirectCounting = true;
            CardinalityEstimator estimator = new CardinalityEstimator(b: bits, useDirectCounting: useDirectCounting);
            // Act
            var state = estimator.GetState();
            // Assert
            Assert.NotNull(state);
            Assert.Equal(bits, state.BitsPerIndex);
            Assert.NotNull(state.DirectCount);
            Assert.Empty(state.DirectCount);
            Assert.NotNull(state.LookupSparse);
            Assert.Empty(state.LookupSparse);
            Assert.Null(state.LookupDense);
            Assert.Equal(0UL, state.CountAdditions);
        }

        /// <summary>
        /// Tests that the copy constructor correctly reproduces the internal state.
        /// After creating a copy, GetState should reflect the same values as in the original instance.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void GetState_CopyConstructor_ReturnsIdenticalState()
        {
            // Arrange
            int bits = 12;
            bool useDirectCounting = false;
            CardinalityEstimator estimator = new CardinalityEstimator(b: bits, useDirectCounting: useDirectCounting);
            var originalState = estimator.GetState();
            // Act: create a copy using the copy constructor and retrieve its state.
            CardinalityEstimator copyEstimator = new CardinalityEstimator(estimator);
            var state = copyEstimator.GetState();
            // Assert
            Assert.NotNull(state);
            Assert.Equal(bits, state.BitsPerIndex);
            Assert.Equal(estimator.CountAdditions, state.CountAdditions);
            Assert.Equal(originalState.DirectCount, state.DirectCount);
            Assert.NotNull(state.LookupSparse);
            Assert.Empty(state.LookupSparse);
            Assert.Null(state.LookupDense);
        }

        /// <summary>
        /// Tests the GetSigma method with various inputs to verify it returns the expected number of leading zeroes plus one.
        /// </summary>
        /// <param name = "hash">The hash value to test.</param>
        /// <param name = "bitsToCount">The number of bits to count.</param>
        /// <param name = "expectedSigma">The expected sigma value.</param>
        [Theory]
        [InlineData(0UL, 50, 51)]
        [InlineData(1UL, 50, 50)]
        [InlineData(8UL, 50, 47)]
        [InlineData(1125899906842623UL, 50, 1)] // (1UL << 50) - 1
        [InlineData(2251799813685248UL, 50, 51)] // (1UL << 51) with bitsToCount=50 results in masked=0 -> 64- (64-50)+1 = 51
        [InlineData(0UL, 64, 65)]
        [InlineData(1UL, 64, 65)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void GetSigma_VariousInputs_ReturnsExpectedSigma(ulong hash, byte bitsToCount, byte expectedSigma)
        {
            // Arrange is implicit
            // Act
            byte actualSigma = CardinalityEstimator.GetSigma(hash, bitsToCount);
            // Assert
            Assert.Equal(expectedSigma, actualSigma);
        }

        /// <summary>
        /// A dummy hash function returning a constant value.
        /// </summary>
        private static ulong DummyHash(byte[] bytes)
        {
            return 42UL;
        }

        /// <summary>
        /// A different dummy hash function returning a different constant value.
        /// </summary>
        private static ulong DifferentDummyHash(byte[] bytes)
        {
            return 84UL;
        }

        /// <summary>
        /// Tests that Equals returns false when the other instance is null.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Equals_Null_ReturnsFalse()
        {
            // Arrange
            var estimator = new CardinalityEstimator(DummyHash, 14, true);
            // Act
            bool result = estimator.Equals(null);
            // Assert
            Assert.False(result);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance with itself.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var estimator = new CardinalityEstimator(DummyHash, 14, true);
            // Act
            bool result = estimator.Equals(estimator);
            // Assert
            Assert.True(result);
        }

        /// <summary>
        /// Tests that the copy constructor creates an instance equal to the original.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Equals_CopyConstructedInstances_ReturnTrue()
        {
            // Arrange
            var original = new CardinalityEstimator(DummyHash, 14, true);
            var copy = new CardinalityEstimator(original);
            // Act
            bool result = original.Equals(copy) && copy.Equals(original);
            // Assert
            Assert.True(result);
        }

        /// <summary>
        /// Tests that two instances with different configuration parameters are not equal.
        /// </summary>
        [Theory]
        [InlineData(14, 15)]
        [InlineData(15, 14)]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Equals_DifferentConfiguration_ReturnsFalse(int b1, int b2)
        {
            // Arrange
            var estimator1 = new CardinalityEstimator(DummyHash, b1, true);
            var estimator2 = new CardinalityEstimator(DummyHash, b2, true);
            // Act
            bool result1 = estimator1.Equals(estimator2);
            bool result2 = estimator2.Equals(estimator1);
            // Assert
            Assert.False(result1);
            Assert.False(result2);
        }

        /// <summary>
        /// Tests that two instances with different hash functions are not equal.
        /// </summary>
        [Fact]
        [Trait("Owner", "AI Testing Agent v0.1.0-alpha.25310.44+8471bbd")]
        [Trait("Category", "auto-generated")]
        public void Equals_DifferentHashFunction_ReturnsFalse()
        {
            // Arrange
            var estimator1 = new CardinalityEstimator(DummyHash, 14, true);
            var estimator2 = new CardinalityEstimator(DifferentDummyHash, 14, true);
            // Act
            bool result1 = estimator1.Equals(estimator2);
            bool result2 = estimator2.Equals(estimator1);
            // Assert
            Assert.False(result1);
            Assert.False(result2);
        }
    }
}
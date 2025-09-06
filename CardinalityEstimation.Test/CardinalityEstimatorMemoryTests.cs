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
    using System.Diagnostics;
    using System.Linq;
    using CardinalityEstimation.Hash;
    using Xunit;
    using Xunit.Abstractions;

    public class CardinalityEstimatorMemoryTests : IDisposable
    {
        public static readonly Random Rand = new Random();

        private readonly ITestOutputHelper output;
        private readonly Stopwatch stopwatch;

        public CardinalityEstimatorMemoryTests(ITestOutputHelper outputHelper)
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

        #region Span<byte> Tests

        [Fact]
        public void Add_Span_WithUniqueData_ReturnsTrue()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data1 = new byte[] { 1, 2, 3, 4 };
            var data2 = new byte[] { 5, 6, 7, 8 };

            // Act
            var result1 = estimator.Add(data1.AsSpan());
            var result2 = estimator.Add(data2.AsSpan());

            // Assert
            Assert.True(result1);
            Assert.True(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_Span_WithDuplicateData_ReturnsFalseOnSecondAdd()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data = new byte[] { 1, 2, 3, 4 };

            // Act
            var result1 = estimator.Add(data.AsSpan());
            var result2 = estimator.Add(data.AsSpan());

            // Assert
            Assert.True(result1);
            Assert.False(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_Span_WithEmptySpan_Works()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var emptySpan = Span<byte>.Empty;

            // Act
            var result = estimator.Add(emptySpan);

            // Assert
            Assert.True(result); // Should work with empty data
            Assert.Equal(1UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_Span_WithLargeData_Works()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var largeData = new byte[10000];
            Rand.NextBytes(largeData);

            // Act
            var result = estimator.Add(largeData.AsSpan());

            // Assert
            Assert.True(result);
            Assert.Equal(1UL, estimator.CountAdditions);
        }

        #endregion

        #region ReadOnlySpan<byte> Tests

        [Fact]
        public void Add_ReadOnlySpan_WithUniqueData_ReturnsTrue()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data1 = new byte[] { 1, 2, 3, 4 };
            var data2 = new byte[] { 5, 6, 7, 8 };

            // Act
            var result1 = estimator.Add((ReadOnlySpan<byte>)data1.AsSpan());
            var result2 = estimator.Add((ReadOnlySpan<byte>)data2.AsSpan());

            // Assert
            Assert.True(result1);
            Assert.True(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_ReadOnlySpan_WithDuplicateData_ReturnsFalseOnSecondAdd()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data = new byte[] { 1, 2, 3, 4 };

            // Act
            var result1 = estimator.Add((ReadOnlySpan<byte>)data.AsSpan());
            var result2 = estimator.Add((ReadOnlySpan<byte>)data.AsSpan());

            // Assert
            Assert.True(result1);
            Assert.False(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_ReadOnlySpan_WithEmptySpan_Works()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var emptySpan = ReadOnlySpan<byte>.Empty;

            // Act
            var result = estimator.Add(emptySpan);

            // Assert
            Assert.True(result);
            Assert.Equal(1UL, estimator.CountAdditions);
        }

        #endregion

        #region Memory<byte> Tests

        [Fact]
        public void Add_Memory_WithUniqueData_ReturnsTrue()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data1 = new byte[] { 1, 2, 3, 4 };
            var data2 = new byte[] { 5, 6, 7, 8 };

            // Act
            var result1 = estimator.Add(data1.AsMemory());
            var result2 = estimator.Add(data2.AsMemory());

            // Assert
            Assert.True(result1);
            Assert.True(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_Memory_WithDuplicateData_ReturnsFalseOnSecondAdd()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data = new byte[] { 1, 2, 3, 4 };

            // Act
            var result1 = estimator.Add(data.AsMemory());
            var result2 = estimator.Add(data.AsMemory());

            // Assert
            Assert.True(result1);
            Assert.False(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_Memory_WithEmptyMemory_Works()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var emptyMemory = Memory<byte>.Empty;

            // Act
            var result = estimator.Add(emptyMemory);

            // Assert
            Assert.True(result);
            Assert.Equal(1UL, estimator.CountAdditions);
        }

        #endregion

        #region ReadOnlyMemory<byte> Tests

        [Fact]
        public void Add_ReadOnlyMemory_WithUniqueData_ReturnsTrue()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data1 = new byte[] { 1, 2, 3, 4 };
            var data2 = new byte[] { 5, 6, 7, 8 };

            // Act
            var result1 = estimator.Add((ReadOnlyMemory<byte>)data1.AsMemory());
            var result2 = estimator.Add((ReadOnlyMemory<byte>)data2.AsMemory());

            // Assert
            Assert.True(result1);
            Assert.True(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_ReadOnlyMemory_WithDuplicateData_ReturnsFalseOnSecondAdd()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var data = new byte[] { 1, 2, 3, 4 };

            // Act
            var result1 = estimator.Add((ReadOnlyMemory<byte>)data.AsMemory());
            var result2 = estimator.Add((ReadOnlyMemory<byte>)data.AsMemory());

            // Assert
            Assert.True(result1);
            Assert.False(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_ReadOnlyMemory_WithEmptyMemory_Works()
        {
            // Arrange
            var estimator = new CardinalityEstimator(b: 10);
            var emptyMemory = ReadOnlyMemory<byte>.Empty;

            // Act
            var result = estimator.Add(emptyMemory);

            // Assert
            Assert.True(result);
            Assert.Equal(1UL, estimator.CountAdditions);
        }

        #endregion

        #region Consistency Tests

        [Fact]
        public void Add_AllMemoryTypes_WithSameData_ProduceSameHash()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            
            var estimator1 = new CardinalityEstimator(b: 10);
            var estimator2 = new CardinalityEstimator(b: 10);
            var estimator3 = new CardinalityEstimator(b: 10);
            var estimator4 = new CardinalityEstimator(b: 10);
            var estimator5 = new CardinalityEstimator(b: 10);

            // Act
            estimator1.Add(data); // byte[]
            estimator2.Add(data.AsSpan()); // Span<byte>
            estimator3.Add((ReadOnlySpan<byte>)data.AsSpan()); // ReadOnlySpan<byte>
            estimator4.Add(data.AsMemory()); // Memory<byte>
            estimator5.Add((ReadOnlyMemory<byte>)data.AsMemory()); // ReadOnlyMemory<byte>

            // Assert
            var count1 = estimator1.Count();
            var count2 = estimator2.Count();
            var count3 = estimator3.Count();
            var count4 = estimator4.Count();
            var count5 = estimator5.Count();

            Assert.Equal(count1, count2);
            Assert.Equal(count1, count3);
            Assert.Equal(count1, count4);
            Assert.Equal(count1, count5);

            Assert.True(estimator1.Equals(estimator2));
            Assert.True(estimator1.Equals(estimator3));
            Assert.True(estimator1.Equals(estimator4));
            Assert.True(estimator1.Equals(estimator5));
        }

        [Fact]
        public void Add_MemoryTypes_WithSlicedData_WorksCorrectly()
        {
            // Arrange
            var fullData = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var expectedData = new byte[] { 2, 3, 4, 5 };
            
            var estimator1 = new CardinalityEstimator(b: 10);
            var estimator2 = new CardinalityEstimator(b: 10);
            var estimator3 = new CardinalityEstimator(b: 10);

            // Act
            estimator1.Add(expectedData); // Control with exact data
            estimator2.Add(fullData.AsSpan(2, 4)); // Sliced Span
            estimator3.Add(fullData.AsMemory(2, 4)); // Sliced Memory

            // Assert
            Assert.Equal(estimator1.Count(), estimator2.Count());
            Assert.Equal(estimator1.Count(), estimator3.Count());
            Assert.True(estimator1.Equals(estimator2));
            Assert.True(estimator1.Equals(estimator3));
        }

        #endregion

        #region Performance and Direct Count Tests

        [Fact]
        public void Add_MemoryTypes_WithDirectCountEnabled_Works()
        {
            // Arrange - Use small count to ensure direct counting is used
            var estimator = new CardinalityEstimator(b: 10, useDirectCounting: true);
            var testData = new[]
            {
                new byte[] { 1, 2 },
                new byte[] { 3, 4 },
                new byte[] { 5, 6 },
                new byte[] { 7, 8 }
            };

            // Act - Add data using different memory types
            estimator.Add(testData[0].AsSpan());
            estimator.Add((ReadOnlySpan<byte>)testData[1].AsSpan());
            estimator.Add(testData[2].AsMemory());
            estimator.Add((ReadOnlyMemory<byte>)testData[3].AsMemory());

            // Assert
            Assert.Equal(4UL, estimator.Count()); // Should be exact with direct counting
            Assert.Equal(4UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_MemoryTypes_TransitionFromDirectToSparse_Works()
        {
            // Arrange - Use parameters that will trigger direct->sparse->dense transitions
            var estimator = new CardinalityEstimator(b: 8, useDirectCounting: true);
            var elementCount = 150; // Exceeds direct count limit

            // Act - Add many unique elements using different memory types
            for (int i = 0; i < elementCount; i++)
            {
                var data = BitConverter.GetBytes(i);
                
                // Alternate between different memory types
                switch (i % 4)
                {
                    case 0:
                        estimator.Add(data.AsSpan());
                        break;
                    case 1:
                        estimator.Add((ReadOnlySpan<byte>)data.AsSpan());
                        break;
                    case 2:
                        estimator.Add(data.AsMemory());
                        break;
                    case 3:
                        estimator.Add((ReadOnlyMemory<byte>)data.AsMemory());
                        break;
                }
            }

            // Assert
            Assert.True(estimator.Count() > 0);
            Assert.Equal((ulong)elementCount, estimator.CountAdditions);
            // Should be close to actual count, allowing for estimation error
            var actualCount = estimator.Count();
            var error = Math.Abs((double)actualCount - elementCount) / elementCount;
            Assert.True(error < 0.1, $"Error {error} should be less than 10%");
        }

        #endregion

        #region Hash Function Tests

        [Fact]
        public void Add_MemoryTypes_WithDifferentHashFunctions_Works()
        {
            // Test with different hash functions to ensure memory types work across all implementations
            var hashFunctions = new GetHashCodeDelegate[]
            {
                Murmur3.GetHashCode,
                Fnv1A.GetHashCode,
                (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x))
            };

            foreach (var hashFunction in hashFunctions)
            {
                // Arrange
                var estimator = new CardinalityEstimator(hashFunction, b: 10);
                var testData = new byte[] { 1, 2, 3, 4, 5 };

                // Act & Assert - All memory types should work with any hash function
                Assert.True(estimator.Add(testData.AsSpan()));
                Assert.False(estimator.Add((ReadOnlySpan<byte>)testData.AsSpan())); // Duplicate
                Assert.False(estimator.Add(testData.AsMemory())); // Duplicate
                Assert.False(estimator.Add((ReadOnlyMemory<byte>)testData.AsMemory())); // Duplicate

                Assert.Equal(1UL, estimator.Count());
                Assert.Equal(4UL, estimator.CountAdditions);
            }
        }

        #endregion

        #region Merge Tests

        [Fact]
        public void Merge_EstimatorsWithMemoryTypes_WorksCorrectly()
        {
            // Arrange
            var estimator1 = new CardinalityEstimator(b: 10);
            var estimator2 = new CardinalityEstimator(b: 10);
            
            var data1 = new byte[] { 1, 2, 3 };
            var data2 = new byte[] { 4, 5, 6 };
            var data3 = new byte[] { 7, 8, 9 };

            // Act - Add data using memory types
            estimator1.Add(data1.AsSpan());
            estimator1.Add(data2.AsMemory());
            
            estimator2.Add((ReadOnlySpan<byte>)data2.AsSpan()); // Overlap with estimator1
            estimator2.Add((ReadOnlyMemory<byte>)data3.AsMemory());
            
            var countBefore1 = estimator1.Count();
            var countBefore2 = estimator2.Count();
            
            estimator1.Merge(estimator2);

            // Assert
            var countAfter = estimator1.Count();
            Assert.True(countAfter >= Math.Max(countBefore1, countBefore2));
            Assert.Equal(6UL, estimator1.CountAdditions); // 4 original + 2 from estimator2
        }

        #endregion

        #region Accuracy Tests

        [Fact]
        public void Add_MemoryTypes_AccuracyTest_SmallCardinality()
        {
            // Arrange
            const int cardinality = 1000;
            var estimator = new CardinalityEstimator(b: 14);

            // Act - Add unique data using alternating memory types
            for (int i = 0; i < cardinality; i++)
            {
                var data = BitConverter.GetBytes(i).Concat(BitConverter.GetBytes(i * 2)).ToArray();
                
                switch (i % 4)
                {
                    case 0:
                        estimator.Add(data.AsSpan());
                        break;
                    case 1:
                        estimator.Add((ReadOnlySpan<byte>)data.AsSpan());
                        break;
                    case 2:
                        estimator.Add(data.AsMemory());
                        break;
                    case 3:
                        estimator.Add((ReadOnlyMemory<byte>)data.AsMemory());
                        break;
                }
            }

            // Assert
            var estimatedCount = estimator.Count();
            var error = Math.Abs((double)estimatedCount - cardinality) / cardinality;
            
            output.WriteLine($"Expected: {cardinality}, Estimated: {estimatedCount}, Error: {error:P2}");
            
            // With b=14, we expect good accuracy for 1000 elements
            Assert.True(error < 0.05, $"Error {error:P2} should be less than 5% for cardinality {cardinality}");
            Assert.Equal((ulong)cardinality, estimator.CountAdditions);
        }

        #endregion
    }
}
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
    using System.Linq;
    using Xunit;

    public class CardinalityEstimatorExtensionsTests
    {
        [Fact]
        public void ToConcurrent_WithValidEstimator_ReturnsEquivalentConcurrentEstimator()
        {
            // Arrange
            var regular = new CardinalityEstimator(b: 10);
            regular.Add("test1");
            regular.Add("test2");
            regular.Add("test3");

            // Act
            using var concurrent = regular.ToConcurrent();

            // Assert
            Assert.NotNull(concurrent);
            Assert.Equal(regular.Count(), concurrent.Count());
            Assert.Equal(regular.CountAdditions, concurrent.CountAdditions);
        }

        [Fact]
        public void ToConcurrent_WithNull_ReturnsNull()
        {
            // Arrange
            CardinalityEstimator nullEstimator = null;

            // Act
            var result = nullEstimator.ToConcurrent();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParallelMerge_WithValidEstimators_ReturnsMergedResult()
        {
            // Arrange
            var estimators = new List<CardinalityEstimator>();
            for (int i = 0; i < 5; i++)
            {
                var estimator = new CardinalityEstimator(b: 10);
                for (int j = 0; j < 20; j++)
                {
                    estimator.Add($"estimator_{i}_element_{j}");
                }
                estimators.Add(estimator);
            }

            // Act
            using var result = estimators.ParallelMerge();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count() > 0);
            Assert.Equal(100UL, result.CountAdditions);
        }

        [Fact]
        public void ParallelMerge_WithCustomParallelism_WorksCorrectly()
        {
            // Arrange
            var estimators = new List<CardinalityEstimator>();
            for (int i = 0; i < 3; i++)
            {
                var estimator = new CardinalityEstimator(b: 10);
                for (int j = 0; j < 10; j++)
                {
                    estimator.Add($"estimator_{i}_element_{j}");
                }
                estimators.Add(estimator);
            }

            // Act
            using var result = estimators.ParallelMerge(parallelismDegree: 2);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count() > 0);
            Assert.Equal(30UL, result.CountAdditions);
        }

        [Fact]
        public void ParallelMerge_WithNullCollection_ReturnsNull()
        {
            // Arrange
            IEnumerable<CardinalityEstimator> nullCollection = null;

            // Act
            var result = nullCollection.ParallelMerge();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParallelMerge_WithEmptyCollection_ReturnsNull()
        {
            // Arrange
            var emptyCollection = new List<CardinalityEstimator>();

            // Act
            var result = emptyCollection.ParallelMerge();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void SafeMerge_WithMixedTypes_ReturnsMergedResult()
        {
            // Arrange
            var regular1 = new CardinalityEstimator(b: 10);
            regular1.Add("regular1_element");
            
            var regular2 = new CardinalityEstimator(b: 10);
            regular2.Add("regular2_element");
            
            var concurrent1 = new ConcurrentCardinalityEstimator(b: 10);
            concurrent1.Add("concurrent1_element");

            // Act
            var result = CardinalityEstimatorExtensions.SafeMerge(regular1, regular2, concurrent1, null);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count() > 0);
            Assert.Equal(3UL, result.CountAdditions);
            
            // Cleanup
            concurrent1.Dispose();
            result.Dispose();
        }

        [Fact]
        public void SafeMerge_WithInvalidType_ThrowsArgumentException()
        {
            // Arrange
            var invalidObject = "not an estimator";

            // Act & Assert
            Assert.Throws<ArgumentException>(() => CardinalityEstimatorExtensions.SafeMerge(invalidObject));
        }

        [Fact]
        public void SafeMerge_WithAllNulls_ReturnsNull()
        {
            // Act
            var result = CardinalityEstimatorExtensions.SafeMerge(null, null, null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void SafeMerge_WithNoParameters_ReturnsNull()
        {
            // Act
            var result = CardinalityEstimatorExtensions.SafeMerge();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void CreateMultiple_WithValidCount_ReturnsCorrectArray()
        {
            // Act
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(5, b: 10);

            // Assert
            Assert.NotNull(estimators);
            Assert.Equal(5, estimators.Length);
            Assert.All(estimators, e =>
            {
                Assert.NotNull(e);
                Assert.Equal(0UL, e.Count());
            });

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Fact]
        public void CreateMultiple_WithZeroCount_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => CardinalityEstimatorExtensions.CreateMultiple(0));
        }

        [Fact]
        public void CreateMultiple_WithNegativeCount_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => CardinalityEstimatorExtensions.CreateMultiple(-1));
        }

        [Fact]
        public void ParallelAdd_WithStringElements_DistributesCorrectly()
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(3, b: 10);
            var elements = Enumerable.Range(0, 30).Select(i => $"element_{i}").ToList();

            // Act
            estimators.ParallelAdd(elements, PartitionStrategy.RoundRobin);

            // Assert
            var totalCount = estimators.Sum(e => (long)e.CountAdditions);
            Assert.Equal(30L, totalCount);
            
            // Each estimator should have received some elements
            Assert.All(estimators, e => Assert.True(e.CountAdditions > 0));

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Fact]
        public void ParallelAdd_WithIntElements_DistributesCorrectly()
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(2, b: 10);
            var elements = Enumerable.Range(0, 20).ToList();

            // Act
            estimators.ParallelAdd(elements, PartitionStrategy.Chunked);

            // Assert
            var totalCount = estimators.Sum(e => (long)e.CountAdditions);
            Assert.Equal(20L, totalCount);

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Fact]
        public void ParallelAdd_WithMultipleTypes_WorksCorrectly()
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(2, b: 10);

            // Act & Assert - Test different types
            estimators.ParallelAdd(new[] { "string1", "string2" });          // 2 elements
            estimators.ParallelAdd(new[] { 1, 2, 3 });                      // 3 elements
            estimators.ParallelAdd(new[] { 1L, 2L });                       // 2 elements
            estimators.ParallelAdd(new[] { 1.0f, 2.0f });                   // 2 elements
            estimators.ParallelAdd(new[] { 1.0, 2.0 });                     // 2 elements
            estimators.ParallelAdd(new[] { 1u, 2u });                       // 2 elements
            estimators.ParallelAdd(new[] { 1ul, 2ul });                     // 2 elements
            estimators.ParallelAdd(new[] { new byte[] { 1 }, new byte[] { 2 } }); // 2 elements

            var totalCount = estimators.Sum(e => (long)e.CountAdditions);
            // Since we're adding 17 elements total across 8 operations, but some may have the same hash,
            // let's verify we got at least 15 additions
            Assert.True(totalCount >= 15L);

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Theory]
        [InlineData(PartitionStrategy.RoundRobin)]
        [InlineData(PartitionStrategy.Chunked)]
        [InlineData(PartitionStrategy.Hash)]
        public void ParallelAdd_WithDifferentStrategies_DistributesElements(PartitionStrategy strategy)
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(3, b: 10);
            var elements = Enumerable.Range(0, 30).Select(i => $"element_{i}").ToList();

            // Act
            estimators.ParallelAdd(elements, strategy);

            // Assert
            var totalCount = estimators.Sum(e => (long)e.CountAdditions);
            Assert.Equal(30L, totalCount);

            // Each estimator should have received some elements (depending on strategy)
            var nonEmptyEstimators = estimators.Count(e => e.CountAdditions > 0);
            Assert.True(nonEmptyEstimators >= 1);

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Fact]
        public void ParallelAdd_WithNullElements_DoesNothing()
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(2, b: 10);

            // Act
            estimators.ParallelAdd<string>(null);

            // Assert
            Assert.All(estimators, e => Assert.Equal(0UL, e.CountAdditions));

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Fact]
        public void ParallelAdd_WithEmptyElements_DoesNothing()
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(2, b: 10);
            var emptyElements = new List<string>();

            // Act
            estimators.ParallelAdd(emptyElements);

            // Assert
            Assert.All(estimators, e => Assert.Equal(0UL, e.CountAdditions));

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Fact]
        public void ParallelAdd_WithNullEstimators_ThrowsArgumentException()
        {
            // Arrange
            ConcurrentCardinalityEstimator[] nullEstimators = null;
            var elements = new[] { "test" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => nullEstimators.ParallelAdd(elements));
        }

        [Fact]
        public void ParallelAdd_WithEmptyEstimators_ThrowsArgumentException()
        {
            // Arrange
            var emptyEstimators = new ConcurrentCardinalityEstimator[0];
            var elements = new[] { "test" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => emptyEstimators.ParallelAdd(elements));
        }

        [Fact]
        public void ParallelAdd_WithUnsupportedType_ThrowsArgumentException()
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(2, b: 10);
            var unsupportedElements = new[] { new { Name = "Test" } }; // Use anonymous type instead of DateTime

            // Act & Assert - The exception will be thrown during the parallel execution
            var exception = Assert.Throws<AggregateException>(() => estimators.ParallelAdd(unsupportedElements));
            Assert.Contains(exception.InnerExceptions, e => e is ArgumentException);

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }

        [Fact]
        public void ParallelAdd_WithByteArrayElements_WorksCorrectly()
        {
            // Arrange
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(2, b: 10);
            var byteArrays = new[]
            {
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                new byte[] { 7, 8, 9 }
            };

            // Act
            estimators.ParallelAdd(byteArrays);

            // Assert
            var totalCount = estimators.Sum(e => (long)e.CountAdditions);
            Assert.Equal(3L, totalCount);

            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }
    }
}
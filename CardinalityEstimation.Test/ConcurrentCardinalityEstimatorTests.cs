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
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public class ConcurrentCardinalityEstimatorTests
    {
        [Fact]
        public void Constructor_WithValidParameters_CreatesEstimator()
        {
            // Act
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            
            // Assert
            Assert.Equal(0UL, estimator.Count());
            Assert.Equal(0UL, estimator.CountAdditions);
        }

        [Fact]
        public void Constructor_WithInvalidBits_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentCardinalityEstimator(b: 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentCardinalityEstimator(b: 17));
        }

        [Fact]
        public void Constructor_FromRegularEstimator_CopiesCorrectly()
        {
            // Arrange
            var regular = new CardinalityEstimator(b: 10);
            regular.Add("test1");
            regular.Add("test2");
            regular.Add("test3");

            // Act
            using var concurrent = new ConcurrentCardinalityEstimator(regular);

            // Assert
            Assert.Equal(regular.Count(), concurrent.Count());
            Assert.Equal(regular.CountAdditions, concurrent.CountAdditions);
        }

        [Fact]
        public void Add_String_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var elements = Enumerable.Range(0, 1000).Select(i => $"element_{i}").ToList();
            var bag = new ConcurrentBag<bool>();

            // Act
            Parallel.ForEach(elements, element =>
            {
                var result = estimator.Add(element);
                bag.Add(result);
            });

            // Assert
            Assert.Equal(1000UL, estimator.CountAdditions);
            Assert.True(estimator.Count() > 0);
            // At least some additions should return true (state changed)
            // We can't guarantee all will return true due to internal state transitions
            Assert.True(bag.Count(b => b) > 0);
        }

        [Fact]
        public void Add_MultipleTypes_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var tasks = new List<Task>();

            // Act - Add different types concurrently
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    estimator.Add($"string_{i}");
                }
            }));

            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    estimator.Add(i);
                }
            }));

            tasks.Add(Task.Run(() =>
            {
                for (long i = 0; i < 100; i++)
                {
                    estimator.Add(i * 1000L);
                }
            }));

            tasks.Add(Task.Run(() =>
            {
                for (double i = 0; i < 100; i++)
                {
                    estimator.Add(i * 0.5);
                }
            }));

            Task.WaitAll(tasks.ToArray());

            // Assert
            Assert.Equal(400UL, estimator.CountAdditions);
            Assert.True(estimator.Count() > 0);
        }

        [Fact]
        public void Count_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var elements = Enumerable.Range(0, 50).Select(i => $"element_{i}").ToList();
            var counts = new ConcurrentBag<ulong>();

            // Add elements first
            foreach (var element in elements)
            {
                estimator.Add(element);
            }

            // Act - Read count from multiple threads
            Parallel.For(0, 100, _ =>
            {
                var count = estimator.Count();
                counts.Add(count);
            });

            // Assert
            Assert.True(counts.All(c => c > 0));
            // All counts should be the same since we're not adding during reading
            Assert.True(counts.Distinct().Count() <= 2); // Allow for minor variations due to direct count vs estimation
        }

        [Fact]
        public void Merge_ConcurrentEstimators_ThreadSafe()
        {
            // Arrange
            using var estimator1 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator2 = new ConcurrentCardinalityEstimator(b: 10);
            
            // Add different elements to each estimator
            for (int i = 0; i < 50; i++)
            {
                estimator1.Add($"set1_element_{i}");
                estimator2.Add($"set2_element_{i}");
            }

            var count1 = estimator1.Count();
            var count2 = estimator2.Count();

            // Act
            estimator1.Merge(estimator2);

            // Assert
            var mergedCount = estimator1.Count();
            Assert.True(mergedCount >= Math.Max(count1, count2));
            Assert.Equal(100UL, estimator1.CountAdditions);
        }

        [Fact]
        public void Merge_RegularEstimator_ThreadSafe()
        {
            // Arrange
            using var concurrent = new ConcurrentCardinalityEstimator(b: 10);
            var regular = new CardinalityEstimator(b: 10);
            
            // Add elements
            for (int i = 0; i < 50; i++)
            {
                concurrent.Add($"concurrent_element_{i}");
                regular.Add($"regular_element_{i}");
            }

            var concurrentCount = concurrent.Count();
            var regularCount = regular.Count();

            // Act
            concurrent.Merge(regular);

            // Assert
            var mergedCount = concurrent.Count();
            Assert.True(mergedCount >= Math.Max(concurrentCount, regularCount));
            Assert.Equal(100UL, concurrent.CountAdditions);
        }

        [Fact]
        public void Merge_WithIncompatibleAccuracy_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            using var estimator1 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator2 = new ConcurrentCardinalityEstimator(b: 12);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => estimator1.Merge(estimator2));
        }

        [Fact]
        public void ParallelMerge_MultipleEstimators_ProducesCorrectResult()
        {
            // Arrange
            var estimators = new List<ConcurrentCardinalityEstimator>();
            for (int i = 0; i < 10; i++)
            {
                var estimator = new ConcurrentCardinalityEstimator(b: 10);
                for (int j = 0; j < 100; j++)
                {
                    estimator.Add($"estimator_{i}_element_{j}");
                }
                estimators.Add(estimator);
            }

            // Act
            var merged = ConcurrentCardinalityEstimator.ParallelMerge(estimators);

            // Assert
            Assert.NotNull(merged);
            Assert.True(merged.Count() > 0);
            Assert.Equal(1000UL, merged.CountAdditions);
            
            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
            merged.Dispose();
        }

        [Fact]
        public void StaticMerge_MultipleEstimators_ProducesCorrectResult()
        {
            // Arrange
            var estimators = new List<ConcurrentCardinalityEstimator>();
            for (int i = 0; i < 5; i++)
            {
                var estimator = new ConcurrentCardinalityEstimator(b: 10);
                for (int j = 0; j < 20; j++)
                {
                    estimator.Add($"estimator_{i}_element_{j}");
                }
                estimators.Add(estimator);
            }

            // Act
            var merged = ConcurrentCardinalityEstimator.Merge(estimators);

            // Assert
            Assert.NotNull(merged);
            Assert.True(merged.Count() > 0);
            Assert.Equal(100UL, merged.CountAdditions);
            
            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
            merged.Dispose();
        }

        [Fact]
        public void ToCardinalityEstimator_CreatesConsistentSnapshot()
        {
            // Arrange
            using var concurrent = new ConcurrentCardinalityEstimator(b: 10);
            var elements = Enumerable.Range(0, 50).Select(i => $"element_{i}").ToList();
            
            foreach (var element in elements)
            {
                concurrent.Add(element);
            }

            // Act
            var regular = concurrent.ToCardinalityEstimator();

            // Assert
            Assert.Equal(concurrent.Count(), regular.Count());
            Assert.Equal(concurrent.CountAdditions, regular.CountAdditions);
        }

        [Fact]
        public void ConcurrentAddAndCount_StressTest()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 12);
            var elementCount = 10000;
            var readerCount = 10;
            var writerCount = 10;
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var counts = new ConcurrentBag<ulong>();

            // Act - Concurrent reads and writes
            var tasks = new List<Task>();

            // Writer tasks
            for (int w = 0; w < writerCount; w++)
            {
                int writerId = w;
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < elementCount / writerCount; i++)
                    {
                        if (cancellation.Token.IsCancellationRequested) break;
                        estimator.Add($"writer_{writerId}_element_{i}");
                        Thread.Yield();
                    }
                }, cancellation.Token));
            }

            // Reader tasks
            for (int r = 0; r < readerCount; r++)
            {
                tasks.Add(Task.Run(() =>
                {
                    while (!cancellation.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var count = estimator.Count();
                            counts.Add(count);
                            Thread.Yield();
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, cancellation.Token));
            }

            try
            {
                Task.WaitAll(tasks.ToArray(), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }

            // Assert
            Assert.True(counts.Count > 0);
            Assert.True(estimator.Count() > 0);
            Assert.Equal((ulong)elementCount, estimator.CountAdditions);
        }

        [Fact]
        public void DirectCount_ThreadSafe()
        {
            // Arrange - Use small number to ensure direct counting
            using var estimator = new ConcurrentCardinalityEstimator(b: 10, useDirectCounting: true);
            var elements = Enumerable.Range(0, 50).Select(i => $"element_{i}").ToList();

            // Act - Add elements concurrently
            Parallel.ForEach(elements, element =>
            {
                estimator.Add(element);
            });

            // Assert
            Assert.Equal(50UL, estimator.Count());
            Assert.Equal(50UL, estimator.CountAdditions);
        }

        [Fact]
        public void SparseTodense_TransitionThreadSafe()
        {
            // Arrange - Use parameters that will trigger sparse to dense transition
            using var estimator = new ConcurrentCardinalityEstimator(b: 6); // Small b to trigger transition quickly
            var elementCount = 1000;

            // Act - Add many elements concurrently to trigger sparse->dense transition
            Parallel.For(0, elementCount, i =>
            {
                estimator.Add($"element_{i}");
            });

            // Assert
            Assert.True(estimator.Count() > 0);
            Assert.Equal((ulong)elementCount, estimator.CountAdditions);
        }

        [Fact]
        public void Equals_SameContent_ReturnsTrue()
        {
            // Arrange
            using var estimator1 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator2 = new ConcurrentCardinalityEstimator(b: 10);
            
            var elements = new[] { "element1", "element2", "element3" };
            
            foreach (var element in elements)
            {
                estimator1.Add(element);
                estimator2.Add(element);
            }

            // Act & Assert
            Assert.True(estimator1.Equals(estimator2));
        }

        [Fact]
        public void Equals_DifferentContent_ReturnsFalse()
        {
            // Arrange
            using var estimator1 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator2 = new ConcurrentCardinalityEstimator(b: 10);
            
            estimator1.Add("element1");
            estimator2.Add("element2");

            // Act & Assert
            Assert.False(estimator1.Equals(estimator2));
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var estimator = new ConcurrentCardinalityEstimator(b: 10);
            estimator.Add("test");

            // Act & Assert - Should not throw
            estimator.Dispose();
            estimator.Dispose();
            estimator.Dispose();
        }

        [Fact]
        public void AfterDispose_OperationsThrowObjectDisposedException()
        {
            // Arrange
            var estimator = new ConcurrentCardinalityEstimator(b: 10);
            estimator.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => estimator.Add("test"));
            Assert.Throws<ObjectDisposedException>(() => estimator.Count());
            Assert.Throws<ObjectDisposedException>(() => estimator.ToCardinalityEstimator());
        }

        [Fact]
        public void DeadlockPrevention_MultipleMerges()
        {
            // Arrange
            var estimators = new ConcurrentCardinalityEstimator[4];
            for (int i = 0; i < 4; i++)
            {
                estimators[i] = new ConcurrentCardinalityEstimator(b: 10);
                estimators[i].Add($"base_element_{i}");
            }

            // Act - Perform merges that could deadlock if not handled properly
            var tasks = new[]
            {
                Task.Run(() => estimators[0].Merge(estimators[1])),
                Task.Run(() => estimators[1].Merge(estimators[0])),
                Task.Run(() => estimators[2].Merge(estimators[3])),
                Task.Run(() => estimators[3].Merge(estimators[2]))
            };

            var completed = Task.WaitAll(tasks, TimeSpan.FromSeconds(5));

            // Assert
            Assert.True(completed, "Merges should complete without deadlock");
            
            // Cleanup
            foreach (var estimator in estimators)
            {
                estimator.Dispose();
            }
        }
    }
}
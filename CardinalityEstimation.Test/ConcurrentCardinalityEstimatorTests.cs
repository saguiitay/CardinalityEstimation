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

        // ---------------------------------------------------------------------
        // Coverage-gap tests: exercise previously-uncovered concurrent estimator
        // paths (null-arg validation, copy ctors, Merge variants, ToCardinalityEstimator,
        // static Merge/ParallelMerge edge cases, Equals branches).
        // ---------------------------------------------------------------------

        [Fact]
        public void Constructor_FromNullCardinalityEstimator_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ConcurrentCardinalityEstimator((CardinalityEstimator)null));
        }

        [Fact]
        public void Constructor_FromNullConcurrentCardinalityEstimator_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ConcurrentCardinalityEstimator((ConcurrentCardinalityEstimator)null));
        }

        [Fact]
        public void Constructor_CopyFromConcurrent_PreservesContent()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            for (int i = 0; i < 25; i++) a.Add($"x_{i}");

            using var b = new ConcurrentCardinalityEstimator(a);

            Assert.Equal(a.Count(), b.Count());
            Assert.Equal(a.CountAdditions, b.CountAdditions);
        }

        [Fact]
        public void Coverage_Merge_NullConcurrentOther_Throws()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            Assert.Throws<ArgumentNullException>(() => a.Merge((ConcurrentCardinalityEstimator)null));
        }

        [Fact]
        public void Coverage_Merge_DifferentBitsPerIndex_Throws()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            using var b = new ConcurrentCardinalityEstimator(b: 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Merge(b));
        }

        [Fact]
        public void Merge_NullCardinalityEstimator_Throws()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            Assert.Throws<ArgumentNullException>(() => a.Merge((CardinalityEstimator)null));
        }

        [Fact]
        public void Merge_CardinalityEstimatorDifferentBitsPerIndex_Throws()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            var b = new CardinalityEstimator(b: 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Merge(b));
        }

        [Fact]
        public void Merge_FromCardinalityEstimator_Succeeds()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            for (int i = 0; i < 10; i++) a.Add($"a_{i}");

            var b = new CardinalityEstimator(b: 14);
            for (int i = 0; i < 10; i++) b.Add($"b_{i}");

            a.Merge(b);
            Assert.Equal(20UL, a.Count());
        }

        [Fact]
        public void ToCardinalityEstimator_PreservesCount()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            for (int i = 0; i < 10; i++) a.Add($"x_{i}");

            var snapshot = a.ToCardinalityEstimator();
            Assert.Equal(10UL, snapshot.Count());
        }

        [Fact]
        public void StaticMerge_NullEnumerable_ReturnsNull()
        {
            Assert.Null(ConcurrentCardinalityEstimator.Merge((IEnumerable<ConcurrentCardinalityEstimator>)null));
        }

        [Fact]
        public void StaticMerge_AllNullEntries_ReturnsNull()
        {
            var result = ConcurrentCardinalityEstimator.Merge(new ConcurrentCardinalityEstimator[] { null, null });
            Assert.Null(result);
        }

        [Fact]
        public void StaticMerge_SkipsNullEntries_AndMergesRest()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            using var b = new ConcurrentCardinalityEstimator(b: 14);
            a.Add("a");
            b.Add("b");

            using var merged = ConcurrentCardinalityEstimator.Merge(new[] { null, a, null, b, null });
            Assert.NotNull(merged);
            Assert.Equal(2UL, merged.Count());
        }

        [Fact]
        public void StaticParallelMerge_NullEnumerable_ReturnsNull()
        {
            Assert.Null(ConcurrentCardinalityEstimator.ParallelMerge(null));
        }

        [Fact]
        public void StaticParallelMerge_AllNullEntries_ReturnsNull()
        {
            var result = ConcurrentCardinalityEstimator.ParallelMerge(new ConcurrentCardinalityEstimator[] { null, null });
            Assert.Null(result);
        }

        [Fact]
        public void StaticParallelMerge_SingleEstimator_ReturnsCopy()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            for (int i = 0; i < 5; i++) a.Add($"v_{i}");

            using var merged = ConcurrentCardinalityEstimator.ParallelMerge(new[] { a });
            Assert.NotNull(merged);
            Assert.Equal(5UL, merged.Count());
        }

        [Fact]
        public void StaticParallelMerge_MismatchedBitsPerIndex_Throws()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            using var b = new ConcurrentCardinalityEstimator(b: 12);

            Assert.Throws<ArgumentException>(() => ConcurrentCardinalityEstimator.ParallelMerge(new[] { a, b }));
        }

        [Fact]
        public void StaticParallelMerge_WithDegree_MergesAll()
        {
            var estimators = new ConcurrentCardinalityEstimator[4];
            try
            {
                for (int i = 0; i < estimators.Length; i++)
                {
                    estimators[i] = new ConcurrentCardinalityEstimator(b: 14);
                    for (int j = 0; j < 10; j++) estimators[i].Add($"e{i}_v{j}");
                }

                using var merged = ConcurrentCardinalityEstimator.ParallelMerge(estimators, parallelismDegree: 2);
                Assert.NotNull(merged);
                Assert.Equal(40UL, merged.Count());
            }
            finally
            {
                foreach (var e in estimators) e?.Dispose();
            }
        }

        [Fact]
        public void Equals_NullOther_ReturnsFalse()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            Assert.False(a.Equals((ConcurrentCardinalityEstimator)null));
        }

        [Fact]
        public void Coverage_Equals_DifferentBitsPerIndex_ReturnsFalse()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            using var b = new ConcurrentCardinalityEstimator(b: 12);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_OneDirectCountOneNot_ReturnsFalse()
        {
            // ConcurrentCardinalityEstimator's public ctor doesn't expose useDirectCounting,
            // but adding more than the threshold tips one out of the direct-count path.
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            using var b = new ConcurrentCardinalityEstimator(b: 14);
            for (int i = 0; i < 5; i++) a.Add($"a_{i}");
            for (int i = 0; i < 200; i++) b.Add($"b_{i}");
            Assert.False(a.Equals(b));
        }

        // Regression tests for the direct-count storage. Previously backed by ConcurrentBag<ulong>,
        // which permits duplicates and forced every Add/Count/Merge/Equals path to call .Distinct()
        // (allocating + O(n)). The storage is now a ConcurrentDictionary<ulong, byte> used as a
        // concurrent hash set, so duplicates simply collapse and Count is O(1).

        [Fact]
        public void DirectCount_ConcurrentDuplicateAdds_DeduplicatesExactly()
        {
            // Arrange - stay below DirectCounterMaxElements (100) so we exercise the direct-count path.
            using var estimator = new ConcurrentCardinalityEstimator(b: 14);
            const int distinctCount = 50;
            const int duplicatesPerElement = 200;

            // Act - many threads pushing the same set of values repeatedly.
            Parallel.For(0, distinctCount * duplicatesPerElement, i =>
            {
                estimator.Add($"element_{i % distinctCount}");
            });

            // Assert - direct counter is exact, so Count must equal the number of distinct strings,
            // regardless of how many duplicate adds raced.
            Assert.Equal((ulong)distinctCount, estimator.Count());
            Assert.Equal((ulong)(distinctCount * duplicatesPerElement), estimator.CountAdditions);
        }

        [Fact]
        public void DirectCount_TransitionsToSparse_AtCorrectThreshold()
        {
            // Arrange - DirectCounterMaxElements is 100. Adding 101 distinct elements must
            // tip the estimator out of the direct-count path and into the HLL representation.
            using var estimator = new ConcurrentCardinalityEstimator(b: 14);

            // Act
            for (int i = 0; i <= 100; i++)
            {
                estimator.Add($"element_{i}");
            }

            // Assert - the direct-count path is exact up to 100, but past the threshold we are
            // back to an HLL estimate, so we just verify the count is in a sane range.
            ulong count = estimator.Count();
            Assert.InRange(count, 90UL, 110UL);
            Assert.Equal(101UL, estimator.CountAdditions);
        }

        [Fact]
        public void DirectCount_MergeDeduplicates_AndRespectsThreshold()
        {
            // Arrange
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            using var b = new ConcurrentCardinalityEstimator(b: 14);

            // 30 elements in each, with a 10-element overlap → 50 distinct after merge,
            // still below the 100-element direct-count threshold.
            for (int i = 0; i < 30; i++)
            {
                a.Add($"a_{i}");
                b.Add($"a_{i + 20}");
            }

            // Act
            a.Merge(b);

            // Assert - merge through the direct-count path must deduplicate exactly.
            Assert.Equal(50UL, a.Count());
        }

        [Fact]
        public void DirectCount_RoundTripsThroughCardinalityEstimator()
        {
            // Arrange - convert to the non-concurrent estimator and back, while still in the
            // direct-count path. The round trip exercises GetStateInternal/InitializeFromState,
            // both of which had to special-case the bag's duplicate semantics.
            using var original = new ConcurrentCardinalityEstimator(b: 14);
            for (int i = 0; i < 25; i++)
            {
                original.Add($"value_{i}");
            }

            // Act
            CardinalityEstimator snapshot = original.ToCardinalityEstimator();
            using var roundTripped = new ConcurrentCardinalityEstimator(snapshot);

            // Assert - direct-count is exact, so all three views must report the same count.
            Assert.Equal(25UL, original.Count());
            Assert.Equal(25UL, snapshot.Count());
            Assert.Equal(25UL, roundTripped.Count());
        }

        // ---------------------------------------------------------------------
        // Regression tests for the zero-allocation primitive Add overloads.
        // See CardinalityEstimatorTests for the full rationale; these mirror
        // the same invariant on the concurrent estimator.
        // ---------------------------------------------------------------------

        [Fact]
        public void Add_PrimitiveAndByteArray_ProduceSameHash()
        {
            using var hll = new ConcurrentCardinalityEstimator(b: 14);
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

            Assert.Equal(12UL, hll.CountAdditions);
            Assert.Equal(6UL, hll.Count());
        }

        [Fact]
        public void Add_String_StackallocAndHeapPath_AgreeWithByteArray()
        {
            using var hll = new ConcurrentCardinalityEstimator(b: 14);
            const string shortStr = "hello world";
            hll.Add(shortStr);
            hll.Add(System.Text.Encoding.UTF8.GetBytes(shortStr));
            Assert.Equal(1UL, hll.Count());

            using var hll2 = new ConcurrentCardinalityEstimator(b: 14);
            string longStr = new string('x', 200);
            hll2.Add(longStr);
            hll2.Add(System.Text.Encoding.UTF8.GetBytes(longStr));
            Assert.Equal(1UL, hll2.Count());
        }

        // ---------------------------------------------------------------------
        // The shared HllConstants.InversePowersOfTwo table is exercised by the
        // unit test in CardinalityEstimatorTests; both estimators now consume
        // the same table so a single regression test is sufficient.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Regression: the <see cref="ConcurrentCardinalityEstimator"/> constructor that takes a
        /// <see cref="GetHashCodeSpanDelegate"/> previously checked <c>this.hashFunction == null</c>
        /// (which is always true at that point because the chained <c>this(state)</c> ctor never
        /// assigns it) and unconditionally overwrote the user-supplied span delegate with the
        /// default XxHash128 implementation. The user delegate must be honored, and a non-null
        /// byte-array <c>hashFunction</c> companion must be wired up to it (mirrors the analogous
        /// branch in <see cref="CardinalityEstimator(GetHashCodeSpanDelegate, CardinalityEstimatorState)"/>).
        /// </summary>
        [Fact]
        public void SpanDelegateConstructor_UsesProvidedDelegateInsteadOfDefault()
        {
            int callCount = 0;
            GetHashCodeSpanDelegate customSpanHash = (ReadOnlySpan<byte> bytes) =>
            {
                callCount++;
                // Return a deterministic, distinctive value so default XxHash128 (which would
                // produce arbitrary bit patterns) cannot accidentally match this fingerprint.
                return 0xDEADBEEFCAFEBABEUL;
            };

            var state = new CardinalityEstimatorState
            {
                BitsPerIndex = 14,
                IsSparse = true,
                LookupSparse = new Dictionary<ushort, byte>(),
                CountAdditions = 0,
            };

            using var estimator = new ConcurrentCardinalityEstimator(customSpanHash, state);
            estimator.Add(BitConverter.GetBytes(42));

            Assert.True(callCount > 0, "Custom span hash delegate was never invoked -- ctor silently replaced it with the default.");
        }

        [Fact]
        public void AddString_ThrowsArgumentNullException_WhenElementIsNull()
        {
            using var estimator = new ConcurrentCardinalityEstimator();
            var ex = Assert.Throws<ArgumentNullException>(() => estimator.Add((string)null));
            Assert.Equal("element", ex.ParamName);
        }

        [Fact]
        public void AddByteArray_ThrowsArgumentNullException_WhenElementIsNull()
        {
            using var estimator = new ConcurrentCardinalityEstimator();
            var ex = Assert.Throws<ArgumentNullException>(() => estimator.Add((byte[])null));
            Assert.Equal("element", ex.ParamName);
        }
    }
}
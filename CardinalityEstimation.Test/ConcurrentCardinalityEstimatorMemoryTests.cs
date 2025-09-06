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
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using CardinalityEstimation.Hash;
    using Xunit;
    using Xunit.Abstractions;

    public class ConcurrentCardinalityEstimatorMemoryTests : IDisposable
    {
        public static readonly Random Rand = new Random();

        private readonly ITestOutputHelper output;
        private readonly Stopwatch stopwatch;

        public ConcurrentCardinalityEstimatorMemoryTests(ITestOutputHelper outputHelper)
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
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
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
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
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
        public void Add_Span_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var testData = new byte[100][];
            for (int i = 0; i < 100; i++)
            {
                testData[i] = BitConverter.GetBytes(i);
            }
            var results = new ConcurrentBag<bool>();

            // Act
            Parallel.ForEach(testData, data =>
            {
                var result = estimator.Add(data.AsSpan());
                results.Add(result);
            });

            // Assert
            Assert.Equal(100UL, estimator.CountAdditions);
            Assert.True(results.Count(r => r) > 0); // At least some should return true
            Assert.True(estimator.Count() > 0);
        }

        #endregion

        #region ReadOnlySpan<byte> Tests

        [Fact]
        public void Add_ReadOnlySpan_WithUniqueData_ReturnsTrue()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
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
        public void Add_ReadOnlySpan_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var testData = new byte[100][];
            for (int i = 0; i < 100; i++)
            {
                testData[i] = BitConverter.GetBytes(i);
            }
            var results = new ConcurrentBag<bool>();

            // Act
            Parallel.ForEach(testData, data =>
            {
                var result = estimator.Add((ReadOnlySpan<byte>)data.AsSpan());
                results.Add(result);
            });

            // Assert
            Assert.Equal(100UL, estimator.CountAdditions);
            Assert.True(results.Count(r => r) > 0);
            Assert.True(estimator.Count() > 0);
        }

        #endregion

        #region Memory<byte> Tests

        [Fact]
        public void Add_Memory_WithUniqueData_ReturnsTrue()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
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
        public void Add_Memory_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var testData = new byte[100][];
            for (int i = 0; i < 100; i++)
            {
                testData[i] = BitConverter.GetBytes(i);
            }
            var results = new ConcurrentBag<bool>();

            // Act
            Parallel.ForEach(testData, data =>
            {
                var result = estimator.Add(data.AsMemory());
                results.Add(result);
            });

            // Assert
            Assert.Equal(100UL, estimator.CountAdditions);
            Assert.True(results.Count(r => r) > 0);
            Assert.True(estimator.Count() > 0);
        }

        #endregion

        #region ReadOnlyMemory<byte> Tests

        [Fact]
        public void Add_ReadOnlyMemory_WithUniqueData_ReturnsTrue()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
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
        public void Add_ReadOnlyMemory_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var testData = new byte[100][];
            for (int i = 0; i < 100; i++)
            {
                testData[i] = BitConverter.GetBytes(i);
            }
            var results = new ConcurrentBag<bool>();

            // Act
            Parallel.ForEach(testData, data =>
            {
                var result = estimator.Add((ReadOnlyMemory<byte>)data.AsMemory());
                results.Add(result);
            });

            // Assert
            Assert.Equal(100UL, estimator.CountAdditions);
            Assert.True(results.Count(r => r) > 0);
            Assert.True(estimator.Count() > 0);
        }

        #endregion

        #region Mixed Thread Safety Tests

        [Fact]
        public void Add_MemoryTypes_ConcurrentMixedTypes_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 12);
            const int elementsPerType = 250;
            const int totalElements = elementsPerType * 4;
            var results = new ConcurrentBag<bool>();

            // Act - Add different memory types concurrently
            var tasks = new[]
            {
                Task.Run(() =>
                {
                    for (int i = 0; i < elementsPerType; i++)
                    {
                        var data = BitConverter.GetBytes(i * 4);
                        var result = estimator.Add(data.AsSpan());
                        results.Add(result);
                    }
                }),
                Task.Run(() =>
                {
                    for (int i = 0; i < elementsPerType; i++)
                    {
                        var data = BitConverter.GetBytes(i * 4 + 1);
                        var result = estimator.Add((ReadOnlySpan<byte>)data.AsSpan());
                        results.Add(result);
                    }
                }),
                Task.Run(() =>
                {
                    for (int i = 0; i < elementsPerType; i++)
                    {
                        var data = BitConverter.GetBytes(i * 4 + 2);
                        var result = estimator.Add(data.AsMemory());
                        results.Add(result);
                    }
                }),
                Task.Run(() =>
                {
                    for (int i = 0; i < elementsPerType; i++)
                    {
                        var data = BitConverter.GetBytes(i * 4 + 3);
                        var result = estimator.Add((ReadOnlyMemory<byte>)data.AsMemory());
                        results.Add(result);
                    }
                })
            };

            Task.WaitAll(tasks);

            // Assert
            Assert.Equal((ulong)totalElements, estimator.CountAdditions);
            Assert.True(estimator.Count() > 0);
            Assert.True(results.Count(r => r) > 0);
        }

        [Fact]
        public void Add_MemoryTypes_ConcurrentReadsAndWrites_ThreadSafe()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            const int writerCount = 4;
            const int readerCount = 4;
            const int elementsPerWriter = 100;
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var counts = new ConcurrentBag<ulong>();

            // Act - Concurrent reads and writes with memory types
            var tasks = new List<Task>();

            // Writer tasks using different memory types
            for (int w = 0; w < writerCount; w++)
            {
                int writerId = w;
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < elementsPerWriter; i++)
                    {
                        if (cancellation.Token.IsCancellationRequested) break;
                        
                        var data = BitConverter.GetBytes(writerId * elementsPerWriter + i);
                        
                        switch (writerId % 4)
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
            Assert.Equal((ulong)(writerCount * elementsPerWriter), estimator.CountAdditions);
            Assert.True(estimator.Count() > 0);
        }

        #endregion

        #region Consistency Tests

        [Fact]
        public void Add_MemoryTypes_ConsistencyWithRegularEstimator()
        {
            // Arrange
            var regularEstimator = new CardinalityEstimator(b: 10);
            using var concurrentEstimator = new ConcurrentCardinalityEstimator(b: 10);
            
            var testData = new[]
            {
                new byte[] { 1, 2, 3, 4 },
                new byte[] { 5, 6, 7, 8 },
                new byte[] { 9, 10, 11, 12 }
            };

            // Act - Add same data to both estimators using memory types
            regularEstimator.Add(testData[0].AsSpan());
            regularEstimator.Add((ReadOnlySpan<byte>)testData[1].AsSpan());
            regularEstimator.Add(testData[2].AsMemory());

            concurrentEstimator.Add(testData[0].AsSpan());
            concurrentEstimator.Add((ReadOnlySpan<byte>)testData[1].AsSpan());
            concurrentEstimator.Add(testData[2].AsMemory());

            // Assert
            Assert.Equal(regularEstimator.Count(), concurrentEstimator.Count());
            Assert.Equal(regularEstimator.CountAdditions, concurrentEstimator.CountAdditions);
        }

        [Fact]
        public void Add_MemoryTypes_AllTypesProduceSameResult()
        {
            // Arrange
            var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            
            using var estimator1 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator2 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator3 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator4 = new ConcurrentCardinalityEstimator(b: 10);

            // Act
            estimator1.Add(data.AsSpan()); // Span<byte>
            estimator2.Add((ReadOnlySpan<byte>)data.AsSpan()); // ReadOnlySpan<byte>
            estimator3.Add(data.AsMemory()); // Memory<byte>
            estimator4.Add((ReadOnlyMemory<byte>)data.AsMemory()); // ReadOnlyMemory<byte>

            // Assert
            var count1 = estimator1.Count();
            var count2 = estimator2.Count();
            var count3 = estimator3.Count();
            var count4 = estimator4.Count();

            Assert.Equal(count1, count2);
            Assert.Equal(count1, count3);
            Assert.Equal(count1, count4);

            Assert.True(estimator1.Equals(estimator2));
            Assert.True(estimator1.Equals(estimator3));
            Assert.True(estimator1.Equals(estimator4));
        }

        #endregion

        #region Merge Tests

        [Fact]
        public void Merge_WithMemoryTypes_WorksCorrectly()
        {
            // Arrange
            using var estimator1 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator2 = new ConcurrentCardinalityEstimator(b: 10);
            
            var data1 = new byte[] { 1, 2, 3 };
            var data2 = new byte[] { 4, 5, 6 };
            var data3 = new byte[] { 7, 8, 9 };

            // Act
            estimator1.Add(data1.AsSpan());
            estimator1.Add(data2.AsMemory());
            
            estimator2.Add((ReadOnlySpan<byte>)data2.AsSpan()); // Overlap
            estimator2.Add((ReadOnlyMemory<byte>)data3.AsMemory());
            
            var countBefore1 = estimator1.Count();
            var countBefore2 = estimator2.Count();
            
            estimator1.Merge(estimator2);

            // Assert
            var countAfter = estimator1.Count();
            Assert.True(countAfter >= Math.Max(countBefore1, countBefore2));
        }

        [Fact]
        public void Merge_ConcurrentWithMemoryTypes_ThreadSafe()
        {
            // Arrange
            using var estimator1 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator2 = new ConcurrentCardinalityEstimator(b: 10);
            using var estimator3 = new ConcurrentCardinalityEstimator(b: 10);

            var testData = new byte[30][];
            for (int i = 0; i < 30; i++)
            {
                testData[i] = BitConverter.GetBytes(i);
            }

            // Act - Add data concurrently to different estimators using memory types
            var addTasks = new[]
            {
                Task.Run(() =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        estimator1.Add(testData[i].AsSpan());
                    }
                }),
                Task.Run(() =>
                {
                    for (int i = 5; i < 15; i++) // Some overlap
                    {
                        estimator2.Add((ReadOnlySpan<byte>)testData[i].AsSpan());
                    }
                }),
                Task.Run(() =>
                {
                    for (int i = 10; i < 20; i++) // Some overlap
                    {
                        estimator3.Add(testData[i].AsMemory());
                    }
                })
            };

            Task.WaitAll(addTasks);

            // Now merge concurrently
            var mergeTasks = new[]
            {
                Task.Run(() => estimator1.Merge(estimator2)),
                Task.Run(() => estimator1.Merge(estimator3))
            };

            // Assert - Should complete without deadlock
            var completed = Task.WaitAll(mergeTasks, TimeSpan.FromSeconds(5));
            Assert.True(completed, "Merges should complete without deadlock");
            
            Assert.True(estimator1.Count() > 0);
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void Add_MemoryTypes_Performance_LargeDataset()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 14);
            const int dataSize = 1000;
            var testData = new byte[dataSize][];
            
            for (int i = 0; i < dataSize; i++)
            {
                testData[i] = BitConverter.GetBytes(i).Concat(BitConverter.GetBytes(i * 2)).ToArray();
            }

            var stopwatch = Stopwatch.StartNew();

            // Act - Add data using different memory types in parallel
            Parallel.For(0, dataSize, i =>
            {
                switch (i % 4)
                {
                    case 0:
                        estimator.Add(testData[i].AsSpan());
                        break;
                    case 1:
                        estimator.Add((ReadOnlySpan<byte>)testData[i].AsSpan());
                        break;
                    case 2:
                        estimator.Add(testData[i].AsMemory());
                        break;
                    case 3:
                        estimator.Add((ReadOnlyMemory<byte>)testData[i].AsMemory());
                        break;
                }
            });

            stopwatch.Stop();

            // Assert
            Assert.Equal((ulong)dataSize, estimator.CountAdditions);
            Assert.True(estimator.Count() > 0);
            
            output.WriteLine($"Added {dataSize} elements in {stopwatch.ElapsedMilliseconds}ms");
            output.WriteLine($"Estimated count: {estimator.Count()}");
            
            // Performance should be reasonable (adjust threshold as needed)
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Performance should be reasonable");
        }

        #endregion

        #region Disposed State Tests

        [Fact]
        public void Add_MemoryTypes_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var data = new byte[] { 1, 2, 3, 4 };
            estimator.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => estimator.Add(data.AsSpan()));
            Assert.Throws<ObjectDisposedException>(() => estimator.Add((ReadOnlySpan<byte>)data.AsSpan()));
            Assert.Throws<ObjectDisposedException>(() => estimator.Add(data.AsMemory()));
            Assert.Throws<ObjectDisposedException>(() => estimator.Add((ReadOnlyMemory<byte>)data.AsMemory()));
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Add_MemoryTypes_EmptyData_Works()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);

            // Act & Assert
            Assert.True(estimator.Add(Span<byte>.Empty));
            Assert.False(estimator.Add(ReadOnlySpan<byte>.Empty)); // Duplicate
            Assert.False(estimator.Add(Memory<byte>.Empty)); // Duplicate
            Assert.False(estimator.Add(ReadOnlyMemory<byte>.Empty)); // Duplicate

            Assert.Equal(1UL, estimator.Count());
            Assert.Equal(4UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_MemoryTypes_VeryLargeData_Works()
        {
            // Arrange
            using var estimator = new ConcurrentCardinalityEstimator(b: 10);
            var largeData = new byte[100000];
            Rand.NextBytes(largeData);

            // Act
            var result1 = estimator.Add(largeData.AsSpan());
            var result2 = estimator.Add(largeData.AsMemory()); // Same data, should be duplicate

            // Assert
            Assert.True(result1);
            Assert.False(result2);
            Assert.Equal(2UL, estimator.CountAdditions);
            Assert.Equal(1UL, estimator.Count());
        }

        #endregion
    }
}
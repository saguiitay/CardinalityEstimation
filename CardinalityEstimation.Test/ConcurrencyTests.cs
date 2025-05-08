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

    public class ConcurrencyTests
    {
        private const int DefaultPrecision = 14;

        [Fact]
        public async Task ParallelAdds_ProducesConsistentResults()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);
            var concurrentSet = new ConcurrentDictionary<int, byte>();
            const int itemCount = 10000;
            const int threadCount = 8;

            // Add same items in parallel
            await Task.WhenAll(Enumerable.Range(0, threadCount).Select(async _ =>
            {
                for (int i = 0; i < itemCount; i++)
                {
                    estimator.Add(i);
                    concurrentSet.TryAdd(i, 0);
                }
            }));

            // Verify count is close to actual unique items
            double actualCount = concurrentSet.Count;
            double estimatedCount = estimator.Count();
            double error = Math.Abs(estimatedCount - actualCount) / actualCount;

            Assert.True(error < 0.1, $"Estimated count {estimatedCount} differs from actual count {actualCount} by more than 10%");
        }

        [Fact]
        public async Task ParallelMerge_ProducesConsistentResults()
        {
            const int estimatorCount = 4;
            const int itemsPerEstimator = 1000;
            
            var mainEstimator = new CardinalityEstimator(b: DefaultPrecision);
            var estimators = new CardinalityEstimator[estimatorCount];
            var allItems = new ConcurrentDictionary<int, byte>();

            // Initialize estimators with different items
            for (int i = 0; i < estimatorCount; i++)
            {
                estimators[i] = new CardinalityEstimator(b: DefaultPrecision);
                int offset = i * itemsPerEstimator;

                for (int j = 0; j < itemsPerEstimator; j++)
                {
                    int value = offset + j;
                    estimators[i].Add(value);
                    allItems.TryAdd(value, 0);
                }
            }

            // Create temporary estimators for parallel merging
            var tempEstimators = new List<CardinalityEstimator>();
            for (int i = 0; i < estimatorCount / 2; i++)
            {
                var temp = new CardinalityEstimator(b: DefaultPrecision);
                tempEstimators.Add(temp);
            }

            // Merge pairs of estimators in parallel
            await Task.WhenAll(Enumerable.Range(0, estimatorCount / 2).Select(async i =>
            {
                var temp = tempEstimators[i];
                temp.Merge(estimators[i * 2]);
                temp.Merge(estimators[i * 2 + 1]);
            }));

            // Final merge of temporary estimators sequentially
            foreach (var temp in tempEstimators)
            {
                mainEstimator.Merge(temp);
            }

            // Verify merged count is close to actual unique items
            double actualCount = allItems.Count;
            double estimatedCount = mainEstimator.Count();
            double error = Math.Abs(estimatedCount - actualCount) / actualCount;

            Assert.True(error < 0.1, $"Merged count {estimatedCount} differs from actual count {actualCount} by more than 10%");
        }

        [Fact]
        public void ParallelAddAndRead_HandlesRaceConditions()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);
            var cts = new CancellationTokenSource();
            var exceptions = new ConcurrentBag<Exception>();

            // Start multiple readers
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var count = estimator.Count();
                        Assert.True(count >= 0);
                        Thread.Sleep(1); // Small delay to increase race condition chances
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToList();

            // Start multiple writers
            var writers = Enumerable.Range(0, 4).Select(threadId => Task.Run(() =>
            {
                try
                {
                    var random = new Random(threadId);
                    for (int i = 0; i < 1000 && !cts.Token.IsCancellationRequested; i++)
                    {
                        estimator.Add(random.Next());
                        Thread.Sleep(1); // Small delay to increase race condition chances
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToList();

            // Let it run for a short time
            Thread.Sleep(1000);
            cts.Cancel();

            // Wait for all tasks and check for exceptions
            Task.WhenAll(readers.Concat(writers)).Wait();
            Assert.Empty(exceptions);
        }

        [Fact]
        public async Task ConcurrentCopyConstruction_ProducesValidEstimators()
        {
            var sourceEstimator = new CardinalityEstimator(b: DefaultPrecision);
            const int itemCount = 1000;

            // Add items to source estimator
            for (int i = 0; i < itemCount; i++)
            {
                sourceEstimator.Add(i);
            }

            // Create copies concurrently
            const int copyCount = 10;
            var copies = await Task.WhenAll(Enumerable.Range(0, copyCount)
                .Select(_ => Task.Run(() => new CardinalityEstimator(sourceEstimator))));

            // Verify all copies have same count
            var sourceCount = sourceEstimator.Count();
            foreach (var copy in copies)
            {
                Assert.Equal(sourceCount, copy.Count());
            }

            // Add more items to copies concurrently
            await Task.WhenAll(copies.Select(async copy =>
            {
                for (int i = itemCount; i < itemCount * 2; i++)
                {
                    copy.Add(i);
                }
            }));

            // Verify copies can be modified independently
            Assert.Equal(sourceCount, sourceEstimator.Count());
            foreach (var copy in copies)
            {
                Assert.True(copy.Count() > sourceCount);
            }
        }
    }
}

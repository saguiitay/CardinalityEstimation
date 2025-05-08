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
    using System.Linq;
    using CardinalityEstimation.Hash;
    using Xunit;

    public class PerformanceTests
    {
        [Theory]
        [InlineData(4)]   // Minimum precision
        [InlineData(8)]   // Low precision
        [InlineData(12)]  // Medium precision
        [InlineData(16)]  // High precision
        public void ErrorBounds_MeetExpectations(int precision)
        {
            var estimator = new CardinalityEstimator(b: precision);
            var actualCounts = new[] { 1000, 10000, 100000, 1000000 }
                .Where(n => n <= Math.Pow(2, precision + 2)) // Skip counts too large for precision
                .ToList();

            foreach (int count in actualCounts)
            {
                // Add unique items
                for (int i = 0; i < count; i++)
                {
                    estimator.Add(i);
                }

                double estimatedCount = estimator.Count();
                double error = Math.Abs(estimatedCount - count) / count;

                // Expected error is approximately 1.04/sqrt(2^precision)
                double expectedError = 1.04 / Math.Sqrt(Math.Pow(2, precision));
                
                Assert.True(error <= expectedError * 2, // Allow twice the theoretical error to account for randomness
                    $"Error {error:P2} for count {count:N0} exceeds twice the expected error {expectedError:P2} at precision {precision}");
            }
        }

        [Fact]
        public void MemoryUsage_ScalesWithPrecision()
        {
            const int itemCount = 100000;
            var memoryUsage = new Dictionary<int, long>();

            // Measure memory usage for different precisions
            for (int precision = 4; precision <= 16; precision++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                long initialMemory = GC.GetTotalMemory(true);

                var estimator = new CardinalityEstimator(b: precision);
                for (int i = 0; i < itemCount; i++)
                {
                    estimator.Add(i);
                }

                GC.Collect(2, GCCollectionMode.Forced, true);
                long finalMemory = GC.GetTotalMemory(true);
                memoryUsage[precision] = finalMemory - initialMemory;
            }

            // Verify memory usage roughly doubles with each 1-bit increase in precision
            for (int precision = 5; precision <= 16; precision++)
            {
                double ratio = (double)memoryUsage[precision] / memoryUsage[precision - 1];
                Assert.True(ratio <= 2.5, // Allow some overhead
                    $"Memory usage ratio {ratio:F2} between precision {precision} and {precision-1} exceeds 2.5");
            }
        }

        [Theory]
        [InlineData(1000)]
        [InlineData(10000)]
        [InlineData(100000)]
        public void AddOperation_HasConstantTimeComplexity(int itemCount)
        {
            var estimator = new CardinalityEstimator(b: 14); // Use default precision
            var timings = new List<double>();
            const int samplesPerBatch = 1000;
            const int warmupBatches = 3;
            var sw = new Stopwatch();

            // Warmup
            for (int i = 0; i < samplesPerBatch * warmupBatches; i++)
            {
                estimator.Add(i);
            }

            // Measure add operations in batches
            for (int batch = warmupBatches; batch * samplesPerBatch < itemCount; batch++)
            {
                sw.Restart();
                for (int i = 0; i < samplesPerBatch; i++)
                {
                    estimator.Add(batch * samplesPerBatch + i);
                }
                sw.Stop();
                timings.Add(sw.Elapsed.TotalMilliseconds / samplesPerBatch);
            }

            // Calculate statistics
            double mean = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - mean, 2)).Average());
            double cv = stdDev / mean; // Coefficient of variation

            // Verify timing consistency (CV < 50% to allow for system noise)
            Assert.True(cv < 0.5, 
                $"Add operation timing shows high variability (CV={cv:P2}), suggesting non-constant time complexity");
        }

        [Theory]
        [InlineData(4)]
        [InlineData(16)]
        public void Merge_Performance_ScalesWithPrecision(int precision)
        {
            const int estimatorCount = 100;
            const int itemsPerEstimator = 1000;
            var sw = new Stopwatch();

            // Create and populate estimators
            var estimators = Enumerable.Range(0, estimatorCount)
                .Select(i => new CardinalityEstimator(b: precision))
                .ToList();

            foreach (var estimator in estimators)
            {
                for (int j = 0; j < itemsPerEstimator; j++)
                {
                    estimator.Add(j);
                }
            }

            // Measure merge time
            var merged = new CardinalityEstimator(b: precision);
            sw.Start();
            foreach (var estimator in estimators)
            {
                merged.Merge(estimator);
            }
            sw.Stop();

            // Higher precision should still complete in reasonable time
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Merge operation took {sw.ElapsedMilliseconds}ms for precision {precision}");
        }

        [Fact]
        public void Count_HasConstantTimeComplexity()
        {
            var estimator = new CardinalityEstimator(b: 14); // Use default precision
            var timings = new List<double>();
            const int measurements = 100;
            const int itemsPerBatch = 10000;
            var sw = new Stopwatch();

            // Add items in batches and measure Count() after each batch
            for (int batch = 0; batch < measurements; batch++)
            {
                // Add more items
                for (int i = 0; i < itemsPerBatch; i++)
                {
                    estimator.Add(batch * itemsPerBatch + i);
                }

                // Measure Count() operation
                sw.Restart();
                estimator.Count();
                sw.Stop();
                timings.Add(sw.Elapsed.TotalMilliseconds);
            }

            // Verify Count() timing remains relatively constant
            double mean = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - mean, 2)).Average());
            double cv = stdDev / mean;

            Assert.True(cv < 0.5, 
                $"Count operation timing shows high variability (CV={cv:P2}), suggesting non-constant time complexity");
        }
    }
}

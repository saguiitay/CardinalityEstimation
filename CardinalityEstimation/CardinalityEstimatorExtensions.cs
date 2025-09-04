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

namespace CardinalityEstimation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides extension methods and utilities for cardinality estimators, particularly for concurrent operations.
    /// </summary>
    public static class CardinalityEstimatorExtensions
    {
        /// <summary>
        /// Converts a regular CardinalityEstimator to a thread-safe ConcurrentCardinalityEstimator
        /// </summary>
        /// <param name="estimator">The estimator to convert</param>
        /// <returns>A new thread-safe ConcurrentCardinalityEstimator</returns>
        public static ConcurrentCardinalityEstimator ToConcurrent(this CardinalityEstimator estimator)
        {
            if (estimator == null)
                return null;

            return new ConcurrentCardinalityEstimator(estimator);
        }

        /// <summary>
        /// Merges a collection of CardinalityEstimators in parallel
        /// </summary>
        /// <param name="estimators">The estimators to merge</param>
        /// <param name="parallelismDegree">Maximum degree of parallelism. If null, uses default Task scheduler behavior.</param>
        /// <returns>A new ConcurrentCardinalityEstimator with merged results</returns>
        public static ConcurrentCardinalityEstimator ParallelMerge(this IEnumerable<CardinalityEstimator> estimators, int? parallelismDegree = null)
        {
            if (estimators == null)
                return null;

            var estimatorList = estimators.Where(e => e != null).ToList();
            if (!estimatorList.Any())
                return null;

            // Convert all to concurrent estimators first
            var concurrentEstimators = estimatorList.AsParallel()
                .WithDegreeOfParallelism(parallelismDegree ?? Environment.ProcessorCount)
                .Select(e => new ConcurrentCardinalityEstimator(e))
                .ToList();

            return ConcurrentCardinalityEstimator.ParallelMerge(concurrentEstimators, parallelismDegree);
        }

        /// <summary>
        /// Safely merges estimators with automatic null checking and type conversion
        /// </summary>
        /// <param name="estimators">Mixed collection of CardinalityEstimator and ConcurrentCardinalityEstimator instances</param>
        /// <returns>A merged ConcurrentCardinalityEstimator or null if no valid estimators provided</returns>
        public static ConcurrentCardinalityEstimator SafeMerge(params object[] estimators)
        {
            if (estimators == null || !estimators.Any())
                return null;

            var validEstimators = new List<ConcurrentCardinalityEstimator>();

            foreach (var estimator in estimators)
            {
                switch (estimator)
                {
                    case ConcurrentCardinalityEstimator concurrent:
                        validEstimators.Add(concurrent);
                        break;
                    case CardinalityEstimator regular:
                        validEstimators.Add(new ConcurrentCardinalityEstimator(regular));
                        break;
                    case null:
                        continue;
                    default:
                        throw new ArgumentException($"Invalid estimator type: {estimator.GetType()}", nameof(estimators));
                }
            }

            return ConcurrentCardinalityEstimator.Merge(validEstimators);
        }

        /// <summary>
        /// Creates multiple concurrent estimators for distributed processing scenarios
        /// </summary>
        /// <param name="count">Number of estimators to create</param>
        /// <param name="hashFunction">Hash function to use (optional)</param>
        /// <param name="b">Accuracy parameter</param>
        /// <param name="useDirectCounting">Whether to use direct counting for small cardinalities</param>
        /// <returns>Array of concurrent cardinality estimators</returns>
        public static ConcurrentCardinalityEstimator[] CreateMultiple(int count, GetHashCodeDelegate hashFunction = null, int b = 14, bool useDirectCounting = true)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive");

            var estimators = new ConcurrentCardinalityEstimator[count];
            for (int i = 0; i < count; i++)
            {
                estimators[i] = new ConcurrentCardinalityEstimator(hashFunction, b, useDirectCounting);
            }

            return estimators;
        }

        /// <summary>
        /// Executes an action in parallel across multiple estimators
        /// </summary>
        /// <typeparam name="T">Type of elements to add</typeparam>
        /// <param name="estimators">The estimators to operate on</param>
        /// <param name="elements">Elements to distribute across estimators</param>
        /// <param name="partitionStrategy">Strategy for partitioning elements across estimators</param>
        public static void ParallelAdd<T>(this ConcurrentCardinalityEstimator[] estimators, 
            IEnumerable<T> elements, 
            PartitionStrategy partitionStrategy = PartitionStrategy.RoundRobin)
        {
            if (estimators == null || !estimators.Any())
                throw new ArgumentException("Estimators array cannot be null or empty", nameof(estimators));

            if (elements == null)
                return;

            var elementList = elements.ToList();
            if (!elementList.Any())
                return;

            var partitioner = CreatePartitioner<T>(estimators.Length, partitionStrategy);
            var partitionedElements = partitioner((IList<T>)elementList);

            Parallel.ForEach(partitionedElements, partition =>
            {
                var (estimatorIndex, elementBatch) = partition;
                var estimator = estimators[estimatorIndex];

                foreach (var element in elementBatch)
                {
                    // Use dynamic dispatch to call the appropriate Add method
                    switch (element)
                    {
                        case string s:
                            ((ICardinalityEstimator<string>)estimator).Add(s);
                            break;
                        case int i:
                            ((ICardinalityEstimator<int>)estimator).Add(i);
                            break;
                        case uint ui:
                            ((ICardinalityEstimator<uint>)estimator).Add(ui);
                            break;
                        case long l:
                            ((ICardinalityEstimator<long>)estimator).Add(l);
                            break;
                        case ulong ul:
                            ((ICardinalityEstimator<ulong>)estimator).Add(ul);
                            break;
                        case float f:
                            ((ICardinalityEstimator<float>)estimator).Add(f);
                            break;
                        case double d:
                            ((ICardinalityEstimator<double>)estimator).Add(d);
                            break;
                        case byte[] ba:
                            ((ICardinalityEstimator<byte[]>)estimator).Add(ba);
                            break;
                        default:
                            throw new ArgumentException($"Unsupported element type: {typeof(T)}", nameof(elements));
                    }
                }
            });
        }

        private static Func<IList<T>, IEnumerable<(int EstimatorIndex, IEnumerable<T> Elements)>> CreatePartitioner<T>(
            int estimatorCount, 
            PartitionStrategy strategy)
        {
            return strategy switch
            {
                PartitionStrategy.RoundRobin => elements => elements
                    .Select((element, index) => (EstimatorIndex: index % estimatorCount, Element: element))
                    .GroupBy(x => x.EstimatorIndex)
                    .Select(g => (g.Key, Elements: g.Select(x => x.Element))),
                    
                PartitionStrategy.Chunked => elements =>
                {
                    var chunkSize = Math.Max(1, elements.Count / estimatorCount);
                    return elements
                        .Select((element, index) => (EstimatorIndex: Math.Min(index / chunkSize, estimatorCount - 1), Element: element))
                        .GroupBy(x => x.EstimatorIndex)
                        .Select(g => (g.Key, Elements: g.Select(x => x.Element)));
                },
                
                PartitionStrategy.Hash => elements => elements
                    .Select(element => (EstimatorIndex: Math.Abs(element.GetHashCode()) % estimatorCount, Element: element))
                    .GroupBy(x => x.EstimatorIndex)
                    .Select(g => (g.Key, Elements: g.Select(x => x.Element))),
                    
                _ => throw new ArgumentException($"Unknown partition strategy: {strategy}", nameof(strategy))
            };
        }
    }

    /// <summary>
    /// Strategy for partitioning elements across multiple estimators
    /// </summary>
    public enum PartitionStrategy
    {
        /// <summary>
        /// Distribute elements in round-robin fashion
        /// </summary>
        RoundRobin,
        
        /// <summary>
        /// Split elements into contiguous chunks
        /// </summary>
        Chunked,
        
        /// <summary>
        /// Distribute based on hash code of elements
        /// </summary>
        Hash
    }
}
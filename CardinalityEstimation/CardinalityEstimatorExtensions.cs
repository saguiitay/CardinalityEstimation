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
    /// Provides extension methods and utilities for cardinality estimators, particularly for concurrent operations
    /// and advanced scenarios like parallel processing and distributed computing.
    /// </summary>
    /// <remarks>
    /// <para>This class contains methods that extend the functionality of both regular and concurrent
    /// cardinality estimators with additional capabilities for high-performance and distributed scenarios.</para>
    /// <para>The methods in this class are designed to be thread-safe when working with concurrent estimators
    /// and provide optimizations for bulk operations.</para>
    /// </remarks>
    public static class CardinalityEstimatorExtensions
    {
        /// <summary>
        /// Converts a regular CardinalityEstimator to a thread-safe ConcurrentCardinalityEstimator.
        /// </summary>
        /// <param name="estimator">The estimator to convert</param>
        /// <returns>
        /// A new thread-safe ConcurrentCardinalityEstimator with the same state and configuration,
        /// or null if the input estimator is null
        /// </returns>
        /// <remarks>
        /// This method creates a snapshot of the current estimator state and initializes a new
        /// concurrent estimator with that state. The original estimator remains unchanged.
        /// </remarks>
        public static ConcurrentCardinalityEstimator ToConcurrent(this CardinalityEstimator estimator)
        {
            if (estimator == null)
                return null;

            return new ConcurrentCardinalityEstimator(estimator);
        }

        /// <summary>
        /// Merges a collection of CardinalityEstimators in parallel for improved performance
        /// with large numbers of estimators.
        /// </summary>
        /// <param name="estimators">The collection of estimators to merge</param>
        /// <param name="parallelismDegree">
        /// Maximum degree of parallelism. If null, uses the default Task scheduler behavior 
        /// based on available processor cores.
        /// </param>
        /// <returns>
        /// A new ConcurrentCardinalityEstimator containing the merged results of all input estimators,
        /// or null if no valid estimators are provided
        /// </returns>
        /// <remarks>
        /// <para>This method first converts all regular estimators to concurrent estimators, then
        /// uses parallel merge algorithms to efficiently combine large numbers of estimators.</para>
        /// <para>All input estimators must have the same accuracy parameters (bitsPerIndex).</para>
        /// <para>The parallel approach is most beneficial when merging many estimators (typically 10+).</para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown when estimators have different accuracy parameters
        /// </exception>
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
        /// Safely merges estimators with automatic null checking and type conversion.
        /// This method accepts mixed collections of CardinalityEstimator and ConcurrentCardinalityEstimator instances.
        /// </summary>
        /// <param name="estimators">
        /// Mixed collection of CardinalityEstimator and ConcurrentCardinalityEstimator instances to merge
        /// </param>
        /// <returns>
        /// A merged ConcurrentCardinalityEstimator containing the union of all input estimators,
        /// or null if no valid estimators are provided
        /// </returns>
        /// <remarks>
        /// <para>This method provides a convenient way to merge estimators of different types without
        /// explicit conversion. Regular estimators are automatically converted to concurrent estimators.</para>
        /// <para>Null values in the input are automatically filtered out.</para>
        /// <para>All estimators must have compatible accuracy parameters.</para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown when an estimator has an unsupported type or when estimators have incompatible parameters
        /// </exception>
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
        /// Creates multiple concurrent estimators for distributed processing scenarios where
        /// you need to process data across multiple threads or nodes.
        /// </summary>
        /// <param name="count">Number of estimators to create</param>
        /// <param name="hashFunction">
        /// Hash function to use for all estimators. If null, uses the default XxHash128.
        /// All estimators will use the same hash function for compatibility.
        /// </param>
        /// <param name="b">
        /// Accuracy parameter for all estimators. Must be in the range [4, 16].
        /// All estimators will have the same accuracy to ensure they can be merged.
        /// </param>
        /// <param name="useDirectCounting">
        /// Whether to enable direct counting for small cardinalities on all estimators
        /// </param>
        /// <returns>An array of concurrent cardinality estimators with identical configurations</returns>
        /// <remarks>
        /// <para>This method is useful for distributed processing scenarios where you want to
        /// process data in parallel across multiple estimators and then merge the results.</para>
        /// <para>All created estimators have identical configurations to ensure compatibility
        /// when merging results.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="count"/> is less than or equal to zero, or when
        /// <paramref name="b"/> is not in the valid range [4, 16]
        /// </exception>
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
        /// Executes element addition operations in parallel across multiple estimators using
        /// a specified partitioning strategy for optimal load distribution.
        /// </summary>
        /// <typeparam name="T">Type of elements to add to the estimators</typeparam>
        /// <param name="estimators">The array of estimators to distribute elements across</param>
        /// <param name="elements">Collection of elements to add to the estimators</param>
        /// <param name="partitionStrategy">
        /// Strategy for partitioning elements across estimators. Different strategies may be
        /// optimal for different data distributions and processing patterns.
        /// </param>
        /// <remarks>
        /// <para>This method is designed for high-throughput scenarios where you need to process
        /// large numbers of elements across multiple estimators in parallel.</para>
        /// <para>The choice of partition strategy can affect performance and load balancing:
        /// - RoundRobin: Good for uniform element distribution
        /// - Chunked: Good for maintaining locality of reference  
        /// - Hash: Good for ensuring consistent assignment of similar elements</para>
        /// <para>Supported element types: string, int, uint, long, ulong, float, double, byte[]</para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown when the estimators array is null or empty
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when elements contain unsupported types
        /// </exception>
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

        /// <summary>
        /// Creates a partitioner function that distributes elements across estimators according
        /// to the specified strategy.
        /// </summary>
        /// <typeparam name="T">Type of elements to partition</typeparam>
        /// <param name="estimatorCount">Number of estimators to distribute across</param>
        /// <param name="strategy">Partitioning strategy to use</param>
        /// <returns>
        /// A function that takes a list of elements and returns partitions with estimator indices
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when an unknown partition strategy is specified
        /// </exception>
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
    /// Defines strategies for partitioning elements across multiple cardinality estimators
    /// in parallel processing scenarios.
    /// </summary>
    /// <remarks>
    /// The choice of partition strategy can significantly impact performance and load balancing
    /// depending on the characteristics of your data and processing environment.
    /// </remarks>
    public enum PartitionStrategy
    {
        /// <summary>
        /// Distributes elements in round-robin fashion across estimators.
        /// This strategy provides good load balancing for uniformly distributed data
        /// and is the default choice for most scenarios.
        /// </summary>
        /// <remarks>
        /// Elements are assigned to estimators in cyclic order: first element to estimator 0,
        /// second to estimator 1, etc., wrapping around after reaching the last estimator.
        /// </remarks>
        RoundRobin,
        
        /// <summary>
        /// Splits elements into contiguous chunks, with each chunk assigned to a different estimator.
        /// This strategy maintains data locality and can improve cache performance when processing
        /// related or ordered data.
        /// </summary>
        /// <remarks>
        /// Elements are divided into approximately equal-sized contiguous chunks, with each
        /// chunk processed by a different estimator. This can be beneficial when elements
        /// have spatial or temporal locality that should be preserved.
        /// </remarks>
        Chunked,
        
        /// <summary>
        /// Distributes elements based on their hash code to ensure consistent assignment.
        /// This strategy guarantees that identical elements always go to the same estimator,
        /// which can be useful for certain distributed processing patterns.
        /// </summary>
        /// <remarks>
        /// The hash-based distribution uses the element's GetHashCode() method to determine
        /// which estimator it should be assigned to. This provides deterministic assignment
        /// and can help with deduplication scenarios.
        /// </remarks>
        Hash
    }
}
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
    using System.Buffers.Binary;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hash;

    /// <summary>
    /// A thread-safe cardinality estimator that uses concurrent data structures and atomic operations
    /// to support multi-threaded scenarios safely.
    /// </summary>
    /// <remarks>
    /// This implementation provides thread-safe access to all operations including Add, Count, and Merge.
    /// It uses a ReaderWriterLockSlim for operations that need to switch between sparse and dense representations,
    /// and atomic operations where possible for better performance.
    /// </remarks>
    [Serializable]
    public class ConcurrentCardinalityEstimator : ICardinalityEstimator<string>, ICardinalityEstimator<int>, ICardinalityEstimator<uint>,
        ICardinalityEstimator<long>, ICardinalityEstimator<ulong>, ICardinalityEstimator<float>, ICardinalityEstimator<double>,
        ICardinalityEstimator<byte[]>, ICardinalityEstimatorMemory, IEquatable<ConcurrentCardinalityEstimator>, IDisposable
    {
        #region Private fields
        /// <summary>
        /// Number of bits for indexing HLL sub-streams - the number of estimators is 2^bitsPerIndex
        /// </summary>
        private readonly int bitsPerIndex;

        /// <summary>
        /// Number of bits to compute the HLL estimate on
        /// </summary>
        private readonly byte bitsForHll;

        /// <summary>
        /// HLL lookup table size
        /// </summary>
        private readonly int m;

        /// <summary>
        /// Fixed bias correction factor
        /// </summary>
        private readonly double alphaM;

        /// <summary>
        /// Threshold determining whether to use LinearCounting or HyperLogLog based on an initial estimate
        /// </summary>
        private readonly double subAlgorithmSelectionThreshold;

        /// <summary>
        /// Lookup table for the dense representation (thread-safe through synchronization)
        /// </summary>
        private volatile byte[] lookupDense;

        /// <summary>
        /// Lookup dictionary for the sparse representation (thread-safe)
        /// </summary>
        private volatile ConcurrentDictionary<ushort, byte> lookupSparse;

        /// <summary>
        /// Max number of elements to hold in the sparse representation
        /// </summary>
        private readonly int sparseMaxElements;

        /// <summary>
        /// Indicates that the sparse representation is currently used
        /// </summary>
        private volatile bool isSparse;

        /// <summary>
        /// Set for direct counting of elements (thread-safe). Used as a concurrent hash set:
        /// only the keys matter; the byte value is a placeholder. This gives O(1) Add/Contains/Count
        /// without the per-call allocations and O(n) deduplication that a <see cref="ConcurrentBag{T}"/>
        /// would require (since a bag permits duplicates).
        /// </summary>
        private volatile ConcurrentDictionary<ulong, byte> directCount;

        /// <summary>
        /// Hash function used
        /// </summary>
        [NonSerialized]
        private readonly GetHashCodeDelegate hashFunction;
        [NonSerialized]
        private GetHashCodeSpanDelegate hashFunctionSpan;
        /// <summary>
        /// Count of additions for tracking purposes
        /// </summary>
        private long countAdditions;

        /// <summary>
        /// Lock for protecting operations that need to switch between sparse and dense representations
        /// </summary>
        [NonSerialized]
        private readonly ReaderWriterLockSlim lockSlim;

        /// <summary>
        /// Monotonically-increasing per-instance ID used to impose a total order on instances
        /// when acquiring two locks together (e.g., in <see cref="Merge"/> and <see cref="Equals(object)"/>).
        /// Using a unique ID instead of <see cref="object.GetHashCode"/> guarantees the ordering is
        /// strict (never equal for two distinct instances) and therefore deadlock-free.
        /// </summary>
        [NonSerialized]
        private readonly long instanceId;

        /// <summary>
        /// Source for <see cref="instanceId"/> values; incremented atomically per allocation.
        /// </summary>
        private static long instanceIdCounter;

        /// <summary>
        /// Tracks if this instance has been disposed
        /// </summary>
        private volatile bool disposed;
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new instance of ConcurrentCardinalityEstimator
        /// </summary>
        /// <param name="hashFunction">Hash function delegate to use. If null, defaults to XxHash128</param>
        /// <param name="b">
        /// Number of bits determining accuracy and memory consumption, in the range [4, 16] (higher = greater accuracy and memory usage).
        /// For large cardinalities, the standard error is 1.04 * 2^(-b/2), and the memory consumption is bounded by 2^b kilobytes.
        /// The default value of 14 typically yields 3% error or less across the entire range of cardinalities (usually much less),
        /// and uses up to ~16kB of memory.  b=4 yields less than ~100% error and uses less than 1kB. b=16 uses up to ~64kB and usually yields 1%
        /// error or less
        /// </param>
        /// <param name="useDirectCounting">
        /// True if direct count should be used for up to <see cref="HllConstants.DirectCounterMaxElements"/> elements.
        /// False if direct count should be avoided and use always estimation, even for low cardinalities.
        /// </param>
        public ConcurrentCardinalityEstimator(GetHashCodeDelegate hashFunction = null, int b = 14, bool useDirectCounting = true)
            : this(hashFunction, HllConstants.CreateEmptyState(b, useDirectCounting))
        { }

        /// <summary>
        /// Copy constructor for creating a thread-safe copy from a regular CardinalityEstimator
        /// </summary>
        public ConcurrentCardinalityEstimator(CardinalityEstimator other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            var state = other.GetState();
            bitsPerIndex = state.BitsPerIndex;
            bitsForHll = (byte)(64 - bitsPerIndex);
            m = 1 << bitsPerIndex;
            alphaM = HllConstants.GetAlphaM(m);
            subAlgorithmSelectionThreshold = HllConstants.GetSubAlgorithmSelectionThreshold(bitsPerIndex);

            lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            instanceId = Interlocked.Increment(ref instanceIdCounter);

            // Preserve the source estimator's hash functions so the conversion is lossless even
            // when a non-default hash (e.g. Murmur3, FNV-1a, or a custom delegate) was supplied
            // to the original CardinalityEstimator.
            hashFunction = other.HashFunction
                ?? ((x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x)));
            hashFunctionSpan = other.HashFunctionSpan
                ?? ((x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x)));

            sparseMaxElements = Math.Max(0, (m / 15) - 10);

            InitializeFromState(hashFunction, state);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public ConcurrentCardinalityEstimator(ConcurrentCardinalityEstimator other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            other.ThrowIfDisposed();

            other.lockSlim.EnterReadLock();
            try
            {
                var state = other.GetStateInternal();
                bitsPerIndex = state.BitsPerIndex;
                bitsForHll = (byte)(64 - bitsPerIndex);
                m = 1 << bitsPerIndex;
                alphaM = HllConstants.GetAlphaM(m);
                subAlgorithmSelectionThreshold = HllConstants.GetSubAlgorithmSelectionThreshold(bitsPerIndex);

                lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            instanceId = Interlocked.Increment(ref instanceIdCounter);
                hashFunction = other.hashFunction;
                sparseMaxElements = Math.Max(0, (m / 15) - 10);

                InitializeFromState(other.hashFunction, state);
            }
            finally
            {
                other.lockSlim.ExitReadLock();
            }
        }

        /// <summary>
        /// Creates a CardinalityEstimator with the given <paramref name="hashFunction" /> and <paramref name="state" />
        /// </summary>
        internal ConcurrentCardinalityEstimator(GetHashCodeDelegate hashFunction, CardinalityEstimatorState state)
            : this(state)
        {
            // Init the hash function
            this.hashFunction = hashFunction;
            if (this.hashFunction == null)
            {
                this.hashFunction = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
                this.hashFunctionSpan = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
            }
            else
            {
                this.hashFunctionSpan = (x) => hashFunction(x.ToArray());
            }

        }

        /// <summary>
        /// Creates a CardinalityEstimator with the given <paramref name="hashFunctionSpan" /> and <paramref name="state" />
        /// </summary>
        internal ConcurrentCardinalityEstimator(GetHashCodeSpanDelegate hashFunctionSpan, CardinalityEstimatorState state)
            : this(state)
        {
            // Init the hash function
            this.hashFunctionSpan = hashFunctionSpan;
            if (this.hashFunctionSpan == null)
            {
                this.hashFunction = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
                this.hashFunctionSpan = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x));
            }
            else
            {
                this.hashFunction = (x) => hashFunctionSpan(x);
            }
        }

        /// <summary>
        /// Creates a ConcurrentCardinalityEstimator with the given <paramref name="state" />
        /// </summary>
        internal ConcurrentCardinalityEstimator(CardinalityEstimatorState state)
        {
            bitsPerIndex = state.BitsPerIndex;
            bitsForHll = (byte)(64 - bitsPerIndex);
            m = 1 << bitsPerIndex;
            alphaM = HllConstants.GetAlphaM(m);
            subAlgorithmSelectionThreshold = HllConstants.GetSubAlgorithmSelectionThreshold(bitsPerIndex);

            lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            instanceId = Interlocked.Increment(ref instanceIdCounter);

            sparseMaxElements = Math.Max(0, (m / 15) - 10);

            InitializeFromState(this.hashFunction, state);
        }

        private void InitializeFromState(GetHashCodeDelegate hashFunction, CardinalityEstimatorState state)
        {
            // Init the direct count
            if (state.DirectCount != null)
            {
                directCount = new ConcurrentDictionary<ulong, byte>();
                foreach (ulong element in state.DirectCount)
                {
                    directCount.TryAdd(element, 0);
                }
            }

            // Init the sparse representation
            isSparse = state.IsSparse;
            if (state.LookupSparse != null)
            {
                lookupSparse = new ConcurrentDictionary<ushort, byte>(state.LookupSparse);
            }
            
            lookupDense = state.LookupDense;
            countAdditions = (long)state.CountAdditions;
            
            // If necessary, switch to the dense representation
            if (sparseMaxElements <= 0)
            {
                SwitchToDenseRepresentation();
            }

            // if DirectCount is not null, populate the HLL lookup with its elements
            if (directCount != null)
            {
                // since we are re-initializing the object, we need to reset isSparse to true and sparse lookup
                isSparse = true;
                lookupSparse = new ConcurrentDictionary<ushort, byte>();
                foreach (ulong element in directCount.Keys)
                {
                    AddElementHashInternal(element);
                }
            }
        }
        #endregion

        #region Public properties
        public ulong CountAdditions => (ulong)Interlocked.Read(ref countAdditions);

        /// <summary>
        /// Gets the byte-array hash function used by this estimator. Returned so that callers
        /// (e.g. <see cref="ToCardinalityEstimator"/>) can preserve the original hash function
        /// across conversions and copies, making such operations lossless even when a non-default
        /// hash was supplied. Safe to read without a lock — the field is set once at construction
        /// and never reassigned afterwards.
        /// </summary>
        public GetHashCodeDelegate HashFunction => hashFunction;

        /// <summary>
        /// Gets the span hash function used by this estimator (the zero-allocation path used by
        /// the <c>Span&lt;byte&gt;</c> / <c>ReadOnlySpan&lt;byte&gt;</c> / <c>Memory&lt;byte&gt;</c>
        /// overloads). Exposed for the same lossless-conversion reason as <see cref="HashFunction"/>.
        /// </summary>
        public GetHashCodeSpanDelegate HashFunctionSpan => hashFunctionSpan;
        #endregion

        #region Public methods
        /// <summary>
        /// Add an element of type <see cref="string"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null</exception>
        public bool Add(string element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            ThrowIfDisposed();

            ulong hashCode;
            if (Encoding.UTF8.GetMaxByteCount(element.Length) <= HllConstants.StackallocByteThreshold)
            {
                Span<byte> bytes = stackalloc byte[HllConstants.StackallocByteThreshold];
                int written = Encoding.UTF8.GetBytes(element, bytes);
                hashCode = hashFunctionSpan(bytes.Slice(0, written));
            }
            else
            {
                hashCode = hashFunctionSpan(Encoding.UTF8.GetBytes(element));
            }

            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="int"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(int element)
        {
            ThrowIfDisposed();
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, element);
            ulong hashCode = hashFunctionSpan(bytes);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="uint"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(uint element)
        {
            ThrowIfDisposed();
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, element);
            ulong hashCode = hashFunctionSpan(bytes);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="long"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(long element)
        {
            ThrowIfDisposed();
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, element);
            ulong hashCode = hashFunctionSpan(bytes);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="ulong"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(ulong element)
        {
            ThrowIfDisposed();
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, element);
            ulong hashCode = hashFunctionSpan(bytes);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="float"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(float element)
        {
            ThrowIfDisposed();
            Span<byte> bytes = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleLittleEndian(bytes, element);
            ulong hashCode = hashFunctionSpan(bytes);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="double"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(double element)
        {
            ThrowIfDisposed();
            Span<byte> bytes = stackalloc byte[sizeof(double)];
            BinaryPrimitives.WriteDoubleLittleEndian(bytes, element);
            ulong hashCode = hashFunctionSpan(bytes);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="byte[]"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null</exception>
        public bool Add(byte[] element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            ThrowIfDisposed();
            ulong hashCode = hashFunction(element);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }


        /// <summary>
        /// Add an element of type <see cref="byte[]"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(Span<byte> element)
        {
            ThrowIfDisposed();
            ulong hashCode = hashFunctionSpan(element);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="byte[]"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(ReadOnlySpan<byte> element)
        {
            ThrowIfDisposed();
            ulong hashCode = hashFunctionSpan(element);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="byte[]"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(Memory<byte> element)
        {
            ThrowIfDisposed();
            ulong hashCode = hashFunctionSpan(element.Span);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Add an element of type <see cref="byte[]"/>
        /// </summary>
        /// <returns>True is estimator's state was modified. False otherwise</returns>
        public bool Add(ReadOnlyMemory<byte> element)
        {
            ThrowIfDisposed();
            ulong hashCode = hashFunctionSpan(element.Span);
            bool changed = AddElementHash(hashCode);
            Interlocked.Increment(ref countAdditions);
            return changed;
        }

        /// <summary>
        /// Returns the estimated number of items in the estimator
        /// </summary>
        /// <remarks>
        /// If Direct Count is enabled, and only a few items were added, the exact count is returned
        /// </remarks>
        public ulong Count()
        {
            ThrowIfDisposed();

            lockSlim.EnterReadLock();
            try
            {
                // If only a few elements have been seen, return the exact count
                if (directCount != null)
                {
                    return (ulong)directCount.Count;
                }

                return ComputeCountInternal();
            }
            finally
            {
                lockSlim.ExitReadLock();
            }
        }

        /// <summary>
        /// Merges the given <paramref name="other" /> ConcurrentCardinalityEstimator instance into this one
        /// </summary>
        /// <param name="other">another instance of ConcurrentCardinalityEstimator</param>
        public void Merge(ConcurrentCardinalityEstimator other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            ThrowIfDisposed();
            other.ThrowIfDisposed();

            if (other.m != m)
            {
                throw new ArgumentOutOfRangeException(nameof(other),
                    "Cannot merge ConcurrentCardinalityEstimator instances with different accuracy/map sizes");
            }

            // Lock both instances in a consistent order to prevent deadlocks. Use the unique
            // per-instance ID rather than GetHashCode() so the ordering is strict and total.
            var lockOrder = instanceId < other.instanceId ? new[] { this, other } : new[] { other, this };
            
            lockOrder[0].lockSlim.EnterWriteLock();
            try
            {
                lockOrder[1].lockSlim.EnterReadLock();
                try
                {
                    MergeInternal(other);
                }
                finally
                {
                    lockOrder[1].lockSlim.ExitReadLock();
                }
            }
            finally
            {
                lockOrder[0].lockSlim.ExitWriteLock();
            }
        }

        /// <summary>
        /// Merges the given regular <paramref name="other" /> CardinalityEstimator instance into this one
        /// </summary>
        /// <param name="other">another instance of CardinalityEstimator</param>
        public void Merge(CardinalityEstimator other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            ThrowIfDisposed();

            var state = other.GetState();
            if (state.BitsPerIndex != bitsPerIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(other),
                    "Cannot merge CardinalityEstimator instances with different accuracy/map sizes");
            }

            lockSlim.EnterWriteLock();
            try
            {
                var tempEstimator = new ConcurrentCardinalityEstimator(hashFunction, state);
                MergeInternal(tempEstimator);
            }
            finally
            {
                lockSlim.ExitWriteLock();
            }
        }

        /// <summary>
        /// Creates a snapshot of the current estimator as a regular CardinalityEstimator
        /// </summary>
        /// <returns>A non-thread-safe snapshot of this estimator</returns>
        public CardinalityEstimator ToCardinalityEstimator()
        {
            ThrowIfDisposed();

            lockSlim.EnterReadLock();
            try
            {
                var state = GetStateInternal();
                return new CardinalityEstimator(hashFunction, hashFunctionSpan, state);
            }
            finally
            {
                lockSlim.ExitReadLock();
            }
        }
        #endregion

        /// <summary>
        /// Merges <paramref name="estimators" /> into a new <see cref="ConcurrentCardinalityEstimator" />.
        /// </summary>
        /// <param name="estimators">Instances of <see cref="ConcurrentCardinalityEstimator"/></param>
        /// <returns>
        /// A new <see cref="ConcurrentCardinalityEstimator" /> if there is at least one non-null estimator in
        /// <paramref name="estimators" />; otherwise <see langword="null" />.
        /// </returns>
        public static ConcurrentCardinalityEstimator Merge(IEnumerable<ConcurrentCardinalityEstimator> estimators)
        {
            if (estimators == null)
            {
                return null;
            }

            ConcurrentCardinalityEstimator result = null;
            foreach (var estimator in estimators)
            {
                if (estimator == null)
                {
                    continue;
                }

                estimator.ThrowIfDisposed();

                if (result == null)
                {
                    result = new ConcurrentCardinalityEstimator(estimator);
                }
                else
                {
                    result.Merge(estimator);
                }
            }

            return result;
        }

        /// <summary>
        /// Merges <paramref name="estimators" /> in parallel into a new <see cref="ConcurrentCardinalityEstimator" />.
        /// This method processes multiple estimators concurrently for better performance with large collections.
        /// </summary>
        /// <param name="estimators">Instances of <see cref="ConcurrentCardinalityEstimator"/></param>
        /// <param name="parallelismDegree">Maximum degree of parallelism. If null, uses default Task scheduler behavior.</param>
        /// <returns>
        /// A new <see cref="ConcurrentCardinalityEstimator" /> if there is at least one non-null estimator;
        /// otherwise <see langword="null" />.
        /// </returns>
        public static ConcurrentCardinalityEstimator ParallelMerge(IEnumerable<ConcurrentCardinalityEstimator> estimators, int? parallelismDegree = null)
        {
            if (estimators == null)
            {
                return null;
            }

            var estimatorList = estimators.Where(e => e != null).ToList();
            if (!estimatorList.Any())
            {
                return null;
            }

            // Verify all estimators have the same parameters
            var first = estimatorList[0];
            foreach (var estimator in estimatorList)
            {
                estimator.ThrowIfDisposed();
                if (estimator.bitsPerIndex != first.bitsPerIndex)
                {
                    throw new ArgumentException("All estimators must have the same bitsPerIndex parameter", nameof(estimators));
                }
            }

            if (estimatorList.Count == 1)
            {
                return new ConcurrentCardinalityEstimator(estimatorList[0]);
            }

            // Merge in batches to reduce memory pressure
            const int batchSize = 8;
            var batches = estimatorList
                .Select((estimator, index) => new { estimator, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.estimator).ToList())
                .ToList();

            ParallelQuery<List<ConcurrentCardinalityEstimator>> batchQuery = batches.AsParallel();
            if (parallelismDegree.HasValue)
            {
                batchQuery = batchQuery.WithDegreeOfParallelism(parallelismDegree.Value);
            }

            var batchResults = batchQuery
                .Select(batch =>
                {
                    var result = new ConcurrentCardinalityEstimator(batch[0]);
                    for (int i = 1; i < batch.Count; i++)
                    {
                        result.Merge(batch[i]);
                    }
                    return result;
                })
                .ToList();

            // Final merge of batch results
            var finalResult = batchResults[0];
            for (int i = 1; i < batchResults.Count; i++)
            {
                finalResult.Merge(batchResults[i]);
                batchResults[i].Dispose();
            }

            return finalResult;
        }

        #region Private/Internal methods
        internal CardinalityEstimatorState GetState()
        {
            ThrowIfDisposed();

            lockSlim.EnterReadLock();
            try
            {
                return GetStateInternal();
            }
            finally
            {
                lockSlim.ExitReadLock();
            }
        }

        private CardinalityEstimatorState GetStateInternal()
        {
            HashSet<ulong> directCountSet = null;
            if (directCount != null)
            {
                directCountSet = new HashSet<ulong>(directCount.Keys);
            }

            Dictionary<ushort, byte> sparseLookup = null;
            if (lookupSparse != null)
            {
                sparseLookup = new Dictionary<ushort, byte>(lookupSparse);
            }

            return new CardinalityEstimatorState
            {
                BitsPerIndex = bitsPerIndex,
                DirectCount = directCountSet,
                IsSparse = isSparse,
                LookupDense = lookupDense,
                LookupSparse = sparseLookup,
                CountAdditions = CountAdditions,
            };
        }

        private bool AddElementHash(ulong hashCode)
        {
            lockSlim.EnterUpgradeableReadLock();
            try
            {
                return AddElementHashInternal(hashCode);
            }
            finally
            {
                lockSlim.ExitUpgradeableReadLock();
            }
        }

        private bool AddElementHashInternal(ulong hashCode)
        {
            var changed = false;
            
            if (directCount != null)
            {
                changed = directCount.TryAdd(hashCode, 0);
                int countAfter = directCount.Count;

                if (countAfter > HllConstants.DirectCounterMaxElements)
                {
                    lockSlim.EnterWriteLock();
                    try
                    {
                        // Double-check after acquiring write lock
                        if (directCount != null && directCount.Count > HllConstants.DirectCounterMaxElements)
                        {
                            directCount = null;
                            changed = true;
                        }
                    }
                    finally
                    {
                        lockSlim.ExitWriteLock();
                    }
                }
            }

            var substream = (ushort)(hashCode >> bitsForHll);
            byte sigma = CardinalityEstimator.GetSigma(hashCode, bitsForHll);
            
            if (isSparse)
            {
                byte prevRank = 0;
                byte newRank = sigma;
                
                if (lookupSparse.ContainsKey(substream))
                {
                    prevRank = lookupSparse[substream];
                    newRank = Math.Max(prevRank, sigma);
                }

                lookupSparse.AddOrUpdate(substream, newRank, (key, old) => Math.Max(old, sigma));
                changed = changed || (prevRank != sigma && newRank == sigma);

                if (lookupSparse.Count > sparseMaxElements)
                {
                    lockSlim.EnterWriteLock();
                    try
                    {
                        // Double-check after acquiring write lock
                        if (isSparse && lookupSparse.Count > sparseMaxElements)
                        {
                            SwitchToDenseRepresentation();
                            changed = true;
                        }
                    }
                    finally
                    {
                        lockSlim.ExitWriteLock();
                    }
                }
            }
            else
            {
                if (lookupDense != null)
                {
                    // For byte values, we need to use a loop-based approach since Interlocked.CompareExchange doesn't support byte
                    var currentValue = lookupDense[substream];
                    var newValue = Math.Max(currentValue, sigma);
                    
                    if (newValue != currentValue)
                    {
                        // Use a lock for thread safety when updating byte arrays
                        lock (lookupDense)
                        {
                            var actualCurrent = lookupDense[substream];
                            var actualNew = Math.Max(actualCurrent, sigma);
                            if (actualNew != actualCurrent)
                            {
                                lookupDense[substream] = actualNew;
                                changed = true;
                            }
                        }
                    }
                }
            }
            
            return changed;
        }

        private ulong ComputeCountInternal()
        {
            double zInverse = 0;
            double v = 0;

            if (isSparse)
            {
                // calc c and Z's inverse
                foreach (KeyValuePair<ushort, byte> kvp in lookupSparse)
                {
                    byte sigma = kvp.Value;
                    zInverse += HllConstants.InversePowersOfTwo[sigma];
                }
                v = m - lookupSparse.Count;
                zInverse += m - lookupSparse.Count;
            }
            else
            {
                // calc c and Z's inverse
                for (var i = 0; i < m; i++)
                {
                    byte sigma = lookupDense[i];
                    zInverse += HllConstants.InversePowersOfTwo[sigma];
                    if (sigma == 0)
                    {
                        v++;
                    }
                }
            }

            double e = alphaM * m * m / zInverse;
            if (e <= 5.0 * m)
            {
                e = BiasCorrection.CorrectBias(e, bitsPerIndex);
            }

            double h;
            if (v > 0)
            {
                // LinearCounting estimate
                h = m * Math.Log(m / v);
            }
            else
            {
                h = e;
            }

            if (h <= subAlgorithmSelectionThreshold)
            {
                return (ulong)Math.Round(h);
            }
            return (ulong)Math.Round(e);
        }

        private void MergeInternal(ConcurrentCardinalityEstimator other)
        {
            Interlocked.Add(ref countAdditions, (long)other.CountAdditions);
            
            if (isSparse && other.isSparse)
            {
                // Merge two sparse instances
                foreach (KeyValuePair<ushort, byte> kvp in other.lookupSparse)
                {
                    ushort index = kvp.Key;
                    byte otherRank = kvp.Value;
                    lookupSparse.AddOrUpdate(index, otherRank, (key, thisRank) => Math.Max(thisRank, otherRank));
                }

                // Switch to dense if necessary
                if (lookupSparse.Count > sparseMaxElements)
                {
                    SwitchToDenseRepresentation();
                }
            }
            else
            {
                // Make sure this (target) instance is dense, then merge
                SwitchToDenseRepresentation();
                if (other.isSparse)
                {
                    foreach (KeyValuePair<ushort, byte> kvp in other.lookupSparse)
                    {
                        ushort index = kvp.Key;
                        byte rank = kvp.Value;
                        
                        // Use lock for thread-safe byte array updates
                        lock (lookupDense)
                        {
                            lookupDense[index] = Math.Max(lookupDense[index], rank);
                        }
                    }
                }
                else
                {
                    lock (lookupDense)
                    {
                        for (var i = 0; i < m; i++)
                        {
                            lookupDense[i] = Math.Max(lookupDense[i], other.lookupDense[i]);
                        }
                    }
                }
            }

            if (other.directCount != null)
            {
                // Other instance is using direct counter. If this instance is also using direct counter, merge them.
                if (directCount != null)
                {
                    foreach (var key in other.directCount.Keys)
                    {
                        directCount.TryAdd(key, 0);
                    }

                    if (directCount.Count > HllConstants.DirectCounterMaxElements)
                    {
                        directCount = null;
                    }
                }
            }
            else
            {
                // Other instance is not using direct counter, make sure this instance doesn't either
                directCount = null;
            }
        }

        private void SwitchToDenseRepresentation()
        {
            if (!isSparse)
            {
                return;
            }

            lookupDense = new byte[m];
            foreach (KeyValuePair<ushort, byte> kvp in lookupSparse)
            {
                int index = kvp.Key;
                lookupDense[index] = kvp.Value;
            }
            lookupSparse = null;
            isSparse = false;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ConcurrentCardinalityEstimator));
            }
        }
        #endregion

        #region IEquatable implementation
        public bool Equals(ConcurrentCardinalityEstimator other)
        {
            if (other == null)
            {
                return false;
            }

            ThrowIfDisposed();
            other.ThrowIfDisposed();

            if (bitsPerIndex != other.bitsPerIndex ||
                bitsForHll != other.bitsForHll ||
                m != other.m ||
                Math.Abs(alphaM - other.alphaM) > double.Epsilon ||
                Math.Abs(subAlgorithmSelectionThreshold - other.subAlgorithmSelectionThreshold) > double.Epsilon ||
                sparseMaxElements != other.sparseMaxElements ||
                isSparse != other.isSparse ||
                !ReferenceEquals(hashFunction, other.hashFunction))
            {
                return false;
            }

            // For thread safety, we need to lock both instances. Use the unique per-instance ID
            // rather than GetHashCode() so the ordering is strict and total (deadlock-free).
            var lockOrder = instanceId < other.instanceId ? new[] { this, other } : new[] { other, this };
            
            lockOrder[0].lockSlim.EnterReadLock();
            try
            {
                lockOrder[1].lockSlim.EnterReadLock();
                try
                {
                    return EqualsInternal(other);
                }
                finally
                {
                    lockOrder[1].lockSlim.ExitReadLock();
                }
            }
            finally
            {
                lockOrder[0].lockSlim.ExitReadLock();
            }
        }

        private bool EqualsInternal(ConcurrentCardinalityEstimator other)
        {
            if ((lookupDense != null && other.lookupDense == null) ||
                (lookupDense == null && other.lookupDense != null))
            {
                return false;
            }

            if ((lookupSparse != null && other.lookupSparse == null) ||
                (lookupSparse == null && other.lookupSparse != null))
            {
                return false;
            }

            if ((directCount != null && other.directCount == null) ||
                (directCount == null && other.directCount != null))
            {
                return false;
            }

            if (lookupDense != null &&
                lookupDense.Length != other.lookupDense.Length)
            {
                return false;
            }
            if (lookupSparse != null &&
                lookupSparse.Count != other.lookupSparse.Count)
            {
                return false;
            }
            if (directCount != null)
            {
                if (directCount.Count != other.directCount.Count)
                {
                    return false;
                }
            }

            if (lookupDense != null)
            {
                for (int i = 0; i < lookupDense.Length; i++)
                {
                    if (lookupDense[i] != other.lookupDense[i])
                    {
                        return false;
                    }
                }
            }

            if (directCount != null)
            {
                foreach (var key in directCount.Keys)
                {
                    if (!other.directCount.ContainsKey(key))
                    {
                        return false;
                    }
                }
            }

            if (lookupSparse != null)
            {
                foreach (var kvp in lookupSparse)
                {
                    if (!other.lookupSparse.TryGetValue(kvp.Key, out var otherValue) ||
                        otherValue != kvp.Value)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        #endregion

        #region IDisposable implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                lockSlim?.Dispose();
                disposed = true;
            }
        }
        #endregion
    }
}

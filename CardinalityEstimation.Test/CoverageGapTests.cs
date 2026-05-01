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
    using System.IO;
    using System.Runtime.Serialization;
    using System.Text;
    using CardinalityEstimation.Hash;
    using Xunit;

    // Targeted tests that close coverage gaps surfaced by the cobertura report.
    // Each nested class focuses on one production class. New tests here are intentionally
    // small and assertion-focused: they only exercise previously-uncovered branches
    // (null arg validation, equality short-circuits, typed Add overloads, legacy serializer
    // paths, etc.) so that any regression in those branches is caught directly.

    public class CardinalityEstimatorCoverageTests
    {
        [Fact]
        public void Constructor_BelowMinB_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardinalityEstimator(b: 3));
        }

        [Fact]
        public void Constructor_AboveMaxB_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardinalityEstimator(b: 17));
        }

        [Fact]
        public void Add_NullString_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => hll.Add((string)null));
        }

        [Fact]
        public void Add_NullByteArray_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => hll.Add((byte[])null));
        }

        [Fact]
        public void Add_TypedOverloads_AllIncrementCountAdditions()
        {
            var hll = new CardinalityEstimator();
            hll.Add(1);
            hll.Add(2u);
            hll.Add(3L);
            hll.Add(4UL);
            hll.Add(5.0f);
            hll.Add(6.0d);
            hll.Add("seven");
            hll.Add(new byte[] { 8 });

            Assert.Equal(8UL, hll.CountAdditions);
            Assert.Equal(8UL, hll.Count());
        }

        [Fact]
        public void Add_SameValueAcrossOverloads_StillCountsCountAdditions()
        {
            var hll = new CardinalityEstimator();
            hll.Add(42);
            hll.Add(42);
            // Direct counter is exact and dedups; CountAdditions counts every Add call.
            Assert.Equal(1UL, hll.Count());
            Assert.Equal(2UL, hll.CountAdditions);
        }

        [Fact]
        public void Merge_NullOther_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => hll.Merge(null));
        }

        [Fact]
        public void Merge_DifferentBitsPerIndex_Throws()
        {
            var a = new CardinalityEstimator(b: 14);
            var b = new CardinalityEstimator(b: 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Merge(b));
        }

        [Fact]
        public void Equals_NullOther_ReturnsFalse()
        {
            var hll = new CardinalityEstimator();
            Assert.False(hll.Equals((CardinalityEstimator)null));
        }

        [Fact]
        public void Equals_DifferentBitsPerIndex_ReturnsFalse()
        {
            var a = new CardinalityEstimator(b: 14);
            var b = new CardinalityEstimator(b: 12);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentDirectCountContents_ReturnsFalse()
        {
            // Both stay in the direct-count path (under 100 elements).
            var a = new CardinalityEstimator(b: 14);
            var b = new CardinalityEstimator(b: 14);
            a.Add("alpha");
            b.Add("beta");
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_OneDirectCountOneNot_ReturnsFalse()
        {
            // A is in direct count; B disables direct counting so the field is null.
            var a = new CardinalityEstimator(b: 14, useDirectCounting: true);
            var b = new CardinalityEstimator(b: 14, useDirectCounting: false);
            a.Add("x");
            b.Add("x");
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_SameStateSameHash_ReturnsTrue()
        {
            // Use the copy constructor so hashFunction reference matches.
            var a = new CardinalityEstimator(b: 14);
            for (int i = 0; i < 5; i++) a.Add(i);
            var b = new CardinalityEstimator(a);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentSparseEntries_ReturnsFalse()
        {
            // Disable direct counting so adds populate the sparse lookup directly.
            var a = new CardinalityEstimator(b: 14, useDirectCounting: false);
            var b = new CardinalityEstimator(b: 14, useDirectCounting: false);
            for (int i = 0; i < 50; i++) a.Add($"a_{i}");
            for (int i = 0; i < 50; i++) b.Add($"b_{i}");
            Assert.False(a.Equals(b));
        }
    }

    public class CardinalityEstimatorSerializerCoverageTests
    {
        private static readonly CardinalityEstimatorSerializer Serializer = new CardinalityEstimatorSerializer();

        [Fact]
        public void Serialize_OneArgOverload_WritesData()
        {
            // The two-arg Serialize(stream, est) wrapper just delegates to the three-arg form
            // with leaveOpen: false, so we capture the bytes via ToArray before disposal.
            var hll = new CardinalityEstimator();
            hll.Add("a");
            var ms = new MemoryStream();
            Serializer.Serialize(ms, hll);
            Assert.True(ms.ToArray().Length > 0);
        }

        [Fact]
        public void Serialize_NullStream_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => Serializer.Serialize(null, hll, false));
        }

        [Fact]
        public void Serialize_NullEstimator_Throws()
        {
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentNullException>(() => Serializer.Serialize(ms, null, false));
        }

        [Fact]
        public void Write_NullWriter_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => Serializer.Write(null, hll));
        }

        [Fact]
        public void Write_NullEstimator_Throws()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            Assert.Throws<ArgumentNullException>(() => Serializer.Write(bw, null));
        }

        [Fact]
        public void Deserialize_NullStream_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Serializer.Deserialize(null));
        }

        [Fact]
        public void Read_NullReader_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Serializer.Read(null));
        }

        [Fact]
        public void Deserialize_FutureMajorVersion_ThrowsSerializationException()
        {
            // Major version above the serializer's own version is unreadable.
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((ushort)(CardinalityEstimatorSerializer.DataFormatMajorVersion + 1));
                bw.Write((ushort)0);
            }
            ms.Position = 0;

            Assert.Throws<SerializationException>(() => Serializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_TruncatedDenseLookup_ThrowsEndOfStream()
        {
            // bitsPerIndex=4 → m=16. We declare a dense lookup of 16 bytes but only write 4.
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((ushort)CardinalityEstimatorSerializer.DataFormatMajorVersion);
                bw.Write((ushort)CardinalityEstimatorSerializer.DataFormatMinorVersion);
                bw.Write(4);          // bitsPerIndex → m = 16
                bw.Write((byte)0);    // flags: dense (not sparse, not direct)
                bw.Write(16);         // count == m (passes the size check)
                bw.Write(new byte[] { 1, 2, 3, 4 }); // truncated: only 4 of the 16 bytes
            }
            ms.Position = 0;

            Assert.Throws<EndOfStreamException>(() => Serializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_LegacyV2WithNullHashFunction_PicksHashByEmbeddedId()
        {
            // v2.x format embeds the hash-function id in the stream. When the caller doesn't pass
            // a hashFunction override, the deserializer must pick one based on that embedded id
            // (1 → Murmur3, anything else → FNV-1a).
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((ushort)2);     // major
                bw.Write((ushort)0);     // minor
                bw.Write((byte)1);       // hashFunctionId = Murmur3
                bw.Write(14);            // bitsPerIndex
                bw.Write((byte)2);       // flags: sparse, not direct count
                bw.Write(0);             // sparse count
            }
            ms.Position = 0;

            // No hashFunction passed — exercises the v2 branch that picks based on the embedded id.
            var hll = Serializer.Deserialize(ms);
            Assert.Equal(0UL, hll.Count());
        }
    }

    public class ConcurrentCardinalityEstimatorCoverageTests
    {
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
        public void Merge_NullConcurrentOther_Throws()
        {
            using var a = new ConcurrentCardinalityEstimator(b: 14);
            Assert.Throws<ArgumentNullException>(() => a.Merge((ConcurrentCardinalityEstimator)null));
        }

        [Fact]
        public void Merge_DifferentBitsPerIndex_Throws()
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
        public void Equals_DifferentBitsPerIndex_ReturnsFalse()
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
    }

    public class CardinalityEstimatorExtensionsCoverageTests
    {
        [Fact]
        public void ParallelAdd_UnknownPartitionStrategy_Throws()
        {
            // The private CreatePartitioner has a default switch arm that throws
            // ArgumentException when given an undefined enum value.
            var estimators = CardinalityEstimatorExtensions.CreateMultiple(count: 2);
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    estimators.ParallelAdd(new[] { "a", "b", "c" }, (PartitionStrategy)999));
            }
            finally
            {
                foreach (var e in estimators) e.Dispose();
            }
        }
    }

    public class HashCoverageTests
    {
        [Fact]
        public void Fnv1A_NullBytes_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Fnv1A.GetHashCode(null));
        }

        [Fact]
        public void Murmur3_NullBytes_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Murmur3.GetHashCode(null));
        }
    }
}

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
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Text;
    using CardinalityEstimation.Hash;
    using Xunit;
    using Xunit.Abstractions;
    using static CardinalityEstimation.CardinalityEstimator;

    public class CardinalityEstimatorSerializerTests : IDisposable
    {
        private const int ElementSizeInBytes = 20;
        public static readonly Random Rand = new Random();

        private readonly ITestOutputHelper output;
        private readonly Stopwatch stopwatch;

        public CardinalityEstimatorSerializerTests(ITestOutputHelper outputHelper)
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

        [Fact]
        public void TestSerializerCardinality10()
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(10);

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll, false);

                results = memoryStream.ToArray();
            }

            // Expected length is 101:
            // 4 bytes for the major and minor versions
            // 4 bytes for the Bits in Index
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in DirectCount
            // 8 bytes for each element (ulong) in DirectCount
            // 8 bytes for CountAdded
            Assert.Equal(101, results.Length);

            Assert.Equal(14, BitConverter.ToInt32(results[4..8])); // Bits in Index = 14
            Assert.Equal(3, results[8]); // IsSparse = true AND IsDirectCount = true
            Assert.Equal(10, BitConverter.ToInt32(results[9..13])); // Count = 10
            Assert.Equal(10UL, BitConverter.ToUInt64(results[^8..])); // CountAdditions = 10
        }

        [Fact]
        public void TestSerializerCardinality1000()
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(1000);

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll, false);

                results = memoryStream.ToArray();
            }

            CardinalityEstimatorState data = hll.GetState();

            // Expected length is:
            // 4 bytes for the major and minor versions
            // 4 bytes for the Bits in Index 
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in lookupSparse
            // 2+1 bytes for each element (ulong) in lookupSparse
            // 8 bytes for CountAdded
            Assert.Equal(21 + (3 *data.LookupSparse.Count), results.Length);

            Assert.Equal(14, BitConverter.ToInt32(results[4..8], 0)); // Bits in Index = 14
            Assert.Equal(2, results[8]); // IsSparse = true AND IsDirectCount = false
            Assert.Equal(data.LookupSparse.Count, BitConverter.ToInt32(results[9..13]));
            Assert.Equal(1000UL, BitConverter.ToUInt64(results[(13 + (3 *data.LookupSparse.Count)) ..])); // CountAdditions = 1000
        }

        [Fact]
        public void TestSerializerCardinality100000()
        {
            TestSerializerCardinality100000Parameterized(false);
        }

        [Fact]
        public void TestSerializer()
        {
            for (var i = 0; i < 100; i++)
            {
                TestSerializerCardinality10();
                TestSerializerCardinality1000();
                TestSerializerCardinality100000Parameterized(false);
                TestSerializerCardinality100000Parameterized(true);
            }
        }

        [Fact]
        public void TestDeserializer2()
        {
            for (var i = 0; i < 100; i++)
            {
                TestDeserializerWithCardinality(10);
                TestDeserializerWithCardinality(1000);
                TestDeserializerWithCardinality(100000);

                TestDeserializer2WithCardinality(10);
                TestDeserializer2WithCardinality(1000);
                TestDeserializer2WithCardinality(100000);
            }
        }

        [Fact]
        public void TestSerializerSizes()
        {
            for (var cardinality = 1; cardinality < 10240; cardinality *= 2)
            {
                long customTotalSize = 0;
                var runs = 10;
                for (var i = 0; i < runs; i++)
                {
                    customTotalSize += TestSerializerCreatesSmallerData(cardinality);
                }

                long customAverageSize = customTotalSize/runs;

                output.WriteLine("{0} | {1}", cardinality, customAverageSize);
            }
        }

        /// <summary>
        /// If this method fails, it's possible that the serialization format has changed and
        /// <see cref="CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [Fact]
        public void SerializerCanDeserializeVersion1Point0()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v1_0), Murmur3.GetHashCode);
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v1_0), Murmur3.GetHashCode);
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v1_0), Murmur3.GetHashCode);

            Assert.Equal(50UL, hllDirect.Count());
            Assert.Equal(151UL, hllSparse.Count());
            Assert.Equal(5005UL, hllDense.Count());
        }

        [Fact]
        public void DeserializedEstimatorUsesSameHashAsOriginal()
        {
            // Prepare some elements
            IList<int> elements = new List<int>();
            for (int i = 0; i < 150; i++)
            {
                elements.Add(Rand.Next());
            }

            foreach (GetHashCodeDelegate hashFunction in new GetHashCodeDelegate[] { Murmur3.GetHashCode, Fnv1A.GetHashCode })
            {
                // Add elements to an estimator using the given hashFunctionId
                CardinalityEstimator original = new CardinalityEstimator(hashFunction: hashFunction);
                foreach (int element in elements)
                {
                    original.Add(element);
                }

                // Serialize
                var serializer = new CardinalityEstimatorSerializer();
                byte[] results;

                using (var memoryStream = new MemoryStream())
                {
                    serializer.Serialize(memoryStream, original, false);
                    results = memoryStream.ToArray();
                }

                // Deserialize
                CardinalityEstimator deserialized;
                using (var memoryStream = new MemoryStream(results))
                {
                    deserialized = serializer.Deserialize(memoryStream, hashFunction, false);
                }

                // Add the elements again, should have no effect on state
                foreach (int element in elements)
                {
                    deserialized.Add(element);
                }

                Assert.Equal(original.Count(), deserialized.Count());
            }
        }

        /// <summary>
        /// If this method fails, it's possible that the serialization format has changed and
        /// <see cref="CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [Fact]
        public void SerializerCanDeserializeVersion2Point0()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v2_0), Murmur3.GetHashCode);
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v2_0), Murmur3.GetHashCode);
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v2_0), Murmur3.GetHashCode);

            Assert.Equal(50UL, hllDirect.Count());
            Assert.Equal(151UL, hllSparse.Count());
            Assert.Equal(5009UL, hllDense.Count());
        }

        /// <summary>
        /// If this method fails, it's possible that the serialization format has changed and
        /// <see cref="CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [Fact]
        public void SerializerCanDeserializeVersion2Point1()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v2_1), Murmur3.GetHashCode);
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v2_1), Murmur3.GetHashCode);
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v2_1), Murmur3.GetHashCode);

            Assert.Equal(50UL, hllDirect.Count());
            Assert.Equal(50UL, hllDirect.CountAdditions);

            Assert.Equal(151UL, hllSparse.Count());
            Assert.Equal(150UL, hllSparse.CountAdditions);

            Assert.Equal(5009UL, hllDense.Count());
            Assert.Equal(5000UL, hllDense.CountAdditions);
        }

        [Fact]
        public void TestSerializerMultipleCardinalityAndBitsCombinations()
        {
            for (int bits = 4; bits <= 16; bits++)
            {
                for (int cardinality = 1; cardinality <= 1000; cardinality++)
                {
                    var estimator = CreateAndFillCardinalityEstimator(cardinality, bits);
                    CardinalityEstimatorSerializer serializer = new CardinalityEstimatorSerializer();
                    using (var stream = new MemoryStream())
                    {
                        serializer.Serialize(stream, estimator, true);
                        stream.Seek(0, SeekOrigin.Begin);
                        var deserializedEstimator = serializer.Deserialize(stream, Murmur3.GetHashCode);
                        Assert.True(deserializedEstimator.Count() == estimator.Count(), "Estimators should have same count before and after serialization");
                    }
                }
            }
        }

        private CardinalityEstimator CreateAndFillCardinalityEstimator(int cardinality = 1000000, int bits = 14)
        {
            var hll = new CardinalityEstimator(Murmur3.GetHashCode, b: bits);

            var nextMember = new byte[ElementSizeInBytes];
            for (var i = 0; i < cardinality; i++)
            {
                Rand.NextBytes(nextMember);
                hll.Add(nextMember);
            }

            return hll;
        }

        private int TestSerializerCreatesSmallerData(int cardinality)
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(cardinality);

            var customSerializer = new CardinalityEstimatorSerializer();

            byte[] customSerializerResults;
            using (var memoryStream = new MemoryStream())
            {
                customSerializer.Serialize(memoryStream, hll, false);
                customSerializerResults = memoryStream.ToArray();
                return customSerializerResults.Length;
            }
        }

        private void TestDeserializerWithCardinality(int cardinality)
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(cardinality);
            CardinalityEstimator hll2;

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll, false);

                results = memoryStream.ToArray();
            }

            using (var memoryStream = new MemoryStream(results))
            {
                hll2 = serializer.Deserialize(memoryStream, Murmur3.GetHashCode, false);
            }

            CompareHLL(hll, hll2);
        }

        private void TestDeserializer2WithCardinality(int cardinality)
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(cardinality);
            CardinalityEstimator hll2;

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                using (var bw = new BinaryWriter(memoryStream))
                {
                    serializer.Write(bw, hll);
                }

                results = memoryStream.ToArray();
            }

            using (var memoryStream = new MemoryStream(results))
            using (var br = new BinaryReader(memoryStream))
            {
                hll2 = serializer.Read(br, Murmur3.GetHashCode);
            }

            CompareHLL(hll, hll2);
        }

        private void TestSerializerCardinality100000Parameterized(bool useBinWriter)
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(100000);

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                if (useBinWriter)
                {
                    using (var bw = new BinaryWriter(memoryStream))
                    {
                        serializer.Write(bw, hll);
                    }
                }
                else
                {
                    serializer.Serialize(memoryStream, hll, false);
                }

                results = memoryStream.ToArray();
            }

            CardinalityEstimatorState data = hll.GetState();

            // Expected length is:
            // 4 bytes for the major and minor versions
            // 4 bytes for the Bits in Index 
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in lookupDense
            // 1 bytes for each element (ulong) in lookupDense
            // 8 bytes for CountAdded
            Assert.Equal(21 + data.LookupDense.Length, results.Length);

            Assert.Equal(14, BitConverter.ToInt32(results[4..8])); // Bits in Index = 14
            Assert.Equal(0, results[8]); // IsSparse = false AND IsDirectCount = false
            Assert.Equal(data.LookupDense.Length, BitConverter.ToInt32(results[9..13]));
            Assert.Equal(100000UL, BitConverter.ToUInt64(results[(13 + data.LookupDense.Length) ..])); // CountAdditions = 100000
        }

        private void CompareHLL(CardinalityEstimator hll1, CardinalityEstimator hll2)
        {
            CardinalityEstimatorState data = hll1.GetState();
            CardinalityEstimatorState data2 = hll2.GetState();

            Assert.Equal(data.BitsPerIndex, data2.BitsPerIndex);
            Assert.Equal(data.IsSparse, data2.IsSparse);

            Assert.True((data.DirectCount != null && data2.DirectCount != null) || (data.DirectCount == null && data2.DirectCount == null));
            Assert.True((data.LookupSparse != null && data2.LookupSparse != null) ||
                          (data.LookupSparse == null && data2.LookupSparse == null));
            Assert.True((data.LookupDense != null && data2.LookupDense != null) || (data.LookupDense == null && data2.LookupDense == null));

            if (data.DirectCount != null)
            {
                // DirectCount are subsets of each-other => they are the same set
                Assert.True(data.DirectCount.IsSubsetOf(data2.DirectCount) && data2.DirectCount.IsSubsetOf(data.DirectCount));
            }
            if (data.LookupSparse != null)
            {
                Assert.True(data.LookupSparse.DictionaryEqual(data2.LookupSparse));
            }
            if (data.LookupDense != null)
            {
                Assert.True(data.LookupDense.SequenceEqual(data2.LookupDense));
            }
        }

        // Header for current format (major=3, minor=1) with the given bitsPerIndex and flags.
        private static void WriteHeader(BinaryWriter bw, int bitsPerIndex, byte flags)
        {
            bw.Write((ushort)CardinalityEstimatorSerializer.DataFormatMajorVersion);
            bw.Write((ushort)CardinalityEstimatorSerializer.DataFormatMinorVersion);
            bw.Write(bitsPerIndex);
            bw.Write(flags);
        }

        [Theory]
        [InlineData(3)]    // below minimum
        [InlineData(17)]   // above maximum
        [InlineData(30)]   // attacker value: m = 2^30 ~ 1 GB
        [InlineData(-1)]   // negative
        public void Deserialize_RejectsOutOfRangeBitsPerIndex(int bitsPerIndex)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(bw, bitsPerIndex, flags: 0);
            }
            ms.Position = 0;

            var serializer = new CardinalityEstimatorSerializer();
            Assert.Throws<InvalidDataException>(() => serializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_RejectsOversizedDirectCount()
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(bw, bitsPerIndex: 14, flags: 1); // isDirectCount=true
                bw.Write(int.MaxValue);                       // attacker-supplied count
            }
            ms.Position = 0;

            var serializer = new CardinalityEstimatorSerializer();
            Assert.Throws<InvalidDataException>(() => serializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_RejectsNegativeDirectCount()
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(bw, bitsPerIndex: 14, flags: 1); // isDirectCount=true
                bw.Write(-1);
            }
            ms.Position = 0;

            var serializer = new CardinalityEstimatorSerializer();
            Assert.Throws<InvalidDataException>(() => serializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_RejectsOversizedSparseCount()
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(bw, bitsPerIndex: 14, flags: 2); // isSparse=true
                bw.Write(int.MaxValue);                       // attacker-supplied count
            }
            ms.Position = 0;

            var serializer = new CardinalityEstimatorSerializer();
            Assert.Throws<InvalidDataException>(() => serializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_RejectsOversizedDenseCount()
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(bw, bitsPerIndex: 14, flags: 0); // dense
                bw.Write(int.MaxValue);                       // attacker-supplied count != m
            }
            ms.Position = 0;

            var serializer = new CardinalityEstimatorSerializer();
            Assert.Throws<InvalidDataException>(() => serializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_RejectsDenseCountThatDoesNotMatchM()
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(bw, bitsPerIndex: 14, flags: 0); // dense, m = 16384
                bw.Write(16383);                               // off by one
            }
            ms.Position = 0;

            var serializer = new CardinalityEstimatorSerializer();
            Assert.Throws<InvalidDataException>(() => serializer.Deserialize(ms));
        }

        // ---------------------------------------------------------------------
        // Coverage-gap tests: exercise previously-uncovered serializer paths
        // (null-arg validation, future-major-version, truncated dense payload,
        // legacy v2 deserialize with hashFunction == null).
        // ---------------------------------------------------------------------

        private static readonly CardinalityEstimatorSerializer CoverageSerializer = new CardinalityEstimatorSerializer();

        [Fact]
        public void Serialize_OneArgOverload_WritesData()
        {
            // The two-arg Serialize(stream, est) wrapper just delegates to the three-arg form
            // with leaveOpen: false, so we capture the bytes via ToArray before disposal.
            var hll = new CardinalityEstimator();
            hll.Add("a");
            var ms = new MemoryStream();
            CoverageSerializer.Serialize(ms, hll);
            Assert.True(ms.ToArray().Length > 0);
        }

        [Fact]
        public void Serialize_NullStream_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => CoverageSerializer.Serialize(null, hll, false));
        }

        [Fact]
        public void Serialize_NullEstimator_Throws()
        {
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentNullException>(() => CoverageSerializer.Serialize(ms, null, false));
        }

        [Fact]
        public void Write_NullWriter_Throws()
        {
            var hll = new CardinalityEstimator();
            Assert.Throws<ArgumentNullException>(() => CoverageSerializer.Write(null, hll));
        }

        [Fact]
        public void Write_NullEstimator_Throws()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            Assert.Throws<ArgumentNullException>(() => CoverageSerializer.Write(bw, null));
        }

        [Fact]
        public void Deserialize_NullStream_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CoverageSerializer.Deserialize(null));
        }

        [Fact]
        public void Read_NullReader_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CoverageSerializer.Read(null));
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

            Assert.Throws<SerializationException>(() => CoverageSerializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_TruncatedDenseLookup_ThrowsEndOfStream()
        {
            // bitsPerIndex=4 -> m=16. We declare a dense lookup of 16 bytes but only write 4.
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((ushort)CardinalityEstimatorSerializer.DataFormatMajorVersion);
                bw.Write((ushort)CardinalityEstimatorSerializer.DataFormatMinorVersion);
                bw.Write(4);          // bitsPerIndex -> m = 16
                bw.Write((byte)0);    // flags: dense (not sparse, not direct)
                bw.Write(16);         // count == m (passes the size check)
                bw.Write(new byte[] { 1, 2, 3, 4 }); // truncated: only 4 of the 16 bytes
            }
            ms.Position = 0;

            Assert.Throws<EndOfStreamException>(() => CoverageSerializer.Deserialize(ms));
        }

        [Fact]
        public void Deserialize_LegacyV2WithNullHashFunction_PicksHashByEmbeddedId()
        {
            // v2.x format embeds the hash-function id in the stream. When the caller doesn't pass
            // a hashFunction override, the deserializer must pick one based on that embedded id
            // (1 -> Murmur3, anything else -> FNV-1a).
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

            // No hashFunction passed - exercises the v2 branch that picks based on the embedded id.
            var hll = CoverageSerializer.Deserialize(ms);
            Assert.Equal(0UL, hll.Count());
        }
    }
}
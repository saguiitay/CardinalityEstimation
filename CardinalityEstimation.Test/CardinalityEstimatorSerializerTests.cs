// /*  
//     See https://github.com/Microsoft/CardinalityEstimation.
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
    using System.Runtime.Serialization.Formatters.Binary;
    using CardinalityEstimation.Hash;
    using Xunit;

    
    public class CardinalityEstimatorSerializerTests : IDisposable
    {
        private const int ElementSizeInBytes = 20;
        public static readonly Random Rand = new Random();

        private Stopwatch stopwatch;

        public CardinalityEstimatorSerializerTests()
        {
            this.stopwatch = new Stopwatch();
            this.stopwatch.Start();
        }

        public void Dispose()
        {
            this.stopwatch.Stop();
            Console.WriteLine("Total test time: {0}", this.stopwatch.Elapsed);
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

            // Expected length is 102:
            // 4 bytes for the major and minor versions
            // 1 byte for the HashFunctionId
            // 4 bytes for the Bits in Index
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in DirectCount
            // 8 bytes for each element (ulong) in DirectCount
            // 8 bytes for CountAdded
            Assert.Equal(102, results.Length);

            Assert.Equal((byte)HashFunctionId.Murmur3, results.Skip(4).Take(1).First());
            Assert.Equal(14, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.Equal(3, results[9]); // IsSparse = true AND IsDirectCount = true
            Assert.Equal(10, BitConverter.ToInt32(results.Skip(10).Take(4).ToArray(), 0)); // Count = 10
            Assert.Equal(10UL, BitConverter.ToUInt64(results.Skip(94).Take(8).ToArray(), 0)); // CountAdditions = 10
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
            // 1 byte for the HashFunctionId
            // 4 bytes for the Bits in Index 
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in lookupSparse
            // 2+1 bytes for each element (ulong) in lookupSparse
            // 8 bytes for CountAdded
            Assert.Equal(22 + 3*data.LookupSparse.Count, results.Length);

            Assert.Equal((byte)HashFunctionId.Murmur3, results.Skip(4).Take(1).First());
            Assert.Equal(14, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.Equal(2, results[9]); // IsSparse = true AND IsDirectCount = false
            Assert.Equal(data.LookupSparse.Count, BitConverter.ToInt32(results.Skip(10).Take(4).ToArray(), 0));
            Assert.Equal(1000UL, BitConverter.ToUInt64(results.Skip(14 + 3*data.LookupSparse.Count).Take(8).ToArray(), 0)); // CountAdditions = 1000
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
                long customTotalSize = 0, defaultTotalSize = 0;
                var runs = 10;
                for (var i = 0; i < runs; i++)
                {
                    int customSize;
                    int defaultSize;
                    TestSerializerCreatesSmallerData(cardinality, out customSize, out defaultSize);

                    customTotalSize += customSize;
                    defaultTotalSize += defaultSize;
                }

                long customAverageSize = customTotalSize/runs, defaultAverageSize = defaultTotalSize/runs;

                Console.WriteLine("{0} | {1} | {2} | {3:P}", cardinality, customAverageSize, defaultAverageSize,
                    1 - ((float) customAverageSize/defaultAverageSize));
            }
        }

        /// <summary>
        ///     If this method fails, it's possible that the serialization format has changed and
        ///     <see cref="CardinalityEstimation.CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [Fact]
        public void SerializerCanDeserializeVersion1Point0()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v1_0));
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v1_0));
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v1_0));

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

            foreach (HashFunctionId hashFunctionId in Enum.GetValues(typeof(HashFunctionId)))
            {
                // Add elements to an estimator using the given hashFunctionId
                CardinalityEstimator original = new CardinalityEstimator(hashFunctionId: hashFunctionId);
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
                    deserialized = serializer.Deserialize(memoryStream, false);
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
        ///     If this method fails, it's possible that the serialization format has changed and
        ///     <see cref="CardinalityEstimation.CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [Fact]
        public void SerializerCanDeserializeVersion2Point0()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v2_0));
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v2_0));
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v2_0));

            Assert.Equal(50UL, hllDirect.Count());
            Assert.Equal(151UL, hllSparse.Count());
            Assert.Equal(5009UL, hllDense.Count());
        }

        /// <summary>
        ///     If this method fails, it's possible that the serialization format has changed and
        ///     <see cref="CardinalityEstimation.CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [Fact]
        public void SerializerCanDeserializeVersion2Point1()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v2_1));
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v2_1));
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v2_1));

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
                        var deserializedEstimator = serializer.Deserialize(stream);
                        Assert.True(deserializedEstimator.Count() == estimator.Count(), "Estimators should have same count before and after serialization");
                    }
                }
            }
        }

        private CardinalityEstimator CreateAndFillCardinalityEstimator(int cardinality = 1000000, int bits = 14)
        {
            var hll = new CardinalityEstimator(bits);

            var nextMember = new byte[ElementSizeInBytes];
            for (var i = 0; i < cardinality; i++)
            {
                Rand.NextBytes(nextMember);
                hll.Add(nextMember);
            }

            return hll;
        }

        private void TestSerializerCreatesSmallerData(int cardinality, out int customSize, out int defaultSize)
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(cardinality);

            var customSerializer = new CardinalityEstimatorSerializer();

            byte[] customSerializerResults;
            using (var memoryStream = new MemoryStream())
            {
                customSerializer.Serialize(memoryStream, hll, false);
                customSerializerResults = memoryStream.ToArray();
                customSize = customSerializerResults.Length;
            }

            var binaryFormatter = new BinaryFormatter();

            byte[] defaultSerializerResults;
            using (var memoryStream = new MemoryStream())
            {
                binaryFormatter.Serialize(memoryStream, hll);
                defaultSerializerResults = memoryStream.ToArray();
                defaultSize = defaultSerializerResults.Length;
            }

            Assert.True(customSerializerResults.Length <= defaultSerializerResults.Length);
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
                hll2 = serializer.Deserialize(memoryStream, false);
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
            {
                using (var br = new BinaryReader(memoryStream))
                {
                    hll2 = serializer.Read(br);
                }
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
            // 1 byte for the HashFunctionId
            // 4 bytes for the Bits in Index 
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in lookupDense
            // 1 bytes for each element (ulong) in lookupDense
            // 8 bytes for CountAdded
            Assert.Equal(22 + data.LookupDense.Length, results.Length);

            Assert.Equal((byte)HashFunctionId.Murmur3, results.Skip(4).Take(1).First());
            Assert.Equal(14, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.Equal(0, results[9]); // IsSparse = false AND IsDirectCount = false
            Assert.Equal(data.LookupDense.Length, BitConverter.ToInt32(results.Skip(10).Take(4).ToArray(), 0));
            Assert.Equal(100000UL, BitConverter.ToUInt64(results.Skip(14 + data.LookupDense.Length).Take(8).ToArray(), 0)); // CountAdditions = 100000
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
    }
}
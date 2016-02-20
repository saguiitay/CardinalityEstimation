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
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class CardinalityEstimatorSerializerTests
    {
        private const int ElementSizeInBytes = 20;
        public static readonly Random Rand = new Random();

        private Stopwatch stopwatch;

        [TestInitialize]
        public void Init()
        {
            this.stopwatch = new Stopwatch();
            this.stopwatch.Start();
        }

        [TestCleanup]
        public void Cleanup()
        {
            this.stopwatch.Stop();
            Console.WriteLine("Total test time: {0}", this.stopwatch.Elapsed);
        }

        [TestMethod]
        public void TestSerializerCardinality10()
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(10);

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll);

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
            Assert.AreEqual(102, results.Length);

            Assert.AreEqual((byte)HashFunctionId.Murmur3, results.Skip(4).Take(1).First());
            Assert.AreEqual(14, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.AreEqual(3, results[9]); // IsSparse = true AND IsDirectCount = true
            Assert.AreEqual(10, BitConverter.ToInt32(results.Skip(10).Take(4).ToArray(), 0)); // Count = 10
            Assert.AreEqual(10UL, BitConverter.ToUInt64(results.Skip(94).Take(8).ToArray(), 0)); // CountAdditions = 10
        }

        [TestMethod]
        public void TestSerializerCardinality1000()
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(1000);

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll);

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
            Assert.AreEqual(22 + 3*data.LookupSparse.Count, results.Length);

            Assert.AreEqual((byte)HashFunctionId.Murmur3, results.Skip(4).Take(1).First());
            Assert.AreEqual(14, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.AreEqual(2, results[9]); // IsSparse = true AND IsDirectCount = false
            Assert.AreEqual(data.LookupSparse.Count, BitConverter.ToInt32(results.Skip(10).Take(4).ToArray(), 0));
            Assert.AreEqual(1000UL, BitConverter.ToUInt64(results.Skip(14 + 3*data.LookupSparse.Count).Take(8).ToArray(), 0)); // CountAdditions = 1000
        }

        [TestMethod]
        public void TestSerializerCardinality100000()
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(100000);

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll);

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
            Assert.AreEqual(22 + data.LookupDense.Length, results.Length);

            Assert.AreEqual((byte)HashFunctionId.Murmur3, results.Skip(4).Take(1).First());
            Assert.AreEqual(14, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.AreEqual(0, results[9]); // IsSparse = false AND IsDirectCount = false
            Assert.AreEqual(data.LookupDense.Length, BitConverter.ToInt32(results.Skip(10).Take(4).ToArray(), 0));
            Assert.AreEqual(100000UL, BitConverter.ToUInt64(results.Skip(14 + data.LookupDense.Length).Take(8).ToArray(), 0)); // CountAdditions = 100000
        }


        [TestMethod]
        public void TestSerializer()
        {
            for (var i = 0; i < 100; i++)
            {
                TestSerializerCardinality10();
                TestSerializerCardinality1000();
                TestSerializerCardinality100000();
            }
        }

        [TestMethod]
        public void TestDeserializer()
        {
            for (var i = 0; i < 100; i++)
            {
                TestDeserializer(10);
                TestDeserializer(1000);
                TestDeserializer(100000);
            }
        }

        [TestMethod]
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
        [TestMethod]
        public void SerializerCanDeserializeVersion1Point0()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v1_0));
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v1_0));
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v1_0));

            Assert.AreEqual(50UL, hllDirect.Count());
            Assert.AreEqual(151UL, hllSparse.Count());
            Assert.AreEqual(5005UL, hllDense.Count());
        }

        [TestMethod]
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
                    serializer.Serialize(memoryStream, original);
                    results = memoryStream.ToArray();
                }

                // Deserialize
                CardinalityEstimator deserialized;
                using (var memoryStream = new MemoryStream(results))
                {
                    deserialized = serializer.Deserialize(memoryStream);
                }

                // Add the elements again, should have no effect on state
                foreach (int element in elements)
                {
                    deserialized.Add(element);
                }

                Assert.AreEqual(original.Count(), deserialized.Count());
            }
        }

        /// <summary>
        ///     If this method fails, it's possible that the serialization format has changed and
        ///     <see cref="CardinalityEstimation.CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [TestMethod]
        public void SerializerCanDeserializeVersion2Point0()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v2_0));
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v2_0));
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v2_0));

            Assert.AreEqual(50UL, hllDirect.Count());
            Assert.AreEqual(151UL, hllSparse.Count());
            Assert.AreEqual(5009UL, hllDense.Count());
        }

        /// <summary>
        ///     If this method fails, it's possible that the serialization format has changed and
        ///     <see cref="CardinalityEstimation.CardinalityEstimatorSerializer.DataFormatMajorVersion" /> should be incremented.
        /// </summary>
        [TestMethod]
        public void SerializerCanDeserializeVersion2Point1()
        {
            var serializer = new CardinalityEstimatorSerializer();

            CardinalityEstimator hllDirect = serializer.Deserialize(new MemoryStream(Resources.serializedDirect_v2_1));
            CardinalityEstimator hllSparse = serializer.Deserialize(new MemoryStream(Resources.serializedSparse_v2_1));
            CardinalityEstimator hllDense = serializer.Deserialize(new MemoryStream(Resources.serializedDense_v2_1));

            Assert.AreEqual(50UL, hllDirect.Count());
            Assert.AreEqual(50UL, hllDirect.CountAdditions);

            Assert.AreEqual(151UL, hllSparse.Count());
            Assert.AreEqual(150UL, hllSparse.CountAdditions);

            Assert.AreEqual(5009UL, hllDense.Count());
            Assert.AreEqual(5000UL, hllDense.CountAdditions);
        }

        private CardinalityEstimator CreateAndFillCardinalityEstimator(int cardinality = 1000000)
        {
            var hll = new CardinalityEstimator();

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
                customSerializer.Serialize(memoryStream, hll);
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

            Assert.IsTrue(customSerializerResults.Length <= defaultSerializerResults.Length);
        }

        private void TestDeserializer(int cardinality)
        {
            CardinalityEstimator hll = CreateAndFillCardinalityEstimator(cardinality);
            CardinalityEstimator hll2;

            var serializer = new CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll);

                results = memoryStream.ToArray();
            }

            using (var memoryStream = new MemoryStream(results))
            {
                hll2 = serializer.Deserialize(memoryStream);
            }

            CardinalityEstimatorState data = hll.GetState();
            CardinalityEstimatorState data2 = hll2.GetState();

            Assert.AreEqual(data.BitsPerIndex, data2.BitsPerIndex);
            Assert.AreEqual(data.IsSparse, data2.IsSparse);

            Assert.IsTrue((data.DirectCount != null && data2.DirectCount != null) || (data.DirectCount == null && data2.DirectCount == null));
            Assert.IsTrue((data.LookupSparse != null && data2.LookupSparse != null) ||
                          (data.LookupSparse == null && data2.LookupSparse == null));
            Assert.IsTrue((data.LookupDense != null && data2.LookupDense != null) || (data.LookupDense == null && data2.LookupDense == null));

            if (data.DirectCount != null)
            {
                // DirectCount are subsets of each-other => they are the same set
                Assert.IsTrue(data.DirectCount.IsSubsetOf(data2.DirectCount) && data2.DirectCount.IsSubsetOf(data.DirectCount));
            }
            if (data.LookupSparse != null)
            {
                Assert.IsTrue(data.LookupSparse.DictionaryEqual(data2.LookupSparse));
            }
            if (data.LookupDense != null)
            {
                Assert.IsTrue(data.LookupDense.SequenceEqual(data2.LookupDense));
            }
        }
    }
}
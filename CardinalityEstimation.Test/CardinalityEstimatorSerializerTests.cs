/*  
    See https://github.com/Microsoft/CardinalityEstimation.
    The MIT License (MIT)

    Copyright (c) 2015 Microsoft

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;

namespace CardinalityEstimation.Test
{
    using System;
    using System.Diagnostics;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class CardinalityEstimatorSerializer
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

        private void TestSerializerCreatesSmallerData(int cardinality, out int customSize, out int defaultSize)
        {
            var hll = CreationCardinalityEstimator(cardinality);

            var customSerializer = new CardinalityEstimation.CardinalityEstimatorSerializer();

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

        [TestMethod]
        public void TestSerializerCardinality10()
        {
            var hll = CreationCardinalityEstimator(10);

            var serializer = new CardinalityEstimation.CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll);

                results = memoryStream.ToArray();
            }


            // Expected length is 89:
            // 4 bytes for the Bits in Index
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in DirectCount
            // 8 bytes for each element (ulong) in DirectCount
            Assert.AreEqual(89, results.Length);


            Assert.AreEqual(14, BitConverter.ToInt32(results.Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.AreEqual(3, results[4]); // IsSparse = true AND IsDirectCount = true
            Assert.AreEqual(10, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0)); // Count = 10
        }

        [TestMethod]
        public void TestSerializerCardinality1000()
        {
            var hll = CreationCardinalityEstimator(1000);

            var serializer = new CardinalityEstimation.CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll);

                results = memoryStream.ToArray();
            }

            var data = hll.GetData();

            // Expected length is 2904:
            // 4 bytes for the Bits in Index 
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in lookupSparse
            // 2+1 bytes for each element (ulong) in lookupSparse
            Assert.AreEqual(9 + 3*data.lookupSparse.Count, results.Length);


            Assert.AreEqual(14, BitConverter.ToInt32(results.Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.AreEqual(2, results[4]); // IsSparse = true AND IsDirectCount = false
            Assert.AreEqual(data.lookupSparse.Count, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0));
        }

        [TestMethod]
        public void TestSerializerCardinality100000()
        {
            var hll = CreationCardinalityEstimator(100000);

            var serializer = new CardinalityEstimation.CardinalityEstimatorSerializer();

            byte[] results;
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(memoryStream, hll);

                results = memoryStream.ToArray();
            }

            var data = hll.GetData();

            // Expected length is 2904:
            // 4 bytes for the Bits in Index 
            // 1 byte for the IsSparse and IsDirectCount flags
            // 4 bytes for the number of elements in lookupDense
            // 1 bytes for each element (ulong) in lookupDense
            Assert.AreEqual(9 + data.lookupDense.Length, results.Length);


            Assert.AreEqual(14, BitConverter.ToInt32(results.Take(4).ToArray(), 0)); // Bits in Index = 14
            Assert.AreEqual(0, results[4]); // IsSparse = false AND IsDirectCount = false
            Assert.AreEqual(data.lookupDense.Length, BitConverter.ToInt32(results.Skip(5).Take(4).ToArray(), 0));
        }

        private void TestDeserializer(int cardinality)
        {
            var hll = CreationCardinalityEstimator(cardinality);
            CardinalityEstimator hll2;

            var serializer = new CardinalityEstimation.CardinalityEstimatorSerializer();

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

            var data = hll.GetData();
            var data2 = hll2.GetData();

            Assert.AreEqual(data.bitsPerIndex, data2.bitsPerIndex);
            Assert.AreEqual(data.isSparse, data2.isSparse);

            Assert.IsTrue((data.directCount != null && data2.directCount != null) || (data.directCount == null && data2.directCount == null));
            Assert.IsTrue((data.lookupSparse != null && data2.lookupSparse != null) || (data.lookupSparse == null && data2.lookupSparse == null));
            Assert.IsTrue((data.lookupDense != null && data2.lookupDense != null) || (data.lookupDense == null && data2.lookupDense == null));

            if (data.directCount != null)
            {
                // DirectCount are subsets of each-other => they are the same set
                Assert.IsTrue(data.directCount.IsSubsetOf(data2.directCount) && data2.directCount.IsSubsetOf(data.directCount));
            }
            if (data.lookupSparse != null)
            {
                Assert.IsTrue(data.lookupSparse.DictionaryEqual(data2.lookupSparse));
            }
            if (data.lookupDense != null)
            {
                Assert.IsTrue(data.lookupDense.SequenceEqual(data2.lookupDense));
            }
        }


        [TestMethod]
        public void TestSerializer()
        {
            for (int i = 0; i < 100; i++)
            {
                TestSerializerCardinality10();
                TestSerializerCardinality1000();
                TestSerializerCardinality100000();
            }
        }

        [TestMethod]
        public void TestDeserializer()
        {
            for (int i = 0; i < 100; i++)
            {
                TestDeserializer(10);
                TestDeserializer(1000);
                TestDeserializer(100000);
            }
        }

        [TestMethod]
        public void TestSerializerSizes()
        {
            for (int cardinality = 1; cardinality < 10240; cardinality *= 2)
            {
                long customTotalSize = 0, defaultTotalSize = 0;
                int runs = 10;
                for (int i = 0; i < runs; i++)
                {
                    int customSize;
                    int defaultSize;
                    TestSerializerCreatesSmallerData(cardinality, out customSize, out defaultSize);

                    customTotalSize += customSize;
                    defaultTotalSize += defaultSize;
                }

                long customAverageSize = customTotalSize / runs, defaultAverageSize = defaultTotalSize / runs;

                Console.WriteLine("{0} | {1} | {2} | {3:P}", 
                    cardinality, customAverageSize, defaultAverageSize,
                    1-((float)customAverageSize / defaultAverageSize));

            }
        }

        
        private CardinalityEstimator CreationCardinalityEstimator(int cardinality = 1000000)
        {
            var hll = new CardinalityEstimator();

            var nextMember = new byte[ElementSizeInBytes];
            for (int i = 0; i < cardinality; i++)
            {
                Rand.NextBytes(nextMember);
                hll.Add(nextMember);
            }

            return hll;
        }
    }
}
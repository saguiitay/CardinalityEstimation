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

namespace CardinalityEstimation.Test.Hash
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using CardinalityEstimation.Hash;
    using Xunit;

    
    public class HashFunctionFactoryTests
    {
        [Fact]
        public void FactoryCanProduceAllHashFunctionTypes()
        {
            // Make sure factory can produce each HashFunctionId
            foreach (HashFunctionId hashFunctionId in Enum.GetValues(typeof (HashFunctionId)))
            {
                IHashFunction hashFunction = HashFunctionFactory.GetHashFunction(hashFunctionId);
                Assert.True(hashFunction != null, "Factory created a null hash function with ID" + hashFunctionId);
            }
        }

        [Fact]
        public void EachImplmentationHasUniqueId()
        {
            Array hashFunctionIds = Enum.GetValues(typeof (HashFunctionId));
            // Discover and count all implementations of IHashFunction
            int hashFunctionTypesCount =
                typeof (IHashFunction).Assembly.GetTypes().Count(t => typeof (IHashFunction).IsAssignableFrom(t) && t.IsClass);

            Assert.True(hashFunctionIds.Length == hashFunctionTypesCount,
                "Number of IHashFunction implementations must match number of HashFunctionIds");

            // Make sure the IDs are unique
            ISet<HashFunctionId> knownIds = new HashSet<HashFunctionId>();
            foreach (HashFunctionId hashFunctionId in hashFunctionIds)
            {
                IHashFunction hashFunction = HashFunctionFactory.GetHashFunction(hashFunctionId);
                if (knownIds.Contains(hashFunction.HashFunctionId))
                {
                    Assert.True(false, "Hash function ID " + hashFunction.HashFunctionId + " has more than one implementation!");
                }
                knownIds.Add(hashFunction.HashFunctionId);
            }
        }
    }
}
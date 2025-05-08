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
    using CardinalityEstimation.Hash;
    using Xunit;

    public class InterfaceImplementationTests
    {
        private const int DefaultPrecision = 14;

        [Fact]
        public void AddString_HandlesEmptyAndNullStrings()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);
            
            // Empty string should be counted
            bool changed = estimator.Add(string.Empty);
            Assert.True(changed);
            Assert.Equal(1UL, estimator.Count());
            
            // Adding empty string again should not change state
            changed = estimator.Add(string.Empty);
            Assert.False(changed);
            Assert.Equal(1UL, estimator.Count());
            
            // Null string should be handled as a distinct value
            changed = estimator.Add((string)null);
            Assert.True(changed);
            Assert.Equal(2UL, estimator.Count());
        }

        [Fact]
        public void AddBytes_HandlesEmptyAndNullArrays()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);
            
            // Empty array should be counted
            bool changed = estimator.Add(new byte[0]);
            Assert.True(changed);
            Assert.Equal(1UL, estimator.Count());
            
            // Adding empty array again should not change state
            changed = estimator.Add(new byte[0]);
            Assert.False(changed);
            Assert.Equal(1UL, estimator.Count());
            
            // Null array should be handled as a distinct value
            changed = estimator.Add((byte[])null);
            Assert.True(changed);
            Assert.Equal(2UL, estimator.Count());
        }

        [Fact]
        public void AddNumeric_HandlesSpecialValues()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);
            
            // Test double special values
            estimator.Add(double.NaN);
            estimator.Add(double.PositiveInfinity);
            estimator.Add(double.NegativeInfinity);
            estimator.Add(double.MaxValue);
            estimator.Add(double.MinValue);
            estimator.Add(0.0);
            estimator.Add(-0.0);
            
            // Test float special values
            estimator.Add(float.NaN);
            estimator.Add(float.PositiveInfinity);
            estimator.Add(float.NegativeInfinity);
            estimator.Add(float.MaxValue);
            estimator.Add(float.MinValue);
            estimator.Add(0.0f);
            estimator.Add(-0.0f);

            // Test integer boundaries
            estimator.Add(int.MaxValue);
            estimator.Add(int.MinValue);
            estimator.Add((uint)0);
            estimator.Add(uint.MaxValue);
            estimator.Add(long.MaxValue);
            estimator.Add(long.MinValue);
            estimator.Add((ulong)0);
            estimator.Add(ulong.MaxValue);

            // All special values should be counted as distinct
            Assert.True(estimator.Count() >= 16);
        }

        [Fact]
        public void InterfaceMethodsWorkIdentically()
        {
            ICardinalityEstimator<string> stringEstimator = new CardinalityEstimator(b: DefaultPrecision);
            ICardinalityEstimator<int> intEstimator = new CardinalityEstimator(b: DefaultPrecision);
            ICardinalityEstimator<byte[]> bytesEstimator = new CardinalityEstimator(b: DefaultPrecision);

            // Add same number of distinct items to each
            for (int i = 0; i < 1000; i++)
            {
                stringEstimator.Add(i.ToString());
                intEstimator.Add(i);
                bytesEstimator.Add(BitConverter.GetBytes(i));
            }

            // Counts should be similar (not necessarily identical due to hash collisions)
            double stringCount = stringEstimator.Count();
            double intCount = intEstimator.Count();
            double bytesCount = bytesEstimator.Count();

            Assert.True(Math.Abs(stringCount - 1000) / 1000 < 0.1);
            Assert.True(Math.Abs(intCount - 1000) / 1000 < 0.1);
            Assert.True(Math.Abs(bytesCount - 1000) / 1000 < 0.1);

            // CountAdditions should be exact
            Assert.Equal(1000UL, stringEstimator.CountAdditions);
            Assert.Equal(1000UL, intEstimator.CountAdditions);
            Assert.Equal(1000UL, bytesEstimator.CountAdditions);
        }

        [Fact]
        public void DifferentTypesCanBeAddedToSameEstimator()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);

            // Add different types to same estimator
            estimator.Add("test");
            estimator.Add(42);
            estimator.Add(42.0);
            estimator.Add(42.0f);
            estimator.Add(42U);
            estimator.Add(42L);
            estimator.Add(42UL);
            estimator.Add(new byte[] { 42 });

            // Each should be counted as distinct due to different serialization
            Assert.True(estimator.Count() >= 4);
            Assert.Equal(8UL, estimator.CountAdditions);
        }

        [Fact]
        public void Add_ReturnsFalseForDuplicates()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);

            // First addition should return true
            Assert.True(estimator.Add("test"));
            Assert.True(estimator.Add(42));
            Assert.True(estimator.Add(new byte[] { 1, 2, 3 }));

            // Duplicate additions should return false
            Assert.False(estimator.Add("test"));
            Assert.False(estimator.Add(42));
            Assert.False(estimator.Add(new byte[] { 1, 2, 3 }));

            Assert.Equal(3UL, estimator.Count());
            Assert.Equal(6UL, estimator.CountAdditions);
        }
    }
}

namespace CardinalityEstimation.Test
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using CardinalityEstimation.Hash;
    using Xunit;

    public class StateTransitionTests
    {
        private const int DefaultPrecision = 14;

        [Fact]
        public void TransitionFromDirectToSparse()
        {
            var estimator = new CardinalityEstimator(b: DefaultPrecision);
            var state = estimator.GetState();
            
            // Initially should be using direct counting
            Assert.NotNull(state.DirectCount);
            Assert.True(state.IsSparse);
            Assert.NotNull(state.LookupSparse);
            Assert.Null(state.LookupDense);

            // Add elements up to DirectCounterMaxElements (100)
            for (int i = 0; i < 101; i++)
            {
                estimator.Add(i);
            }

            state = estimator.GetState();
            
            // Should now be using sparse representation
            Assert.Null(state.DirectCount);
            Assert.True(state.IsSparse);
            Assert.NotNull(state.LookupSparse);
            Assert.Null(state.LookupDense);
        }

        [Fact]
        public void TransitionFromSparseToDense()
        {
            var estimator = new CardinalityEstimator(b: 8); // Use smaller precision to trigger dense transition sooner
            var state = estimator.GetState();

            // Initially should be using sparse
            Assert.True(state.IsSparse);
            Assert.NotNull(state.LookupSparse);
            Assert.Null(state.LookupDense);

            // Add enough elements to force transition to dense
            for (int i = 0; i < 1000; i++) 
            {
                estimator.Add(i);
                state = estimator.GetState();
                
                if (!state.IsSparse)
                {
                    // Verify proper transition
                    Assert.Null(state.LookupSparse);
                    Assert.NotNull(state.LookupDense);
                    Assert.Equal(Math.Pow(2, 8), state.LookupDense.Length); // 2^8 for 8-bit precision
                    break;
                }
            }

            // Ensure transition occurred
            Assert.False(state.IsSparse);
        }

        [Theory]
        [InlineData(4)]  // Minimum precision
        [InlineData(16)] // Maximum precision
        public void DenseRepresentationSizeMatchesPrecision(int precision)
        {
            var estimator = new CardinalityEstimator(b: precision);
            
            // Add enough elements to force dense representation
            for (int i = 0; i < 10000; i++)
            {
                estimator.Add(i);
            }

            var state = estimator.GetState();
            Assert.False(state.IsSparse);
            Assert.NotNull(state.LookupDense);
            Assert.Equal(Math.Pow(2, precision), state.LookupDense.Length);
        }

        [Fact]
        public void MergingPreservesDenseState()
        {
            var sparseEstimator = new CardinalityEstimator(b: DefaultPrecision);
            var denseEstimator = new CardinalityEstimator(b: DefaultPrecision);

            // Make denseEstimator dense
            for (int i = 0; i < 10000; i++)
            {
                denseEstimator.Add(i);
            }

            // Add some elements to sparseEstimator
            for (int i = 0; i < 50; i++)
            {
                sparseEstimator.Add(i + 10000);
            }

            var denseState = denseEstimator.GetState();
            Assert.False(denseState.IsSparse);

            // Merge sparse into dense
            denseEstimator.Merge(sparseEstimator);
            denseState = denseEstimator.GetState();
            
            // Should still be dense
            Assert.False(denseState.IsSparse);
            Assert.NotNull(denseState.LookupDense);
            Assert.Null(denseState.LookupSparse);
        }

        [Fact]
        public void CustomHashFunction_IsUsed()
        {
            int hashCalls = 0;
            ulong CustomHash(byte[] data)
            {
                hashCalls++;
                // Ensure the byte array is at least 8 bytes long
                if (data.Length < 8)
                {
                    var padded = new byte[8];
                    Array.Copy(data, padded, data.Length);
                    return BitConverter.ToUInt64(padded, 0);
                }
                return BitConverter.ToUInt64(data, 0);
            }

            var estimator = new CardinalityEstimator(hashFunction: CustomHash, b: DefaultPrecision);
            estimator.Add(42);
            estimator.Add("test");
            estimator.Add(new byte[] { 1, 2, 3 });

            Assert.Equal(3, hashCalls);
        }
    }
}

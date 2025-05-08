using Xunit;

namespace CardinalityEstimation.Test
{
    public class GetSigmaTests
    {
        [Fact]
        public void GetSigma_ZeroInput_ReturnsMaxPlusOne()
        {
            // Zero input should return bitsToCount + 1
            byte result = CardinalityEstimator.GetSigma(0UL, 4);
            Assert.Equal(5, result);
        }

        [Fact]
        public void GetSigma_Example1_ThreeLeadingZeros()
        {
            // Binary: ...000001
            ulong hash = 0b000001UL;
            byte result = CardinalityEstimator.GetSigma(hash, 4);
            Assert.Equal(4, result);
        }

        [Fact]
        public void GetSigma_Example2_NoLeadingZeros()
        {
            // Binary: ...001000
            ulong hash = 0b001000UL;
            byte result = CardinalityEstimator.GetSigma(hash, 4);
            Assert.Equal(1, result);
        }

        [Fact]
        public void GetSigma_Example3_OneLeadingZero()
        {
            // Binary: ...000100
            ulong hash = 0b000100UL;
            byte result = CardinalityEstimator.GetSigma(hash, 4);
            Assert.Equal(2, result);
        }

        [Fact]
        public void GetSigma_AllOnes_ReturnsOne()
        {
            ulong hash = ulong.MaxValue; // All bits set to 1
            byte result = CardinalityEstimator.GetSigma(hash, 4);
            Assert.Equal(1, result);
        }

        [Fact]
        public void GetSigma_DifferentBitCounts()
        {
            // Test with different bitsToCount values
            ulong hash = 0b000001UL;
            Assert.Equal(4, CardinalityEstimator.GetSigma(hash, 4));
            Assert.Equal(5, CardinalityEstimator.GetSigma(hash, 5));
            Assert.Equal(6, CardinalityEstimator.GetSigma(hash, 6));
        }
    }
}

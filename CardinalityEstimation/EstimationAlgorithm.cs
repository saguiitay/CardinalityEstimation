namespace CardinalityEstimation
{
    /// <summary>Identifies the algorithm used to estimate cardinality.</summary>
    public enum EstimationAlgorithm
    {
        /// <summary>The original estimator, including its existing bias correction and exact-counting behavior.</summary>
        HyperLogLog = 0,

        /// <summary>HyperLogLog++ with higher-precision sparse registers and nearest-neighbor bias correction.</summary>
        HyperLogLogPlusPlus = 1,

        /// <summary>SuperLogLog with a corrected mean of the lowest 70 percent of registers.</summary>
        SuperLogLog = 2,
    }
}

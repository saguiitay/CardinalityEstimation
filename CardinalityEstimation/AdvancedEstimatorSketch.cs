namespace CardinalityEstimation
{
    using System;
    using System.Collections.Generic;

    /// <summary>Shared state and algorithms for the opt-in estimators; callers provide synchronization.</summary>
    [Serializable]
    internal sealed class AdvancedEstimatorSketch
    {
        internal const int SparsePrecision = 25;
        private readonly int bits;
        private readonly int m;
        private HashSet<ulong> directCount;
        private HllPlusPlusSparse sparse;
        private byte[] dense;

        // Normalization of 2^(trimmed mean), multiplied by m (not the retained register count).
        // Calibrated from independent Poisson registers R=ceil(log2(lambda/-log(U))):
        // seed=104729+b, log2(lambda)=20+U, max(2048,2^24/m) trials, floor(0.7*m) retained.
        private static readonly double[] SuperLogLogNormalization =
        {
            0.7278589776957998, 0.756586801642344, 0.7702546451474176,
            0.7682150106272748, 0.7670680893694566, 0.7686036608367246,
            0.7695104240658018, 0.7695535045285254, 0.7690327612393769,
            0.7695404857035653, 0.7696849276430244, 0.7696113402566721,
            0.7692956914461775,
        };
        private static readonly double[] SuperLogLogAsymptoticNormalization = BuildAsymptoticNormalization();

        internal EstimationAlgorithm Algorithm { get; }

        internal AdvancedEstimatorSketch(CardinalityEstimatorState state)
        {
            Algorithm = state.Algorithm;
            bits = state.BitsPerIndex;
            m = 1 << bits;
            if (state.DirectCount != null)
            {
                directCount = new HashSet<ulong>(state.DirectCount);
            }
            else if (Algorithm == EstimationAlgorithm.HyperLogLogPlusPlus && state.IsSparse)
            {
                sparse = new HllPlusPlusSparse(state.LookupSparseHighPrecision ?? Array.Empty<uint>());
            }
            else
            {
                dense = state.LookupDense != null ? (byte[])state.LookupDense.Clone() : new byte[m];
            }
        }

        internal bool Add(ulong hash)
        {
            if (directCount != null)
            {
                bool changed = directCount.Add(hash);
                if (directCount.Count <= HllConstants.DirectCounterMaxElements)
                    return changed;

                InitializeRegisters();
                foreach (ulong element in directCount)
                    AddRegister(element);
                directCount = null;
                return true;
            }
            return AddRegister(hash);
        }

        private void InitializeRegisters()
        {
            if (Algorithm == EstimationAlgorithm.HyperLogLogPlusPlus && SparseEntryLimit > 0)
                sparse = new HllPlusPlusSparse(Array.Empty<uint>());
            else
                dense = new byte[m];
        }

        private int SparseEntryLimit => Math.Max(0, (m - HllPlusPlusSparse.BufferBudgetBytes) / sizeof(uint));

        private bool AddRegister(ulong hash)
        {
            if (sparse != null)
            {
                uint index = (uint)(hash >> (64 - SparsePrecision));
                byte rank = CardinalityEstimator.GetSigma(hash, 64 - SparsePrecision);
                bool changed = sparse.Add(index, rank);
                if (sparse.Count > SparseEntryLimit)
                {
                    PromoteToDense();
                    return true;
                }
                return changed;
            }

            int bucket = (int)(hash >> (64 - bits));
            byte sigma = CardinalityEstimator.GetSigma(hash, (byte)(64 - bits));
            if (sigma <= dense[bucket])
                return false;
            dense[bucket] = sigma;
            return true;
        }

        private void PromoteToDense()
        {
            if (dense != null)
                return;
            dense = new byte[m];
            foreach (uint entry in sparse.GetEntries())
                FoldEntry(entry);
            sparse = null;
        }

        private void FoldEntry(uint entry)
        {
            uint index = entry >> 6;
            int extraBits = SparsePrecision - bits;
            int bucket = (int)(index >> extraBits);
            uint suffix = index & ((1U << extraBits) - 1);
            // The extra sparse index bits become the prefix of the dense rank's bit stream.
            byte rank = suffix == 0
                ? (byte)(extraBits + (entry & 63))
                : (byte)(System.Numerics.BitOperations.LeadingZeroCount(suffix) - (32 - extraBits) + 1);
            dense[bucket] = Math.Max(dense[bucket], rank);
        }

        internal ulong Count()
        {
            if (directCount != null)
                return (ulong)directCount.Count;
            if (sparse != null)
            {
                double buckets = 1 << SparsePrecision;
                return RoundEstimate(-buckets * Math.Log(1 - sparse.Count / buckets));
            }

            return Algorithm == EstimationAlgorithm.SuperLogLog ? CountSuperLogLog() : CountHyperLogLogPlusPlus();
        }

        private ulong CountHyperLogLogPlusPlus()
        {
            double inverseSum = 0;
            int zeros = 0;
            foreach (byte rank in dense)
            {
                inverseSum += HllConstants.InversePowersOfTwo[rank];
                if (rank == 0)
                    zeros++;
            }
            double estimate = HllConstants.GetAlphaM(m) * m * m / inverseSum;
            if (estimate <= 5.0 * m)
                estimate = BiasCorrection.CorrectBiasNearestNeighbors(estimate, bits);
            if (zeros > 0)
            {
                double linear = m * Math.Log((double)m / zeros);
                if (linear <= HllConstants.GetSubAlgorithmSelectionThreshold(bits))
                    return RoundEstimate(linear);
            }
            return RoundEstimate(estimate);
        }

        private ulong CountSuperLogLog()
        {
            Span<int> histogram = stackalloc int[HllConstants.InversePowersOfTwoLength];
            histogram.Clear();
            foreach (byte rank in dense)
                histogram[rank]++;

            if (histogram[0] > 0)
            {
                double linear = m * Math.Log((double)m / histogram[0]);
                if (linear <= 2.5 * m)
                    return RoundEstimate(linear);
            }

            int retained = 7 * m / 10;
            int remaining = retained;
            int sum = 0;
            for (int rank = 0; remaining > 0; rank++)
            {
                int take = Math.Min(remaining, histogram[rank]);
                sum += take * rank;
                remaining -= take;
            }
            double estimate = SuperLogLogNormalization[bits - 4] * m * Math.Pow(2, (double)sum / retained);
            if (bits >= 10)
            {
                // Integer ranks introduce a periodic bias that otherwise dominates error at high precision.
                double fraction = (double)retained / m;
                double mean = (double)sum / retained;
                double logLoad = Math.Log2(estimate / m);
                for (int i = 0; i < 8; i++)
                    logLoad += mean - ExpectedTrimmedMean(logLoad, fraction);
                estimate = m * Math.Pow(2, logLoad) * SuperLogLogNormalization[bits - 4] /
                    SuperLogLogAsymptoticNormalization[bits - 4];
            }
            return RoundEstimate(estimate);
        }

        private static double ExpectedTrimmedMean(double logLoad, double fraction)
        {
            double remaining = fraction, sum = 0, previous = 0;
            for (int rank = 0; remaining > 0; rank++)
            {
                double cumulative = Math.Exp(-Math.Pow(2, logLoad - rank));
                double take = Math.Min(remaining, cumulative - previous);
                sum += take * rank;
                remaining -= take;
                previous = cumulative;
            }
            return sum / fraction;
        }

        private static double[] BuildAsymptoticNormalization()
        {
            var result = new double[13];
            for (int bits = 4; bits <= 16; bits++)
            {
                int m = 1 << bits;
                double fraction = (double)(7 * m / 10) / m;
                double sum = 0;
                const int samples = 256;
                for (int i = 0; i < samples; i++)
                {
                    double logLoad = 20 + (i + 0.5) / samples;
                    sum += Math.Pow(2, ExpectedTrimmedMean(logLoad, fraction) - logLoad);
                }
                result[bits - 4] = samples / sum;
            }
            return result;
        }

        private static ulong RoundEstimate(double estimate)
        {
            // Estimates can exceed the public return type near the 64-bit hash-space limit.
            return estimate >= ulong.MaxValue ? ulong.MaxValue : (ulong)Math.Round(estimate);
        }

        internal void Merge(AdvancedEstimatorSketch other)
        {
            if (directCount != null && other.directCount != null)
            {
                foreach (ulong hash in other.directCount)
                {
                    if (directCount != null)
                        Add(hash);
                    else
                        AddRegister(hash);
                }
                return;
            }

            if (directCount != null)
            {
                InitializeRegisters();
                foreach (ulong hash in directCount)
                    AddRegister(hash);
                directCount = null;
            }
            if (other.directCount != null)
            {
                foreach (ulong hash in other.directCount)
                    AddRegister(hash);
            }
            else if (sparse != null && other.sparse != null)
            {
                foreach (uint entry in other.sparse.GetEntries())
                    sparse.Add(entry >> 6, (byte)(entry & 63));
                if (sparse.Count > SparseEntryLimit)
                    PromoteToDense();
            }
            else
            {
                PromoteToDense();
                if (other.sparse != null)
                {
                    foreach (uint entry in other.sparse.GetEntries())
                        FoldEntry(entry);
                }
                else
                {
                    for (int i = 0; i < m; i++)
                        dense[i] = Math.Max(dense[i], other.dense[i]);
                }
            }
        }

        internal CardinalityEstimatorState GetState(ulong countAdditions)
        {
            return new CardinalityEstimatorState
            {
                Algorithm = Algorithm,
                BitsPerIndex = bits,
                DirectCount = directCount != null ? new HashSet<ulong>(directCount) : null,
                IsSparse = Algorithm == EstimationAlgorithm.HyperLogLogPlusPlus && dense == null,
                LookupSparseHighPrecision = sparse?.GetEntries(),
                LookupDense = dense != null ? (byte[])dense.Clone() : null,
                CountAdditions = countAdditions,
            };
        }

        internal bool EqualsSketch(AdvancedEstimatorSketch other)
        {
            if ((directCount == null) != (other.directCount == null) ||
                (sparse == null) != (other.sparse == null) ||
                (dense == null) != (other.dense == null))
                return false;
            if (directCount != null)
                return directCount.SetEquals(other.directCount);
            if (dense != null)
                return dense.AsSpan().SequenceEqual(other.dense);
            return sparse.GetEntries().AsSpan().SequenceEqual(other.sparse.GetEntries());
        }
    }
}

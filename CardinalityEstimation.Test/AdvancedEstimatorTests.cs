namespace CardinalityEstimation.Test
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using CardinalityEstimation.Hash;
    using Xunit;
    using Xunit.Abstractions;

    public class AdvancedEstimatorTests
    {
        private readonly ITestOutputHelper output;
        private static readonly GetHashCodeDelegate IdentityHash = bytes => BinaryPrimitives.ReadUInt64LittleEndian(bytes);

        public AdvancedEstimatorTests(ITestOutputHelper output) => this.output = output;

        private static CardinalityEstimator Create(EstimationAlgorithm algorithm, int bits = 14, bool direct = false)
        {
            GetHashCodeSpanDelegate identity = bytes => BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return new CardinalityEstimator(identity, HllConstants.CreateEmptyState(bits, direct, algorithm));
        }

        private static ulong Mix(ulong value)
        {
            value += 0x9e3779b97f4a7c15UL;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }

        private static byte[] Serialize(CardinalityEstimator estimator)
        {
            using var stream = new MemoryStream();
            new CardinalityEstimatorSerializer().Serialize(stream, estimator, leaveOpen: true);
            return stream.ToArray();
        }

        [Fact]
        public void LegacyConstructorAndWireFormatRemainUnchanged()
        {
            Assert.NotNull(typeof(CardinalityEstimator).GetConstructor(new[] { typeof(GetHashCodeDelegate), typeof(int), typeof(bool) }));
            Assert.NotNull(typeof(ConcurrentCardinalityEstimator).GetConstructor(new[] { typeof(GetHashCodeDelegate), typeof(int), typeof(bool) }));
            var legacy = new CardinalityEstimator(IdentityHash);
            legacy.Add(1UL);
            legacy.Add(2UL);
            legacy.Add(1UL);
            byte[] expected =
            {
                3, 0, 1, 0, 14, 0, 0, 0, 3, 2, 0, 0, 0,
                1, 0, 0, 0, 0, 0, 0, 0,
                2, 0, 0, 0, 0, 0, 0, 0,
                3, 0, 0, 0, 0, 0, 0, 0,
            };
            Assert.Equal(expected, Serialize(legacy));
            Assert.Equal(EstimationAlgorithm.HyperLogLog, legacy.Algorithm);
            var restored = new CardinalityEstimatorSerializer().Deserialize(new MemoryStream(expected), IdentityHash);
            Assert.Equal(EstimationAlgorithm.HyperLogLog, restored.Algorithm);
            Assert.Equal(2UL, restored.Count());
            Assert.Equal(3UL, restored.CountAdditions);

            var explicitLegacy = new CardinalityEstimator(EstimationAlgorithm.HyperLogLog, IdentityHash);
            explicitLegacy.Add(1UL);
            explicitLegacy.Add(2UL);
            explicitLegacy.Add(1UL);
            Assert.Equal(expected, Serialize(explicitLegacy));
        }

        [Theory]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus)]
        [InlineData(EstimationAlgorithm.SuperLogLog)]
        public void ExactCountingTransitionsAndAllInputOverloadsRemainAvailable(EstimationAlgorithm algorithm)
        {
            var estimator = new CardinalityEstimator(algorithm, Murmur3.GetHashCode);
            using var concurrent = new ConcurrentCardinalityEstimator(algorithm, Murmur3.GetHashCode);
            for (int i = 0; i < 100; i++)
            {
                Assert.True(estimator.Add(i));
                Assert.True(concurrent.Add(i));
                Assert.False(estimator.Add(i));
                Assert.False(concurrent.Add(i));
            }
            Assert.Equal(100UL, estimator.Count());
            Assert.Equal(100UL, concurrent.Count());
            Assert.NotNull(estimator.GetState().DirectCount);
            estimator.Add(100);
            concurrent.Add(100);
            Assert.Null(estimator.GetState().DirectCount);

            byte[] bytes = { 7, 8, 9 };
            estimator.Add("text"); concurrent.Add("text");
            estimator.Add(2U); concurrent.Add(2U);
            estimator.Add(3L); concurrent.Add(3L);
            estimator.Add(4UL); concurrent.Add(4UL);
            estimator.Add(5F); concurrent.Add(5F);
            estimator.Add(6D); concurrent.Add(6D);
            estimator.Add(bytes); concurrent.Add(bytes);
            estimator.Add(bytes.AsSpan()); concurrent.Add(bytes.AsSpan());
            estimator.Add((ReadOnlySpan<byte>)bytes); concurrent.Add((ReadOnlySpan<byte>)bytes);
            estimator.Add(bytes.AsMemory()); concurrent.Add(bytes.AsMemory());
            estimator.Add((ReadOnlyMemory<byte>)bytes); concurrent.Add((ReadOnlyMemory<byte>)bytes);
            Assert.Equal(estimator.Count(), concurrent.Count());
            Assert.Equal(estimator.CountAdditions, concurrent.CountAdditions);
            Assert.Equal(algorithm, concurrent.ToCardinalityEstimator().Algorithm);
        }

        [Fact]
        public void HllPlusPlusSparseRegistersRetainAdditionalPrecisionAndRanks()
        {
            var estimator = Create(EstimationAlgorithm.HyperLogLogPlusPlus);
            for (uint index = 1; index <= 1000; index++)
                Assert.True(estimator.Add(((ulong)index << 39) | (1UL << 38)));
            Assert.True(estimator.GetState().IsSparse);
            Assert.Equal(1000UL, estimator.Count());
            Assert.Equal(1000, estimator.GetState().LookupSparseHighPrecision.Length);
            ulong stronger = (1UL << 39) | 1;
            Assert.True(estimator.Add(stronger));
            Assert.False(estimator.Add(stronger));
            Assert.False(estimator.Add((1UL << 39) | (1UL << 38)));
            Assert.Equal(1000UL, estimator.Count());
        }

        [Fact]
        public void HllPlusPlusPromotionFoldsExtraIndexBitsIntoRanks()
        {
            const int bits = 11;
            var plus = Create(EstimationAlgorithm.HyperLogLogPlusPlus, bits);
            var reference = Create(EstimationAlgorithm.HyperLogLog, bits);
            ulong[] edges = { 0, 1, 1UL << 39, (1UL << 50) | 1, ulong.MaxValue };
            foreach (ulong hash in edges)
            {
                plus.Add(hash);
                reference.Add(hash);
            }
            for (uint index = 1; index <= 300; index++)
            {
                ulong hash = ((ulong)(index * 7919) << 39) | (index % 2 == 0 ? 1UL : (1UL << 38));
                plus.Add(hash);
                reference.Add(hash);
            }
            Assert.False(plus.GetState().IsSparse);
            Assert.Equal(reference.GetState().LookupDense, plus.GetState().LookupDense);
        }

        [Theory]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 14, 50, 75)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 14, 50, 500)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 14, 500, 50)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 11, 180, 180)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 11, 180, 1000)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 11, 1000, 180)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 11, 1000, 1000)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 10, 50, 75)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 10, 50, 1000)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 10, 1000, 50)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 10, 1000, 1000)]
        public void MergingMatchesIngestionOfUnion(EstimationAlgorithm algorithm, int bits, int leftCount, int rightCount)
        {
            var left = Create(algorithm, bits, true);
            var right = Create(algorithm, bits, true);
            var reference = Create(algorithm, bits, true);
            for (int i = 0; i < leftCount; i++)
            {
                ulong hash = Mix((ulong)i);
                left.Add(hash);
                reference.Add(hash);
            }
            for (int i = 0; i < rightCount; i++)
            {
                ulong hash = Mix((ulong)(i + leftCount / 2));
                right.Add(hash);
                reference.Add(hash);
            }
            var reverse = new CardinalityEstimator(right);
            reverse.Merge(left);
            left.Merge(right);
            Assert.Equal(reference.Count(), left.Count());
            Assert.Equal(reference.Count(), reverse.Count());
            Assert.Equal(reference.CountAdditions, left.CountAdditions);
            if (reference.GetState().DirectCount != null)
            {
                Assert.True(reference.GetState().DirectCount.SetEquals(left.GetState().DirectCount));
                Assert.True(reference.GetState().DirectCount.SetEquals(reverse.GetState().DirectCount));
            }
            else
            {
                Assert.Equal(Serialize(reference), Serialize(left));
                Assert.Equal(Serialize(reference), Serialize(reverse));
            }
            ulong count = left.Count();
            left.Merge(left);
            Assert.Equal(count, left.Count());
            Assert.Equal(reference.CountAdditions * 2, left.CountAdditions);
        }

        [Theory]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 4, 0)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 14, 50)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 14, 1000)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 11, 1000)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 4, 0)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 14, 50)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 14, 1000)]
        public void NewWireFormatRoundTripsAndCopiesAreIndependent(EstimationAlgorithm algorithm, int bits, int count)
        {
            var original = Create(algorithm, bits, count > 0);
            for (int i = 0; i < count; i++)
                original.Add(Mix((ulong)i));
            byte[] payload = Serialize(original);
            Assert.Equal(CardinalityEstimatorSerializer.AdvancedDataFormatMajorVersion, BitConverter.ToUInt16(payload, 0));
            var restored = new CardinalityEstimatorSerializer().Deserialize(new MemoryStream(payload), IdentityHash);
            Assert.Equal(algorithm, restored.Algorithm);
            Assert.Equal(original.Count(), restored.Count());
            Assert.Equal(original.CountAdditions, restored.CountAdditions);
            Assert.Equal(payload, Serialize(restored));

            var copy = new CardinalityEstimator(original);
            Assert.True(original.Equals(copy));
            using var concurrent = new ConcurrentCardinalityEstimator(original);
            using var concurrentCopy = new ConcurrentCardinalityEstimator(concurrent);
            Assert.True(concurrent.Equals(concurrentCopy));
            var snapshot = concurrent.ToCardinalityEstimator();
            for (int i = 10000; i < 30000; i++)
            {
                ulong hash = Mix((ulong)i);
                restored.Add(hash);
                copy.Add(hash);
                concurrent.Add(hash);
            }
            Assert.Equal(copy.Count(), restored.Count());
            Assert.Equal(copy.Count(), concurrent.Count());
            Assert.Equal(payload, Serialize(original));
            Assert.Equal(payload, Serialize(snapshot));
            Assert.Equal(payload, Serialize(concurrentCopy.ToCardinalityEstimator()));
        }

        [Theory]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus)]
        [InlineData(EstimationAlgorithm.SuperLogLog)]
        public void ConcurrentIngestionAndParallelMergesPreserveTheAlgorithm(EstimationAlgorithm algorithm)
        {
            using var concurrent = new ConcurrentCardinalityEstimator(algorithm, IdentityHash, b: 11, useDirectCounting: false);
            var reference = Create(algorithm, 11);
            Parallel.For(0, 10000, i =>
            {
                concurrent.Add(Mix((ulong)i));
                if (i % 100 == 0)
                    concurrent.Count();
            });
            for (int i = 0; i < 10000; i++)
                reference.Add(Mix((ulong)i));
            Assert.Equal(Serialize(reference), Serialize(concurrent.ToCardinalityEstimator()));

            var shards = Enumerable.Range(0, 10).Select(_ => Create(algorithm, 11)).ToArray();
            for (int i = 0; i < 10000; i++)
                shards[i % shards.Length].Add(Mix((ulong)i));
            using var merged = shards.ParallelMerge(parallelismDegree: 2);
            Assert.Equal(algorithm, merged.Algorithm);
            Assert.Equal(Serialize(reference), Serialize(merged.ToCardinalityEstimator()));
            using var secondShard = new ConcurrentCardinalityEstimator(shards[1]);
            using var safe = CardinalityEstimatorExtensions.SafeMerge(shards[0], secondShard);
            var two = CardinalityEstimator.Merge(shards.Take(2));
            Assert.Equal(two.Count(), safe.Count());

            var source = new CardinalityEstimator(reference);
            concurrent.Merge(source);
            ulong before = concurrent.Count();
            concurrent.Merge(concurrent);
            Assert.Equal(before, concurrent.Count());
        }

        [Fact]
        public void IncompatibleAlgorithmMergesFailBeforeMutatingTheTarget()
        {
            foreach (EstimationAlgorithm algorithm in Enum.GetValues(typeof(EstimationAlgorithm)))
            {
                var target = Create(algorithm, 11, true);
                target.Add(1UL);
                using var concurrent = new ConcurrentCardinalityEstimator(target);
                byte[] before = Serialize(target);
                foreach (EstimationAlgorithm otherAlgorithm in Enum.GetValues(typeof(EstimationAlgorithm)))
                {
                    if (algorithm == otherAlgorithm)
                        continue;
                    var other = Create(otherAlgorithm, 11, true);
                    using var otherConcurrent = new ConcurrentCardinalityEstimator(other);
                    Assert.Throws<ArgumentException>(() => target.Merge(other));
                    Assert.Throws<ArgumentException>(() => concurrent.Merge(other));
                    Assert.Throws<ArgumentException>(() => concurrent.Merge(otherConcurrent));
                    Assert.Throws<ArgumentException>(() => ConcurrentCardinalityEstimator.ParallelMerge(new[] { concurrent, otherConcurrent }));
                }
                Assert.Equal(before, Serialize(target));
                Assert.Equal(before, Serialize(concurrent.ToCardinalityEstimator()));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardinalityEstimator((EstimationAlgorithm)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentCardinalityEstimator((EstimationAlgorithm)(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardinalityEstimator(EstimationAlgorithm.SuperLogLog, b: 3));
        }

        [Fact]
        public void SuperLogLogTruncatesOutliersWithoutModifyingStoredRegisters()
        {
            const int bits = 10;
            int m = 1 << bits;
            var ordinary = Enumerable.Repeat((byte)10, m).ToArray();
            var outliers = (byte[])ordinary.Clone();
            for (int i = 7 * m / 10; i < m; i++)
                outliers[i] = 55;
            CardinalityEstimator FromRegisters(byte[] registers) => new CardinalityEstimator(IdentityHash,
                new CardinalityEstimatorState { Algorithm = EstimationAlgorithm.SuperLogLog, BitsPerIndex = bits, LookupDense = registers });
            var baseline = FromRegisters(ordinary);
            var sketch = FromRegisters(outliers);
            byte[] before = Serialize(sketch);
            Assert.Equal(baseline.Count(), sketch.Count());
            Assert.Equal(before, Serialize(sketch));
            var empty = Create(EstimationAlgorithm.SuperLogLog, bits);
            Assert.Equal(0UL, empty.Count());
            var saturated = FromRegisters(Enumerable.Repeat((byte)55, m).ToArray());
            Assert.Equal(ulong.MaxValue, saturated.Count());
        }

        [Theory]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 4)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 10)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 14)]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus, 16)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 4)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 10)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 14)]
        [InlineData(EstimationAlgorithm.SuperLogLog, 16)]
        public void DenseAccuracyIsValidatedAcrossIndependentTrials(EstimationAlgorithm algorithm, int bits)
        {
            const int trials = 48;
            double sum = 0, squares = 0;
            for (int trial = 0; trial < trials; trial++)
            {
                int cardinality = (1 << bits) * (16 + 4 * (trial % 4));
                var estimator = Create(algorithm, bits);
                for (int i = 0; i < cardinality; i++)
                    estimator.Add(Mix((ulong)i + (ulong)trial * 1000000007));
                double error = (double)estimator.Count() / cardinality - 1;
                sum += error;
                squares += error * error;
            }
            double bias = sum / trials;
            double rmse = Math.Sqrt(squares / trials);
            double standardError = 1.05 / Math.Sqrt(1 << bits);
            output.WriteLine("{0}, b={1}: bias={2:P3}, RMSE={3:P3}, nominal error={4:P3}", algorithm, bits, bias, rmse, standardError);
            Assert.True(Math.Abs(bias) < 0.6 * standardError + 0.001, $"Bias {bias} exceeds tolerance.");
            Assert.True(rmse < 1.5 * standardError, $"RMSE {rmse} exceeds tolerance.");
        }

        [Theory]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus)]
        [InlineData(EstimationAlgorithm.SuperLogLog)]
        public void AccuracyIsValidatedAroundRepresentationAndCountingTransitions(EstimationAlgorithm algorithm)
        {
            foreach (int bits in new[] { 11, 14 })
            {
                int m = 1 << bits;
                int sparseLimit = (m - HllPlusPlusSparse.BufferBudgetBytes) / sizeof(uint);
                int linearThreshold = (int)HllConstants.GetSubAlgorithmSelectionThreshold(bits);
                foreach (int cardinality in new[]
                {
                    100, 101, sparseLimit, sparseLimit + 1, linearThreshold, linearThreshold + 1,
                    5 * m / 2, 5 * m / 2 + 1, 5 * m,
                })
                {
                    const int trials = 32;
                    double sum = 0, squares = 0;
                    for (int trial = 0; trial < trials; trial++)
                    {
                        var estimator = Create(algorithm, bits);
                        for (int i = 0; i < cardinality; i++)
                            estimator.Add(Mix((ulong)i + (ulong)trial * 1000000007));
                        double error = (double)estimator.Count() / cardinality - 1;
                        sum += error;
                        squares += error * error;
                    }
                    double sigma = 1.05 / Math.Sqrt(m);
                    double bias = sum / trials, rmse = Math.Sqrt(squares / trials);
                    output.WriteLine("{0}, b={1}, n={2}: bias={3:P3}, RMSE={4:P3}", algorithm, bits, cardinality, bias, rmse);
                    Assert.True(Math.Abs(bias) < 0.7 * sigma + 0.001, $"b={bits}, n={cardinality}: bias={bias}");
                    Assert.True(rmse < 1.6 * sigma, $"b={bits}, n={cardinality}: RMSE={rmse}");
                }
            }
        }

        [Theory]
        [InlineData(EstimationAlgorithm.HyperLogLogPlusPlus)]
        [InlineData(EstimationAlgorithm.SuperLogLog)]
        public void OppositeDirectionConcurrentMergesDoNotDeadlock(EstimationAlgorithm algorithm)
        {
            using var first = new ConcurrentCardinalityEstimator(algorithm, IdentityHash, b: 11, useDirectCounting: false);
            using var second = new ConcurrentCardinalityEstimator(algorithm, IdentityHash, b: 11, useDirectCounting: false);
            var reference = Create(algorithm, 11);
            for (int i = 0; i < 2000; i++)
            {
                ulong hash = Mix((ulong)i);
                reference.Add(hash);
                if (i % 2 == 0)
                    first.Add(hash);
                else
                    second.Add(hash);
            }
            var forward = Task.Run(() => first.Merge(second));
            var reverse = Task.Run(() => second.Merge(first));
            Assert.True(Task.WaitAll(new[] { forward, reverse }, TimeSpan.FromSeconds(10)));
            Assert.Equal(reference.Count(), first.Count());
            Assert.Equal(reference.Count(), second.Count());
        }

        [Theory]
        [InlineData(4, 99)]
        [InlineData(5, 255)]
        [InlineData(9, 24)]
        [InlineData(10, 128)]
        [InlineData(14, 255)]
        public void AdvancedDeserializerRejectsInvalidHeadersAndLengths(int offset, byte value)
        {
            var estimator = Create(EstimationAlgorithm.HyperLogLogPlusPlus, 14);
            estimator.Add(Mix(1));
            byte[] payload = Serialize(estimator);
            payload[offset] = value;
            Assert.Throws<InvalidDataException>(() => new CardinalityEstimatorSerializer().Deserialize(new MemoryStream(payload)));
        }

        [Fact]
        public void AdvancedDeserializerValidatesRanksAndTruncation()
        {
            var estimator = Create(EstimationAlgorithm.HyperLogLogPlusPlus, 14);
            estimator.Add(Mix(1));
            byte[] payload = Serialize(estimator);
            payload[15] &= 0xc0;
            Assert.Throws<InvalidDataException>(() => new CardinalityEstimatorSerializer().Deserialize(new MemoryStream(payload)));

            var dense = Create(EstimationAlgorithm.SuperLogLog, 4);
            byte[] invalidDense = Serialize(dense);
            invalidDense[15] = 62;
            Assert.Throws<InvalidDataException>(() => new CardinalityEstimatorSerializer().Deserialize(new MemoryStream(invalidDense)));
            byte[] valid = Serialize(dense);
            Assert.Throws<EndOfStreamException>(() => new CardinalityEstimatorSerializer().Deserialize(new MemoryStream(valid.Take(20).ToArray())));
        }
    }
}

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using CardinalityEstimation;

var config = DefaultConfig.Instance
                .AddJob(Job.Default.WithId("Core70").WithRuntime(CoreRuntime.Core70))
                .AddJob(Job.Default.WithId("Core80").WithRuntime(CoreRuntime.Core80));

if (args.Contains("--algorithms"))
    BenchmarkRunner.Run<EstimationAlgorithms>(args: args.Where(arg => arg != "--algorithms").ToArray());
else
    BenchmarkRunner.Run<DifferentHashes>(config);

[MemoryDiagnoser]
public class EstimationAlgorithms
{
    [Params(EstimationAlgorithm.HyperLogLog, EstimationAlgorithm.HyperLogLogPlusPlus, EstimationAlgorithm.SuperLogLog)]
    public EstimationAlgorithm Algorithm { get; set; }

    [Params(10, 14)]
    public int Bits { get; set; }

    [Params(100, 1000, 100000)]
    public int Cardinality { get; set; }

    private CardinalityEstimator filled = null!;
    private CardinalityEstimator other = null!;

    private CardinalityEstimator Create() => new CardinalityEstimator(
        Algorithm, CardinalityEstimation.Hash.Murmur3.GetHashCode, Bits);

    [GlobalSetup]
    public void Setup()
    {
        filled = Create();
        other = Create();
        for (int i = 0; i < Cardinality; i++)
        {
            filled.Add(i);
            other.Add(i + Cardinality / 2);
        }
    }

    [Benchmark]
    public CardinalityEstimator Add()
    {
        var estimator = Create();
        for (int i = 0; i < Cardinality; i++)
            estimator.Add(i);
        return estimator;
    }

    [Benchmark]
    public ulong Count() => filled.Count();

    [Benchmark]
    public CardinalityEstimator Merge()
    {
        var result = new CardinalityEstimator(filled);
        result.Merge(other);
        return result;
    }

    [Benchmark]
    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        new CardinalityEstimatorSerializer().Serialize(stream, filled, leaveOpen: true);
        return stream.ToArray();
    }
}

[MemoryDiagnoser]
public class DifferentHashes
{
    public static readonly Random Rand = new Random();
       
    private const int N = 10000000;

    private string[] dataStrings = Enumerable.Range(0, N).Select(_ => Rand.Next().ToString() + Guid.NewGuid().ToString() + Rand.Next().ToString()).ToArray();

    [Params(4, 16)]
    public int Bits { get; set; }

    [Benchmark]
    public void Murmur3() => Run(Bits, CardinalityEstimation.Hash.Murmur3.GetHashCode);
    [Benchmark]
    public void Fnv1A() => Run(Bits, CardinalityEstimation.Hash.Fnv1A.GetHashCode);
    [Benchmark]
    public void XxHash128() => Run(Bits, (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash128.Hash(x)));


    private void Run(int bits, GetHashCodeDelegate hashFunction)
    {
        var hll = new CardinalityEstimator(hashFunction, bits);
        for (var i = 0; i < N; i++)
        {
            hll.Add(dataStrings[i]);
        }
    }
}

[MemoryDiagnoser]
public class GetBytesTests
{
    public static readonly Random Rand = new Random();

    private const int N = 10000000;

    private int[] dataInts = Enumerable.Range(0, N).Select(_ => Rand.Next()).ToArray();

    [Params(4, 16)]
    public int Bits { get; set; }

    [Benchmark(Baseline = true)]
    public void GetBytes()
    {
        GetHashCodeDelegate hashFunction = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash64.Hash(x));
        var hll = new CardinalityEstimator(hashFunction, Bits);
        for (var i = 0; i < N; i++)
        {
            hll.Add(dataInts[i]);
        }
    }

    [Benchmark]
    public void WriteToBytes()
    {
        GetHashCodeDelegate hashFunction = (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash64.Hash(x));
        var hll = new CardinalityEstimator(hashFunction, Bits);
        var bytes = new byte[sizeof(int)];
        for (var i = 0; i < N; i++)
        {
            BitConverter.TryWriteBytes(bytes, dataInts[i]);
            hll.Add(bytes);
        }
    }
}

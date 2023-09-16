using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using CardinalityEstimation;

var config = DefaultConfig.Instance
                .AddJob(Job.Default.WithId("Core70").WithRuntime(CoreRuntime.Core70))
                .AddJob(Job.Default.WithId("Core80").WithRuntime(CoreRuntime.Core80));

BenchmarkRunner.Run<DifferentHashes>(config);

[MemoryDiagnoser]
public class DifferentHashes
{
    public static readonly Random Rand = new Random();
       
    private const int N = 1000;

    private string[] dataStrings = Enumerable.Range(0, N).Select(_ => Rand.Next().ToString() + Guid.NewGuid().ToString() + Rand.Next().ToString()).ToArray();

    [Params(4, 16)]
    public int Bits { get; set; }

    [Benchmark]
    public void Murmur3() => Run(Bits, CardinalityEstimation.Hash.Murmur3.GetHashCode);
    [Benchmark]
    public void Fnv1A() => Run(Bits, CardinalityEstimation.Hash.Fnv1A.GetHashCode);
    [Benchmark]
    public void XxHash64() => Run(Bits, (x) => BitConverter.ToUInt64(System.IO.Hashing.XxHash64.Hash(x)));


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

    private const int N = 1000;

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

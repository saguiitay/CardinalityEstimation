# CardinalityEstimation

HyperLogLog-based set cardinality estimation library

This library estimates the number of unique elements in a set, in a quick and memory-efficient manner.  It's based on the following:

1. Flajolet et al., "HyperLogLog: the analysis of a near-optimal cardinality estimation algorithm", DMTCS proc. AH 2007, http://algo.inria.fr/flajolet/Publications/FlFuGaMe07.pdf
2. Heule, Nunkesser and Hall 2013, "HyperLogLog in Practice: Algorithmic Engineering of a State of The Art Cardinality Estimation Algorithm", https://research.google/pubs/hyperloglog-in-practice-algorithmic-engineering-of-a-state-of-the-art-cardinality-estimation-algorithm/

The accuracy/memory usage are user-selectable.  Typically, a cardinality estimator will give a perfect estimate of small cardinalities (up to 100 unique elements), and 97% accuracy or better (usually much better) for any cardinality up to near 2^64, while consuming several KB of memory (no more than 16KB).

## Usage

Usage is very simple:

```csharp
ICardinalityEstimator<string> estimator = new CardinalityEstimator();

estimator.Add("Alice");
estimator.Add("Bob");
estimator.Add("Alice"); // duplicate — already counted
estimator.Add("George Michael");

ulong numberOfuniqueElements = estimator.Count(); // will be 3 — "Alice" was added twice but counted once
```

## Nuget Package

This code is available as the Nuget package [CardinalityEstimation](https://www.nuget.org/packages/CardinalityEstimation/). A strong-named build is also published as [CardinalityEstimation.Signed](https://www.nuget.org/packages/CardinalityEstimation.Signed/).

To install, use the .NET CLI:

```bash
dotnet add package CardinalityEstimation
```

Or, equivalently, the Package Manager Console in Visual Studio:

```powershell
Install-Package CardinalityEstimation
```

## Controlling Accuracy and Memory

The constructor accepts a `b` parameter that controls the tradeoff between accuracy and memory:

```csharp
// b = 14 (default) — ~3% standard error or better, up to ~16 KB per estimator
var estimator = new CardinalityEstimator(b: 14);

// b = 4 — minimum, < 1 KB but high error (up to ~100%)
var tinyEstimator = new CardinalityEstimator(b: 4);

// b = 16 — maximum, ~1% error or better, up to ~64 KB
var preciseEstimator = new CardinalityEstimator(b: 16);
```

`b` must be in the range `[4, 16]`. The standard error for large cardinalities is approximately `1.04 * 2^(-b/2)`, and memory consumption is bounded by `2^b` bytes for the dense representation. The estimator gives **perfect** counts for the first 100 elements regardless of `b`, then transitions to a sparse representation, and finally to the dense HyperLogLog representation as cardinality grows.

## Hash Functions

By default the estimator uses `XxHash128` (from `System.IO.Hashing`), which is fast and has excellent distribution properties. Two alternatives ship in the box:

- `Murmur3` (`CardinalityEstimation.Hash.Murmur3`)
- `FNV-1a` (`CardinalityEstimation.Hash.Fnv1A`)

You can supply your own `GetHashCodeDelegate` to the constructor:

```csharp
using CardinalityEstimation;
using CardinalityEstimation.Hash;

GetHashCodeDelegate murmur = Murmur3.GetHashCode;
var estimator = new CardinalityEstimator(hashFunction: murmur);
```

The hash must be a 64-bit hash with good distribution — biased hashes will degrade estimate accuracy. When merging or deserializing estimators, all participants must have been built with the same hash function.

## Thread-Safe Usage

`CardinalityEstimator` is **not** thread-safe. For concurrent producers, use `ConcurrentCardinalityEstimator`, which wraps the same algorithm with a `ReaderWriterLockSlim`:

```csharp
var estimator = new ConcurrentCardinalityEstimator();

Parallel.ForEach(events, e => estimator.Add(e.UserId));

ulong uniqueUsers = estimator.Count();
```

Use `ConcurrentCardinalityEstimator` when multiple threads add to (or read from) the same estimator concurrently. If each thread owns its own estimator and you only combine results at the end, the basic `CardinalityEstimator` is faster — combine them with `Merge` (see below).

## Merging Estimators

Estimators built with the same `b` and the same hash function can be merged losslessly. This is the core primitive for distributed and parallel cardinality counting — partition your data, count each shard independently, then merge:

```csharp
var a = new CardinalityEstimator();
var b = new CardinalityEstimator();

a.Add("Alice"); a.Add("Bob");
b.Add("Bob");   b.Add("Carol");

// In-place merge of b into a
a.Merge(b);
ulong unique = a.Count(); // 3

// Static merge of many estimators into a new one
CardinalityEstimator combined = CardinalityEstimator.Merge(new[] { a, b });
```

For large numbers of estimators you can merge them in parallel via the extension method in `CardinalityEstimatorExtensions`:

```csharp
using CardinalityEstimation;

ConcurrentCardinalityEstimator merged = shardEstimators.ParallelMerge();
```

`ParallelMerge` returns a `ConcurrentCardinalityEstimator` and uses all available cores by default; pass `parallelismDegree` to cap it.

## Serialization

Use `CardinalityEstimatorSerializer` to persist an estimator to a stream and restore it later (e.g. to checkpoint state, ship it across the wire, or store it in a cache):

```csharp
var serializer = new CardinalityEstimatorSerializer();

// Serialize
using (var stream = File.Create("estimator.bin"))
{
    serializer.Serialize(stream, estimator);
}

// Deserialize — pass the same hash function the estimator was built with
using (var stream = File.OpenRead("estimator.bin"))
{
    CardinalityEstimator restored = serializer.Deserialize(stream);
}
```

The serializer uses a versioned binary format (see `DataFormatMajorVersion` / `DataFormatMinorVersion` in `CardinalityEstimatorSerializer`) so newer minor versions can read older payloads of the same major version.

## Zero-Allocation / High-Performance

For hot paths where you already have bytes (e.g. from a buffer pool, a network packet, or `stackalloc`), `CardinalityEstimator` implements `ICardinalityEstimatorMemory`, which exposes `Add` overloads for `Span<byte>`, `ReadOnlySpan<byte>`, `Memory<byte>`, and `ReadOnlyMemory<byte>`:

```csharp
ICardinalityEstimatorMemory estimator = new CardinalityEstimator();

Span<byte> buffer = stackalloc byte[16];
// ...fill buffer...
estimator.Add(buffer); // no allocation
```

These overloads route through a `GetHashCodeSpanDelegate` and avoid the byte-array allocation of the legacy path.

## Release Notes

### 1.15.0
- Hardened `CardinalityEstimatorSerializer` against DoS via crafted input.
- Switched target frameworks to `net8.0` / `net10.0`.
- Updated `System.IO.Hashing` to `10.0.7`.
- Fixed O(n) `ConcurrentCardinalityEstimator` direct-count storage (`ConcurrentBag` → `ConcurrentDictionary`).
- Zero-allocation primitive `Add` overloads (`stackalloc` + span hash).
- Precomputed inverse-powers-of-two table in `Count()` (removes `Math.Pow` from hot loop).
- Bulk-write dense lookup array in serializer.
- Fixed `CardinalityEstimator.Merge(IEnumerable)` double-counting `CountAdditions` for the seed element; copy constructor now preserves `CountAdditions`.
- Fixed `ConcurrentCardinalityEstimator` span-delegate constructor silently discarding the supplied delegate.
- Added null-argument validation to `ConcurrentCardinalityEstimator.Add(string)` and `Add(byte[])` for parity with `CardinalityEstimator`.
- Compute `m` via bit shift (`1 << bitsPerIndex`) instead of `(int)Math.Pow(2, bitsPerIndex)` in constructors.
- Documented the intentionally empty version-3 branch in `CardinalityEstimatorSerializer.Read`.
- Consolidated duplicated constants (`DirectCounterMaxElements`, `StackallocByteThreshold`) and helpers (`GetAlphaM`, `GetSubAlgorithmSelectionThreshold`, `CreateEmptyState`) into `HllConstants`.
- Honored `parallelismDegree` in `ConcurrentCardinalityEstimator.ParallelMerge` and removed dead `ParallelQuery` variable.
- Replaced `GetHashCode`-based lock ordering in `ConcurrentCardinalityEstimator.Merge`/`Equals` with a unique per-instance ID to eliminate a deadlock window on hash collisions.

### 1.14.0
- Added support for `Span<byte>`, `ReadOnlySpan<byte>`, `Memory<byte>`, and `ReadOnlyMemory<byte>` via `ICardinalityEstimatorMemory` (zero-allocation hot path).
- Added `ConcurrentCardinalityEstimator` for thread-safe usage, plus `ParallelMerge` / `SafeMerge` extensions.
- Updated and expanded XML documentation.

### 1.13.0
- Switched the default hash function to `XxHash128` (from `System.IO.Hashing`) on .NET 8+ for better speed and distribution. `Murmur3` and `FNV-1a` remain available.
- Optimized `GetSigma` on .NET 8.

### 1.12.0
- Targets `net8.0` and `net10.0`; dropped support for end-of-life .NET versions.
- Added the `CardinalityEstimation.Benchmark` project (BenchmarkDotNet).
- Made the hashing classes (`Murmur3`, `Fnv1A`) public.

## Keeping things friendly

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

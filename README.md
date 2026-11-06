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

## Selecting an Algorithm

The original constructors and default algorithm are unchanged. To opt in to another
algorithm, use the new overload with an explicit `EstimationAlgorithm`:

```csharp
var plus = new CardinalityEstimator(
    algorithm: EstimationAlgorithm.HyperLogLogPlusPlus, b: 14);

var super = new CardinalityEstimator(
    algorithm: EstimationAlgorithm.SuperLogLog, b: 14);

using var concurrent = new ConcurrentCardinalityEstimator(
    algorithm: EstimationAlgorithm.HyperLogLogPlusPlus, b: 14);
```

All algorithms support the existing primitive, string, byte-array, and memory-type
`Add` overloads, custom hash functions, copies, merges, and `CountAdditions`.
The `Algorithm` property identifies the selected algorithm. Exact counting of up
to 100 distinct hashes remains enabled by default; pass `useDirectCounting: false`
to estimate from the start.

| Algorithm | Behavior and tradeoffs |
| --- | --- |
| `HyperLogLog` | The original implementation, including its existing interpolated bias correction and sparse/dense behavior. This remains the default. |
| `HyperLogLogPlusPlus` | Uses 25-bit sparse precision, buffered sorted 32-bit entries, and linear counting while sparse. Promotes to `2^b` byte-sized dense registers when the estimated sparse storage cost exceeds the dense budget. Dense estimation uses six-nearest-neighbor bias correction and the published precision-dependent linear-counting thresholds. |
| `SuperLogLog` | Uses `2^b` byte-sized registers after exact counting. Estimates from the lowest `floor(0.7 * 2^b)` ranks without modifying or discarding the stored registers. Uses SuperLogLog-specific empirical normalization, linear counting for small cardinalities, and a Poisson-model periodic-bias correction at `b >= 10`. |

HLL++ primarily improves small-cardinality accuracy and sparse memory usage over
the existing implementation, which already incorporates several HLL++ techniques.
It is not a promise of better accuracy at every cardinality. SuperLogLog is an
alternative, not an upgrade over HLL: its nominal large-cardinality standard error
is approximately `1.05 / sqrt(2^b)`. These are statistical standard errors, not
per-estimate error guarantees.

SuperLogLog's normalization is calibrated for each supported precision using
independent Poisson-distributed register maxima; the calibration recipe is recorded
with the coefficients in the implementation. Registers retain the full rank range
of the 64-bit hash, so no smaller maximum-cardinality bound is required. New
algorithms saturate `Count()` at `ulong.MaxValue` if an estimate exceeds the return
type's range.

The implementations follow [Heule, Nunkesser and Hall (HLL++)](https://www.openproceedings.org/2013/conf/edbt/HeuleNH13.pdf)
and [Durand and Flajolet (SuperLogLog)](https://algo.inria.fr/flajolet/Publications/DuFl03-LNCS.pdf).
Algorithm support does not imply binary compatibility with sketches produced by
other libraries or services.

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

Estimators must also use the same `Algorithm`. A cross-algorithm merge throws
`ArgumentException` before changing the destination. Copies, concurrent snapshots,
`SafeMerge`, and `ParallelMerge` preserve the selected algorithm. As before, callers
are responsible for using the same hash function across all participants.

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

**Backward compatibility:** the default `HyperLogLog` estimator still writes the
original **3.1** format, with unchanged layout. Existing serialized estimators
continue to deserialize using the original algorithm and hashing rules.
Only opt-in HLL++ and SuperLogLog estimators use format **4.1**, which includes
algorithm identity and sparse precision. `AdvancedDataFormatMajorVersion` identifies
this additional format. Older package versions cannot read 4.1 payloads; upgrade
all readers before exchanging opt-in sketches in a rolling deployment.
Hash functions are still supplied by the caller rather than serialized.

## Algorithm Benchmarks

Run the opt-in BenchmarkDotNet comparisons for add, count, merge, and serialization:

```powershell
dotnet run --project CardinalityEstimation.Benchmark -c Release --framework net10.0 -- --algorithms
```

The comparisons cover all three algorithms at multiple precisions and cardinalities,
with allocation diagnostics. Running the benchmark project without `--algorithms`
continues to run the existing hash-function benchmarks.

## Zero-Allocation / High-Performance

For hot paths where you already have bytes (e.g. from a buffer pool, a network packet, or `stackalloc`), `CardinalityEstimator` implements `ICardinalityEstimatorMemory`, which exposes `Add` overloads for `Span<byte>`, `ReadOnlySpan<byte>`, `Memory<byte>`, and `ReadOnlyMemory<byte>`:

```csharp
ICardinalityEstimatorMemory estimator = new CardinalityEstimator();

Span<byte> buffer = stackalloc byte[16];
// ...fill buffer...
estimator.Add(buffer); // no allocation
```

These overloads route through a `GetHashCodeSpanDelegate` and avoid the byte-array allocation of the legacy path.

## Code Coverage

Coverage is collected locally with [`dotnet-coverage`](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage), pinned as a [local .NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/local-tools-how-to-use) in `.config/dotnet-tools.json`. From a fresh checkout:

```bash
dotnet tool restore
dotnet build
dotnet dotnet-coverage collect --output-format cobertura --output coverage.xml \
    "dotnet test --no-build --nologo"
```

The resulting `coverage.xml` is gitignored and can be fed into ReportGenerator, Codecov, or any Cobertura-aware viewer.CI does not currently collect coverage — this is a developer-local workflow.

## Release Notes

### 1.16.0
- Added opt-in HyperLogLog++ and SuperLogLog algorithms through `EstimationAlgorithm` constructor overloads.
- Preserved existing constructors, default estimation behavior, and legacy 3.1 serialization.
- Added format 4.1 for opt-in sketches; older readers must be upgraded before receiving them.
- Supported both algorithms in concurrent estimators, copying, snapshots, and merge utilities.
- Added deterministic accuracy and compatibility tests and BenchmarkDotNet algorithm comparisons.
- Updated the package version to `1.16.0` and aligned assembly and file versions to `1.16.0.0`.

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
- Exposed `HashFunction`/`HashFunctionSpan` properties on `CardinalityEstimator` and `ConcurrentCardinalityEstimator` and made cross-type conversions preserve the source hash functions losslessly.
- Replaced the unused `coverlet.collector` test package with a `dotnet-coverage` local tool manifest; coverage is now a developer-local opt-in via `dotnet tool restore` + `dotnet dotnet-coverage collect`.

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

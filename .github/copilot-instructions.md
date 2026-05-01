# CardinalityEstimation – Copilot Instructions

A C# library for estimating the number of unique elements in a set using HyperLogLog, packaged as the `CardinalityEstimation` (and `CardinalityEstimation.Signed`) NuGet package.

## Build, test, package

The solution multi-targets `net8.0` and `net9.0`. Both SDKs must be installed.

```powershell
dotnet restore
dotnet build --no-restore                                  # default = Debug
dotnet build -c Release
dotnet build -c Release-Signed                             # signed assembly + CardinalityEstimation.Signed package
dotnet test --no-build                                     # full xUnit suite, all TFMs
dotnet test --framework net9.0                             # single TFM
dotnet test --filter "FullyQualifiedName~CardinalityEstimatorTests.TestAccuracy"   # single test/class
```

`GeneratePackageOnBuild` is on, so building `Release` / `Release-Signed` produces the `.nupkg` (+ `.snupkg`). The `Release-Signed` configuration switches `PackageId` and swaps the `murmurhash` reference for `murmurhash-signed` — keep both branches working when touching the csproj.

CI (`.github/workflows/dotnet.yml`) runs `restore` → `build --no-restore` → `test --no-build` on Ubuntu against both SDKs. Don't introduce a step that requires the previous one's outputs to be regenerated.

Benchmarks live in `CardinalityEstimation.Benchmark` (BenchmarkDotNet) and are run manually via `dotnet run -c Release --project CardinalityEstimation.Benchmark`. They are not part of CI.

## Architecture

Three counting strategies inside a single `CardinalityEstimator`, switched automatically as cardinality grows:

1. **Direct counting** – exact `HashSet<ulong>` up to `DirectCounterMaxElements` (100).
2. **Sparse representation** – `Dictionary<ushort, byte>` of HLL substream → max-leading-zeros.
3. **Dense representation** – `byte[]` of size `m = 2^bitsPerIndex`.

Transitions are one-way (Direct → Sparse → Dense) and are driven by element count and dictionary size; `CardinalityEstimatorState` is the serializable snapshot of whichever representation is active. Never read more than one representation at a time — the inactive ones are `null`.

Hashing is pluggable via two delegates declared in `CardinalityEstimator.cs`:

- `GetHashCodeDelegate(byte[])` – legacy/array path
- `GetHashCodeSpanDelegate(ReadOnlySpan<byte>)` – zero-allocation path used by the `ICardinalityEstimatorMemory` overloads

Default hash is `XxHash128` (from `System.IO.Hashing`); `Murmur3` and `Fnv1A` live under `CardinalityEstimation/Hash/` and are selected through `HashFunctionId` on the constructor. Adding a hash means: implement it under `Hash/`, register an id, and extend the factory + serializer round-trip.

`CardinalityEstimator` implements `ICardinalityEstimator<T>` for each supported primitive (`string`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `byte[]`) plus `ICardinalityEstimatorMemory` for `Span<byte>` / `ReadOnlySpan<byte>` / `Memory<byte>` / `ReadOnlyMemory<byte>`. When adding a new element type, add an `Add` overload on the class **and** a matching `ICardinalityEstimator<T>` interface implementation — both are part of the public API.

`ConcurrentCardinalityEstimator` wraps the same state with a `ReaderWriterLockSlim` and `Interlocked` counters; `CardinalityEstimatorExtensions` adds parallel/distributed merge helpers (`ParallelMerge`, partitioned aggregation). Any new public method on `CardinalityEstimator` should also be exposed on the concurrent wrapper to keep the two APIs aligned.

### Serialization compatibility (important)

`CardinalityEstimatorSerializer` has explicit `DataFormatMajorVersion` / `DataFormatMinorVersion` constants. The rule encoded in the comments:

- A breaking change → bump **major**; older majors cannot be read.
- A non-breaking additive change → bump **minor**; older minor versions of the same major must still deserialize.

Any change to the on-the-wire layout (new field, new representation, new hash id) requires bumping the appropriate version and adding/keeping a read path for previous versions. There are serializer round-trip tests in `CardinalityEstimatorSerializerTests` that lock this in.

## Conventions

- File header: every `.cs` file starts with the MIT license block — preserve it on new files.
- `using` directives go **inside** the namespace, not above it (see any existing file).
- Public APIs are documented with full XML doc comments (`<summary>`, `<param>`, `<returns>`, `<remarks>`); this is enforced by convention, not analyzers, so match the style of neighbouring members.
- Tests are xUnit (`[Fact]` / `[Theory]`), one test class per production class, named `<ClassName>Tests`, located in `CardinalityEstimation.Test/` mirroring the source folder layout (e.g. `Hash/Murmur3Tests.cs`).
- Tests use `ITestOutputHelper` for diagnostics; accuracy tests print timing via a `Stopwatch` started in the ctor and stopped in `Dispose` — follow the same pattern for new perf-sensitive tests rather than adding a logging dependency.
- `InternalsVisibleTo` exposes internals to `CardinalityEstimation.Test` (see `InternalsVisible.cs`); prefer `internal` over `public` for helpers that only tests need.
- Bump `<Version>`, `<AssemblyVersion>`, `<FileVersion>` and `<PackageReleaseNotes>` in `CardinalityEstimation.csproj` together when shipping a release.
- `ROADMAP.md` tracks completed/planned features and the supported-frameworks story — update it when finishing a roadmap item rather than letting it drift.

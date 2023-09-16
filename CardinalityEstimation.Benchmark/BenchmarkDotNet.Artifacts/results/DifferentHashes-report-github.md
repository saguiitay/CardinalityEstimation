```

BenchmarkDotNet v0.13.8, Windows 11 (10.0.22621.2283/22H2/2022Update/SunValley2)
Intel Core i7-8700 CPU 3.20GHz (Coffee Lake), 1 CPU, 12 logical and 6 physical cores
.NET SDK 8.0.100-rc.1.23463.5
  [Host] : .NET 7.0.11 (7.0.1123.42427), X64 RyuJIT AVX2
  Core70 : .NET 7.0.11 (7.0.1123.42427), X64 RyuJIT AVX2
  Core80 : .NET 8.0.0 (8.0.23.41904), X64 RyuJIT AVX2


```
| Method    | Job    | Runtime  | Bits | Mean       | Error    | StdDev   | Gen0       | Allocated  |
|---------- |------- |--------- |----- |-----------:|---------:|---------:|-----------:|-----------:|
| **Murmur3**   | **Core70** | **.NET 7.0** | **4**    | **2,145.8 ms** | **28.16 ms** | **24.96 ms** | **34000.0000** | **2441.41 MB** |
| Fnv1A     | Core70 | .NET 7.0 | 4    | 1,062.2 ms | 10.78 ms |  9.55 ms | 10000.0000 |  762.95 MB |
| XxHash64  | Core70 | .NET 7.0 | 4    |   861.5 ms |  7.19 ms |  6.38 ms | 13000.0000 | 1068.12 MB |
| XxHash128 | Core70 | .NET 7.0 | 4    |   782.3 ms | 11.13 ms |  9.29 ms | 15000.0000 | 1144.42 MB |
| Murmur3   | Core80 | .NET 8.0 | 4    | 2,007.7 ms | 38.95 ms | 52.00 ms | 27000.0000 | 2441.41 MB |
| Fnv1A     | Core80 | .NET 8.0 | 4    | 1,013.4 ms |  6.26 ms |  5.85 ms |  8000.0000 |  762.95 MB |
| XxHash64  | Core80 | .NET 8.0 | 4    |   869.7 ms |  6.63 ms |  5.88 ms | 12000.0000 | 1068.12 MB |
| XxHash128 | Core80 | .NET 8.0 | 4    |   779.6 ms |  7.87 ms |  7.36 ms | 13000.0000 | 1144.42 MB |
| **Murmur3**   | **Core70** | **.NET 7.0** | **16**   | **2,177.4 ms** | **42.68 ms** | **70.12 ms** | **34000.0000** | **2441.72 MB** |
| Fnv1A     | Core70 | .NET 7.0 | 16   | 1,121.2 ms | 10.51 ms |  9.32 ms | 10000.0000 |  763.26 MB |
| XxHash64  | Core70 | .NET 7.0 | 16   |   909.6 ms | 17.82 ms | 28.27 ms | 14000.0000 | 1068.43 MB |
| XxHash128 | Core70 | .NET 7.0 | 16   |   825.1 ms | 16.01 ms | 22.96 ms | 15000.0000 | 1144.73 MB |
| Murmur3   | Core80 | .NET 8.0 | 16   | 2,094.1 ms | 31.54 ms | 27.96 ms | 27000.0000 | 2441.72 MB |
| Fnv1A     | Core80 | .NET 8.0 | 16   | 1,098.3 ms | 21.94 ms | 23.47 ms |  8000.0000 |  763.26 MB |
| XxHash64  | Core80 | .NET 8.0 | 16   |   903.3 ms | 16.75 ms | 17.20 ms | 12000.0000 | 1068.43 MB |
| XxHash128 | Core80 | .NET 8.0 | 16   |   835.4 ms | 15.32 ms | 12.79 ms | 13000.0000 | 1144.73 MB |

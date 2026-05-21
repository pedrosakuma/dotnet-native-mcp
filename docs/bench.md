# Benchmark Harness

`DotnetNativeMcp.Bench` is a [BenchmarkDotNet](https://benchmarkdotnet.org/) project that measures
the three core operations: **`find_native_callers`** (all three cache tiers), **`disassemble`**
(64-instruction window), and **`extract_strings`** (end-to-end over read-only data sections).

## Running locally

```bash
# 1. Build the SampleAot fixture (needed by the SampleAot input param).
dotnet build tests/DotnetNativeMcp.Core.Tests/ -c Release

# 2. Build the bench project.
dotnet build tests/bench/DotnetNativeMcp.Bench/ -c Release --no-restore

# 3. Run all benchmarks (full BDN default job — recommended for committed numbers).
dotnet run --project tests/bench/DotnetNativeMcp.Bench -c Release --no-build \
  -- --filter '*' --exporters json

# Quick smoke-test (short job — 3 iterations, not representative; do not use for published numbers).
dotnet run --project tests/bench/DotnetNativeMcp.Bench -c Release --no-build \
  -- --filter '*' --job short
```

Results are written to `BenchmarkDotNet.Artifacts/results/` in the working directory.
The CI `bench.yml` workflow uploads them as a GitHub Actions artifact named `benchmark-results`.

## CI

The benchmark CI workflow (`.github/workflows/bench.yml`) keeps `workflow_dispatch` and now also:

- updates the stored baseline on pushes to `main`
- runs on PRs only when the PR already carries the `perf` label
- limits PR/push runs to perf-relevant source and benchmark paths

BenchmarkDotNet JSON export stays command-line driven (`--exporters json`). The workflow feeds the
three `*-report-full-compressed.json` files into `benchmark-action/github-action-benchmark`, using a
`110%` regression threshold for mean time. It also bootstraps a local `gh-pages` branch when the repo
has no stored baseline yet, so the first `main` run can publish the baseline without manual setup.
Allocation regression gating remains out of scope for now.

## Cache tier methodology (FindNativeCallersBench)

| Tier | Description |
|------|-------------|
| Cold | Fresh `NativeCallGraphCache` instance **and** disk cache file deleted before each call. Measures full ELF scan + disk write. |
| WarmL2 | Fresh `NativeCallGraphCache` instance, disk cache pre-populated. Measures JSON deserialization from disk. |
| WarmL1 | Steady-state reuse of the same `NativeCallGraphCache` instance. Measures in-memory dictionary lookup. |

## Fixture inputs

| Param | Binary | Notes |
|-------|--------|-------|
| `SampleAot` | `tests/DotnetNativeMcp.Core.Tests/bin/Release/net10.0/fixtures/SampleAot/SampleAot` | Small NativeAOT ELF; built by `BuildNativeAotFixture` target inside Core.Tests. |
| `SystemPrivateCoreLib` | `tests/fixtures/SampleAot/bin/Release/net10.0/linux-x64/System.Private.CoreLib.dll` | Large R2R managed PE; produced alongside SampleAot publish. Not committed. Bench skips cleanly if absent. |

## Baseline numbers (v1 snapshot)

> **Hardware:** AMD EPYC 7763 @ 2.45 GHz, 16 logical / 8 physical cores, Linux Ubuntu 24.04.4 LTS  
> **Runtime:** .NET 10.0.5, X64 RyuJIT x86-64-v3  
> **SDK:** 10.0.201  
> **Job:** ShortRun (3 iterations) — indicative only; production numbers should use the default job.  
> **Commit:** `7e995190de25421b76404636140172d017a3dfc6` (pre-PR, branch `feat/bench-harness`)  
> ⚠ These are dev-machine / CI-runner numbers captured during development; treat as order-of-magnitude guidance, not hard SLOs.

### FindNativeCallersBench

| Method | Input | Mean | Allocated |
|--------|-------|-----:|----------:|
| Cold (no L1, no L2) | SampleAot | 12,830 ms | 4,142 MB |
| WarmL2 (no L1, disk cache) | SampleAot | 35 ms | 18.5 MB |
| WarmL1 (in-memory) | SampleAot | 31 ns | 24 B |
| Cold (no L1, no L2) | SystemPrivateCoreLib | 412 µs | 3.4 KB |
| WarmL2 (no L1, disk cache) | SystemPrivateCoreLib | 21 µs | 3.3 KB |
| WarmL1 (in-memory) | SystemPrivateCoreLib | 24 ns | 24 B |

### DisassembleBench (64-instruction window)

| Method | Input | Mean |
|--------|-------|-----:|
| Disassemble 64 instructions | SampleAot | 8,382 µs |
| Disassemble 64 instructions | SystemPrivateCoreLib | 113 ns |

### ExtractStringsBench (end-to-end over read-only sections)

| Method | Input | Mean |
|--------|-------|-----:|
| ExtractStrings end-to-end | SampleAot | 1,064 µs |
| ExtractStrings end-to-end | SystemPrivateCoreLib | 14,648 µs |

> Results will not be refreshed on every release — refresh on dedicated hardware when a perf-sensitive change lands.

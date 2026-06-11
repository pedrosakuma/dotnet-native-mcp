# Fuzzing the native binary parsers

## Approach

`dotnet-native-mcp` uses **property-based smoke tests** (deterministic PRNG seeds)
rather than a coverage-guided fuzzer (SharpFuzz / libFuzzer). The tests live in the
normal xunit test project so they run as part of `dotnet test` without any extra
toolchain setup.

Each harness:
1. Generates `1 000` pseudorandom byte arrays per PRNG seed (three seeds per harness).
2. Asserts the parser **never throws** an unhandled exception.
3. Asserts all iterations complete within a **60-second wall-clock budget** (guards
   against infinite loops and catastrophic backtracking).

The "never throws" contract is load-bearing: tools surface malformed-input failures as
`NativeResult.Fail` (or a `null`/empty result), never as a thrown exception. Any harness
that catches a throw is reporting a real hardening bug in the parser, not a test defect.

### Parsers covered

| Parser | Input shape | Source |
|---|---|---|
| `PeNativeReader.Read` | raw bytes + file path | `src/DotnetNativeMcp.Core/Imaging/PeNativeReader.cs` |
| `ElfReader.Read` | raw bytes + file path | `src/DotnetNativeMcp.Core/Imaging/ElfReader.cs` |
| `MachOReader.Read` | raw bytes + file path | `src/DotnetNativeMcp.Core/Imaging/MachOReader.cs` |
| `MachOReader.ParseFatSlice` | raw bytes | `src/DotnetNativeMcp.Core/Imaging/MachOReader.cs` |
| `MachOReader.CheckUnsupportedFeatures` | raw bytes | `src/DotnetNativeMcp.Core/Imaging/MachOReader.cs` |
| `ElfReader.ReadImported{Functions,Libraries}`, `ResolvePltEntries` | `NativeImage` (`Elf`) with random raw bytes | `src/DotnetNativeMcp.Core/Imaging/ElfImportReader.cs`, `ElfPltResolver.cs` |
| `PeNativeReader.ReadImported{Functions,Libraries}` | `NativeImage` (`Pe`) with random raw bytes | `src/DotnetNativeMcp.Core/Imaging/PeImportReader.cs` |
| `MachOReader.ReadImported{Functions,Libraries}`, `ResolveStubEntries`, `ReadExports` | `NativeImage` (`MachO`) with random raw bytes | `src/DotnetNativeMcp.Core/Imaging/MachOImportReader.cs`, `MachOCrossImageReader.cs` |
| `ReadyToRunReader.*` (header + every section reader) | `NativeImage` (`Pe`) with random raw bytes | `src/DotnetNativeMcp.Core/R2R/ReadyToRunReader.cs` |
| `StringExtractor.Extract` | raw bytes | `src/DotnetNativeMcp.Core/Strings/StringExtractor.cs` |
| `NativeAotSymbolDemangler.{Demangle,LooksLikeNativeAotMangled,Classify}` | mangling-biased random strings | `src/DotnetNativeMcp.Core/Symbols/NativeAotSymbolDemangler.cs` |
| `MapFileReader.TryMerge` | file path (bytes written to scratch dir) | `src/DotnetNativeMcp.Core/Imaging/MapFileReader.cs` |
| `DgmlReader.Read` | file path (bytes written to scratch dir) | `src/DotnetNativeMcp.Core/Dgml/DgmlReader.cs` |
| `DwarfLineReader.Read` | `NativeImage` with `.debug_line` section | `src/DotnetNativeMcp.Core/Symbols/DwarfLineReader.cs` |
| `DwarfInfoReader.TryGetSignatureForRva` | `NativeImage` with `.debug_info`/`.debug_abbrev` | `src/DotnetNativeMcp.Core/Symbols/DwarfInfoReader.cs` |
| `MstatReader.Read` | file path (bytes written to scratch dir) | `src/DotnetNativeMcp.Core/Mstat/MstatReader.cs` |
| `SourceLinkResolver.TryLoadFromBytes` | raw bytes | `src/DotnetNativeMcp.Core/Symbols/SourceLinkResolver.cs` |
| `EmbeddedPdbExtractor` | raw bytes (MZ-prefixed) | `src/DotnetNativeMcp.Core/Symbols/EmbeddedPdbExtractor.cs` |

The `DwarfLineReader` and `DwarfInfoReader` harnesses also contain dedicated zlib-bomb
tests that craft a syntactically valid `Elf64_Chdr` declaring a decompressed size just
above the 256 MiB guard introduced in issue #48, ensuring the reader rejects it promptly.

### Random bytes vs. mutation fuzzing

Random bytes almost always fail the format's magic/header check and return early, so they
only exercise *shallow* code. To reach the dangerous **offset-chasing** logic (corrupted
size/count/RVA fields read from an otherwise well-formed binary), `MutationFuzz_RealFixtures_NeverThrows`
loads each real test fixture (the NativeAOT `SampleAot` ELF, the Mach-O `.o` fixtures, and
the managed `EmbeddedPdb.dll` PE), applies 1–16 random byte mutations while keeping the
header valid, then re-runs the full pipeline (`Read` → import/PLT/stub/export readers →
every R2R section reader → symbol demangling). It uses `500` iterations per seed and is
skipped automatically when fixtures are not built.

This harness found four real hardening bugs on its first run, all since fixed:
`NativeAotSymbolDemangler.Demangle` (unbalanced `<` overran the string),
`PeNativeReader.Read` (lazy `PEReader` header parsing threw `BadImageFormatException`
past the constructor's `try/catch`), `ReadyToRunReader.RvaToFileOffset` (uint→int overflow
yielded a negative offset that slipped past the caller's bounds check), and the ELF import
readers (`checked((int)nameOffset)` threw `OverflowException` on a huge string-table index).

## Running locally

```bash
# Run all fuzz smoke tests
dotnet test tests/DotnetNativeMcp.Core.Tests \
  --configuration Release \
  --filter "FullyQualifiedName~DotnetNativeMcp.Core.Tests.Fuzz"
```

## Reproducing a failure

Because the seeds are deterministic, a failing test can be reproduced exactly:

```bash
dotnet test tests/DotnetNativeMcp.Core.Tests \
  --configuration Release \
  --filter "FullyQualifiedName~ParserFuzz"
```

The seed value is shown in the xunit test name (e.g.
`ElfReader_RandomBytes_NeverThrows(seed: 42)`). To reproduce a specific iteration:

```csharp
var rng = new Random(42);          // same seed
for (int i = 0; i < 137; i++)     // skip to the failing iteration
    GenerateRandomBytes(rng);
var bytes = GenerateRandomBytes(rng); // this is the crashing input
```

## Adding a new harness

1. Identify the public entry point in `DotnetNativeMcp.Core` that you want to fuzz.
2. Add a `[Theory] [InlineData(0)] [InlineData(42)] [InlineData(unchecked((int)0xDEAD_BEEF))]`
   test method in `tests/DotnetNativeMcp.Core.Tests/Fuzz/ParserFuzzTests.cs`.
3. Inside the loop, call `GenerateRandomBytes(rng)` to obtain a random input, feed it
   to the parser, and assert it does not throw.
4. Run the tests locally to confirm all iterations pass before pushing.

## CI

The workflow at `.github/workflows/fuzz.yml` runs the fuzz smoke tests on every push
to `main` and on every pull request. On failure the `.trx` results file is uploaded
as an artifact (`fuzz-results`) with a 30-day retention window, giving reviewers a
structured view of which seed and iteration triggered the failure.

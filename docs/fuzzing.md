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

### Parsers covered

| Parser | Input shape | Source |
|---|---|---|
| `PeNativeReader.Read` | raw bytes + file path | `src/DotnetNativeMcp.Core/Imaging/PeNativeReader.cs` |
| `ElfReader.Read` | raw bytes + file path | `src/DotnetNativeMcp.Core/Imaging/ElfReader.cs` |
| `MachOReader.Read` | raw bytes + file path | `src/DotnetNativeMcp.Core/Imaging/MachOReader.cs` |
| `DwarfLineReader.Read` | `NativeImage` with `.debug_line` section | `src/DotnetNativeMcp.Core/Symbols/DwarfLineReader.cs` |
| `MstatReader.Read` | file path (bytes written to scratch dir) | `src/DotnetNativeMcp.Core/Mstat/MstatReader.cs` |
| `SourceLinkResolver.TryLoadFromBytes` | raw bytes | `src/DotnetNativeMcp.Core/Symbols/SourceLinkResolver.cs` |

The `DwarfLineReader` harness also contains a dedicated test
(`DwarfLineReader_ZlibBombHeader_NeverThrowsAndCompletesQuickly`) that crafts a
syntactically valid `Elf64_Chdr` header declaring a decompressed size just above the
256 MiB guard introduced in issue #48, ensuring the reader rejects it promptly.

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

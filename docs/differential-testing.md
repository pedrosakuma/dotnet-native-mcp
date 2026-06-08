# Differential testing the native binary readers

## Approach

The fuzz harness (`docs/fuzzing.md`) proves the parsers **never throw** on hostile input.
It says nothing about whether the parsed result is *correct*. The differential
("oracle") harness closes that gap: it parses the same binary with `dotnet-native-mcp`
**and** with an independent, battle-tested reference tool, then asserts the two agree.

The tests live in the normal xunit project so they run as part of `dotnet test` with no
extra toolchain setup. They **no-op (pass) when the reference tool is missing or the
fixture is unbuilt**, so the suite stays green on hosts without binutils — CI guarantees
the real comparison runs (see [CI](#ci)).

### Surfaces covered

| Reader | Reference oracle | Compared properties | Source |
|---|---|---|---|
| `ElfReader` symbols | GNU `readelf -sW` | per-index name, value (RVA), size, function flag, and total named-symbol count | `tests/DotnetNativeMcp.Core.Tests/ElfSymbolDifferentialTests.cs` |
| `ElfReader` sections | GNU `readelf -SW` | per-name virtual address, file offset, and size (for every emitted section) | `tests/DotnetNativeMcp.Core.Tests/ElfSectionDifferentialTests.cs` |
| `ElfReader.ReadImportedLibraries` | GNU `readelf -dW` | the set of `DT_NEEDED` shared libraries | `tests/DotnetNativeMcp.Core.Tests/ElfImportDifferentialTests.cs` |
| `ElfReader.ReadImportedFunctions` | GNU `readelf -sW` | the multiset of undefined (`UND`) `.dynsym` symbol names | `tests/DotnetNativeMcp.Core.Tests/ElfImportDifferentialTests.cs` |

The reference oracle is the shared `ReadelfOracle` helper
(`tests/DotnetNativeMcp.Core.Tests/ReadelfOracle.cs`), which shells out to `readelf` and
parses its wide (`-W`) output. The symbol oracle mirrors `ElfReader`'s table preference —
`.symtab` when present, otherwise `.dynsym` — so the two read the same symbol table.

#### Comparison strategy per surface

- **Symbols** — exact 1:1 by table index (name, value, size, function flag) plus a
  named-symbol count check.
- **Sections** — per-name geometry match for every section `ElfReader` emits, with **no**
  count assertion: `ElfReader` intentionally drops the NULL section and any section with a
  zero file offset/size or whose bytes fall outside the file (`SHT_NOBITS`, e.g. `.bss`).
  The invariant is that whatever it surfaces is byte-for-byte correct. For `SHT_NOBITS`
  sections the `FileSize` is not checked (they occupy no file bytes, so readelf's `sh_size`
  is not a meaningful file-size oracle); duplicate section names fall back to a
  geometry-existence match.
- **Imports** — multiset-equivalent (order-independent, duplicate-aware) comparison of the
  `DT_NEEDED` library set and of the undefined `.dynsym` symbol names.

> Radix gotcha: `readelf -sW` prints symbol **Size in decimal**, while `readelf -SW` prints
> section **Address/Off/Size in hex**. `ReadelfOracle` parses each accordingly.

### Fixtures exercised

| Fixture | Why |
|---|---|
| `SampleAot` (NativeAOT ELF) | Clean `.symtab` with no SECTION/FILE symbols — enables an exact 1:1 comparison. |
| `/usr/bin/cat` | Stock, usually-stripped system binary — exercises the `.dynsym` fallback and the `@GLIBC_x.y` version-suffix normalization. |

### Normalization notes

Symbol versioning is represented inconsistently across tables, which the harness
normalizes away before comparing names:

- In a linked `.symtab` the `@VER` suffix is baked into `st_name`, so `ElfReader` returns
  it verbatim (e.g. `tcsetattr@GLIBC_2.2.5`).
- For `.dynsym`, `st_name` is the bare base name and `readelf` *synthesizes* the
  `@VER (N)` display from `.gnu.version`.

Both sides are reduced to the version-independent base name (everything before `@`)
so the comparison is table-agnostic. Value, size, and the function flag are compared
without normalization.

## Running locally

```bash
# Build the fixture + test project, then run the differential harness.
dotnet build tests/DotnetNativeMcp.Core.Tests/ -c Release
dotnet test tests/DotnetNativeMcp.Core.Tests/ \
  --configuration Release --no-build \
  --filter "FullyQualifiedName~Differential"
```

Requires GNU binutils (`readelf`) on `PATH`:

```bash
sudo apt-get update && sudo apt-get install -y binutils
```

## Reproducing a failure

A failure prints the first 25 divergences inline, each tagged with the symbol index, e.g.:

```
index 8413: name 'tcsetattr' != readelf 'tcsetattr'
index 42 'Foo': rva 0x1000 != readelf 0x2000
```

To inspect the reference output for a given index directly:

```bash
readelf -sW <binary> | awk '/Symbol table/{t=$3} $1=="42:"{print t, $0}'
```

## Adding a new oracle harness

1. Pick a reader surface in `DotnetNativeMcp.Core` and a trusted reference tool that
   reports the same facts (`readelf`, `nm`, `objdump`, `addr2line`, `llvm-readobj`, …).
2. Add a wrapper that shells out to the tool and parses its output into a comparable
   shape. Make it return `null` when the tool is absent (catch `Win32Exception`) so the
   test skips cleanly.
3. Compare **stable, semantic** properties (addresses, sizes, names), and normalize away
   tool-specific display artifacts (version suffixes, demangling, radix).
4. Aggregate divergences into a list and assert it is empty, so one run reports *all*
   mismatches rather than failing on the first.

## CI

The differential tests run inside the standard `dotnet test` step of `.github/workflows/ci.yml`.
`ubuntu-latest` ships `readelf` preinstalled and the workflow builds the NativeAOT fixture,
so the comparison executes for real on every push and pull request — it is not silently
skipped. The existing "Verify NativeAOT fixture outputs" guard fails the build if the
fixture did not compile, which would otherwise let the `SampleAot` comparison skip unnoticed.

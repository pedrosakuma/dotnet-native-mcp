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
| `PeNativeReader` sections | LLVM `llvm-readobj --sections` | per-name virtual address, virtual size, file offset, and file size | `tests/DotnetNativeMcp.Core.Tests/PeSectionDifferentialTests.cs` |

The reference oracle is the shared `ReadelfOracle` helper
(`tests/DotnetNativeMcp.Core.Tests/ReadelfOracle.cs`), which shells out to `readelf` and
parses its wide (`-W`) output. The symbol oracle mirrors `ElfReader`'s table preference —
`.symtab` when present, otherwise `.dynsym` — so the two read the same symbol table. The
disassembly oracle (`ObjdumpOracle`) shells out to `objdump`, and the PE oracle
(`LlvmReadobjOracle`) shells out to `llvm-readobj`. All share the safe process runner in
`OracleProcess` (concurrent stdout/stderr drain + timeout + missing-tool skip).

> Mach-O is not yet covered: the `MachOReader` tests synthesize bytes in-process and the repo
> has no real Mach-O fixture on disk to point an oracle at. The `LlvmReadobjOracle` helper
> generalizes to Mach-O once such a fixture exists.

#### Disassembly

| Decoder | Reference oracle | Compared properties | Source |
|---|---|---|---|
| `IcedDisassembler` (x86/x64, via `RawDisassembler`) | GNU `objdump -d -M intel` | per-address instruction boundary + raw bytes (hard); mnemonic (soft) | `tests/DotnetNativeMcp.Core.Tests/ElfDisassemblyDifferentialTests.cs` |

The hard oracle is **instruction-boundary + raw-byte** agreement: two independent decoders
walking the same bytes must segment them identically, so a mismatch means one of them
mis-sized an instruction — the most dangerous class of decoder bug, invisible to the
"never throws" fuzz harness. The harness disassembles each function symbol in `.text`
(bounded by size) both ways and asserts every Iced instruction has an `objdump` instruction
at the same address with identical bytes.

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
- **Disassembly** — per-address boundary + raw-byte equality (hard). Mnemonics are compared
  after normalizing `objdump`'s display: leading segment/`rep`/`lock`/`REX` prefix tokens are
  stripped, `movabs` is mapped to Iced's `mov`, and the `nop`/`xchg ax,ax` NOP family is
  treated as equivalent. Operand text is **not** compared (formatting differs by design).
- **PE sections** — exact per-name geometry match (virtual address, virtual size, file offset,
  file size) for every section, plus a same-section-name-set check. `PeNativeReader` emits the
  full COFF section table with no filtering, so unlike the ELF section comparison this asserts
  the complete set. Duplicate section names fall back to a geometry-existence match.

> Radix gotcha: `readelf -sW` prints symbol **Size in decimal**, while `readelf -SW` prints
> section **Address/Off/Size in hex**. `ReadelfOracle` parses each accordingly. `objdump`
> prints instruction addresses and bytes in hex. `llvm-readobj` prints section addresses and
> offsets in hex (`0x`-prefixed) but sizes in decimal; `LlvmReadobjOracle` keys off the `0x`
> prefix per field.

### Fixtures exercised

| Fixture | Why |
|---|---|
| `SampleAot` (NativeAOT ELF) | Clean `.symtab` with no SECTION/FILE symbols — enables an exact 1:1 comparison. It is also the disassembly fixture: its `.text` function symbols give well-bounded code ranges to decode both ways. |
| `/usr/bin/cat` | Stock, usually-stripped system binary — exercises the `.dynsym` fallback and the `@GLIBC_x.y` version-suffix normalization. |
| `DotnetNativeMcp.Core.dll` | The Core assembly itself — a managed PE always present beside the test binary, so the PE section comparison runs everywhere instead of skipping on a missing fixture. |
| `System.Private.CoreLib.dll` (ReadyToRun PE) | Published alongside `SampleAot`; a real R2R PE — the actual asm-mcp → native-mcp handoff target — exercising the PE section reader on a non-trivial binary. |

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

Requires GNU binutils (`readelf`, `objdump`) and LLVM (`llvm-readobj`) on `PATH`:

```bash
sudo apt-get update && sudo apt-get install -y binutils llvm
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
`ubuntu-latest` ships `readelf` and `objdump` preinstalled, and the workflow's toolchain step
installs `llvm` (for `llvm-readobj`) and builds the NativeAOT fixture, so the comparisons execute
for real on every push and pull request — they are not silently skipped. The existing "Verify
NativeAOT fixture outputs" guard fails the build if the fixture did not compile, which would
otherwise let the `SampleAot` comparison skip unnoticed. The PE section comparison additionally
runs against the always-present Core assembly, so it cannot skip even if a fixture is missing.

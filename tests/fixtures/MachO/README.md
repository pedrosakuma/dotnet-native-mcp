# Mach-O fixtures

Tiny, committed Mach-O **object files** (`.o`) used by the Mach-O section
differential harness (`MachOSectionDifferentialTests`). They are checked in
(rather than built at test time) so the harness never depends on a macOS
cross-toolchain being present locally — only the oracle (`llvm-readobj`)
is needed, and the test skips cleanly when it is absent.

Object files are used deliberately: `MachOReader` rejects
`LC_DYLD_CHAINED_FIXUPS`, which the linker injects into modern linked
dylibs/executables. A relocatable object (`.o`) never carries chained
fixups, so it round-trips through `MachOReader` while still being a real,
multi-section, multi-architecture Mach-O binary.

| File             | Arch    | Sections                                  |
|------------------|---------|-------------------------------------------|
| `macho-x64.o`    | x86_64  | `__TEXT,__text` `__DATA,__data` `__TEXT,__cstring` |
| `macho-arm64.o`  | arm64   | `__TEXT,__text` `__DATA,__data` `__TEXT,__cstring` |

## Regenerating

The `.s` sources are committed alongside the objects. Regenerate with the
LLVM integrated assembler (no macOS SDK required):

```bash
llvm-mc -triple=x86_64-apple-darwin -filetype=obj macho-x64.s   -o macho-x64.o
llvm-mc -triple=arm64-apple-darwin  -filetype=obj macho-arm64.s -o macho-arm64.o
```

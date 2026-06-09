# Handoff Contract — `dotnet-diagnostics-mcp` → `dotnet-native-mcp`

> Wire format for native frames produced by `dotnet-diagnostics-mcp` (NativeAOT
> attach paths) and consumed by `dotnet-native-mcp`. Mirrors the structure of
> [`dotnet-assembly-mcp/docs/handoff-contract.md`](https://github.com/pedrosakuma/dotnet-assembly-mcp/blob/main/docs/handoff-contract.md).

## Why this document exists

Three independent servers share a tooling pipeline. To keep them honest:

1. The producer (`dotnet-diagnostics-mcp`) commits to emitting a typed payload.
2. The consumer (`dotnet-native-mcp`) commits to accepting and resolving it.
3. Neither side can silently change shape without breaking the LLM's ability
   to chain the two.

When this document and the producer's emission disagree, the producer is the
authority — open an issue here and update the contract.

## The handle: `NativeFrame`

```jsonc
{
  // Required. Absolute path to the native binary on the producer's host. May
  // be inside a container; consumers running outside that container resolve
  // it via the assemblyPathHint pattern (see §3).
  "binary": "/app/MyService",

  // Required. Mangled symbol as observed in the symbol table / perf.data /
  // ETW callstack. ILC mangling looks like:
  //   S_P_CoreLib_System_String__Concat
  //   S_P____MyApp_Program__<Main>g__Foo|0_1
  // Capstone-emitted ARM64 symbols and ELF .symtab entries are accepted as-is.
  "symbol": "S_P_CoreLib_System_String__Concat",

  // Optional but recommended. Virtual address of the instruction the producer
  // is pointing at. Hex string, no 0x prefix in transport. When present the
  // consumer SHOULD center its disassembly window here.
  "address": "00000000004012a0",

  // Optional. Module load base when the producer observed the frame. Lets the
  // consumer rebase absolute addresses if the binary was loaded with ASLR.
  "loadBase": "0000000000400000",

  // Optional. Build-id (ELF) / PDB GUID-Age (PE) so the consumer can refuse
  // to operate on a binary whose contents have drifted from what the producer
  // saw. Format: lowercase hex, no separators.
  "buildId": "d8f3a4b1c2e5f607"
}
```

## Resolution flow (consumer side)

```
NativeFrame
  ├─ load_native_binary(binary)                  -> ImageHandle
  ├─ import_native_manifest(entries[], mode?)    -> per-entry outcomes (bulk handshake)
  ├─ resolve_symbols(image, [address, ...])       -> [{ demangled, section, displacement }]
  ├─ find_native_callers(image, target)          -> [{ callSite, mnemonic, source? }]
  └─ disassemble(image, address, n)              -> List<NativeInstruction>
```

`resolve_symbols` is the batch variant: pass up to 200 hex or decimal address strings in a
single call. Per-address failures are reported inline without aborting the whole batch.

When `dotnet-diagnostics-mcp` emits a manifest of all native images it saw in a process,
use `import_native_manifest` (not `load_native_binary`) to register them in bulk. Lazy mode
(default) records path hints without opening files — actual loading is deferred until a tool
call requires the handle. Eager mode opens and verifies every entry immediately.

## Path hints are untrusted

Every path-shaped field that arrives off the wire — the binary `path` on
`load_native_binary` / `import_native_manifest`, `imagePath` and `ilMapPath` on
`disassemble`, and the `mstatPath` / `dgmlPath` sidecar overrides on
`get_size_breakdown` / `explain_retention` — is a **best-effort display hint**,
not an authenticated assertion that the file at that path is safe to open. The
LLM sits in the middle of every handoff and a compromised or mistaken model can
swap a path for anything reachable on the consumer host (`/etc/shadow`, an
attacker-staged binary under `/tmp`, a `..`-traversal, a symlink pointing
outside a mounted volume).

This server therefore treats path hints as untrusted (per the upstream contract
amendment, `dotnet-diagnostics-mcp#168`):

1. **Canonicalise** — every path is resolved with `Path.GetFullPath` **and** has
   its reparse points (POSIX symlinks, NTFS junctions) followed to the true
   on-disk target before any policy check (`PathCanonicalizer.ResolveRealPath`).
2. **Allowlist** — when enforcement is enabled the canonical path must sit under
   one of the configured trusted roots, using a boundary-aware containment check
   (so `/binaries-secret` is **not** under `/binaries`). Anything outside is
   refused with `path_not_allowed` and the rejected canonical path is surfaced.
3. **Verify build-id** — `load_native_binary` / `import_native_manifest` (eager)
   still verify the on-disk build-id against the producer-supplied `buildId`
   before caching, refusing mismatches with `binary_mismatch` / `build_id_mismatch`.

### Enforcement is opt-in

The allowlist activates once an **operator** configures at least one trusted
root. Until then the server is permissive (it still canonicalises, so downstream
always sees the resolved path) and logs a one-time startup warning. Configure
roots via any of:

- `NativeMcp:AllowedBinaryRoots` — JSON/array of root directories.
- `NATIVE_MCP_ALLOWED_ROOTS` — `PATH`-separator-delimited list (`:` POSIX, `;` Windows).
- `BINARIES_DIR` — single root (the same variable the Docker Compose tier uses).

When enforcing, a set of well-known roots is always allowed in addition to the
operator roots so handed-off binaries keep resolving: the NuGet global packages
cache, the .NET shared-framework / runtime install, and the system temp
directory (where sidecar producers stage `ilmap` / captured-byte artifacts).

## R2R handoff from `dotnet-assembly-mcp`

ReadyToRun (R2R) method bodies live inside ordinary managed PEs — the same `.dll` files that
`dotnet-assembly-mcp` inspects for metadata and IL. When `dotnet-assembly-mcp` encounters a
method whose IL was compiled to native R2R code, it can emit the RVA and byte length of that
body, then hand off to **`dotnet-native-mcp`** for disassembly without requiring the PE to
pass the `load_native_binary` validation (which rejects managed PEs that lack NativeAOT
marker symbols or an R2R header that this server can detect).

### Raw-bytes disassembly (`imagePath` + `rva` + `size`)

`disassemble` accepts two mutually exclusive modes:

| Parameter group | When to use |
|---|---|
| `imageHandle` (+ `address` or `symbolName`) | Binary is already registered via `load_native_binary`. Supports symbol lookup and xref hints. |
| `imagePath` + `rva` + `size` | Direct file path, no prior `load_native_binary` call needed. Works on any PE/ELF/Mach-O including managed PEs. No xref resolution. |

**Wire example — `dotnet-assembly-mcp` → `dotnet-native-mcp` R2R handoff:**

```jsonc
// dotnet-assembly-mcp emits a method body location like this:
// {
//   "file": "/app/MyService.dll",
//   "methodRva": 139776,        // 0x22200 — decimal RVA of the R2R native code
//   "nativeCodeSize": 128       // byte length of the compiled body
// }

// Tool: disassemble (raw-bytes mode)
{
  "imagePath": "/app/MyService.dll",
  "rva": 139776,
  "size": 128,
  "maxInstructions": 64
}
// -> {
//   "summary": "Disassembled 42 instruction(s) at RVA 0x22200 in 'raw-...'.",
//   "data": [
//     { "addressHex": "0000000000022200", "bytes": "55", "mnemonic": "push", "operands": "rbp" },
//     { "addressHex": "0000000000022201", "bytes": "4889e5", "mnemonic": "mov", "operands": "rbp, rsp" },
//     ...
//   ]
// }
```

**Validation rules for `imagePath` mode:**

- Exactly one of `{imageHandle, imagePath}` must be supplied → else `invalid_argument`.
- When `imagePath` is supplied, `rva` and `size` are both required → else `invalid_argument`.
- `architecture` is optional; detected from the PE/ELF/Mach-O header when omitted.
- `baseAddress` is optional; used to format absolute addresses in output; detected from
  the binary header when omitted.
- `architecture` and `baseAddress` are ignored when `imageHandle` is supplied.

When using `imagePath`, the resulting instructions **will not** have symbolic cross-ref
hints resolved (no symbol table is available without a registered image).

### Raw-blob IL map sidecar (`ilMapPath`)

`disassemble(rawBlob=true)` accepts an optional `ilMapPath` sidecar for producer-supplied
IL-to-native mappings. This is the consumer side of the JIT handoff; emitting the `.ilmap`
file from `dotnet-diagnostics-mcp.capture_method_bytes` is tracked separately on the producer.

**Format**

- Plain text, UTF-8, no BOM.
- Optional magic/version header on the first non-empty line: `# ilmap v1`
  - `v1` is the only supported version today.
  - Unknown versions fail fast with `invalid_argument`.
  - Files without the header are treated as v1 for backwards compatibility.
- One mapping per line: `<nativeOffsetHex>\t<ilOffsetHex>\n`
- `nativeOffsetHex` is the byte offset from the start of the raw blob, lowercase hex, no `0x`.
- `ilOffsetHex` is lowercase hex, no `0x`, or one of the sentinel values `prolog`, `epilog`, `noinfo`.
- Lines starting with `#` are comments.
- The file may be unsorted; the consumer sorts by native offset before building ranges.

Each entry covers the byte range from its native offset up to (but not including) the next
entry's native offset. `InstructionView.ilOffset` is populated by looking up the instruction
start offset within those ranges. The emitted `ilOffset` value uses lowercase hex without `0x`
for ordinary IL offsets, or the literal sentinel string for `prolog`, `epilog`, and `noinfo`.

**Example**

```text
# ilmap v1
# JIT IL-to-native map for a captured raw blob
0	prolog
2	0
5	a
7	noinfo
```

`ilMapPath` is only valid with `rawBlob=true`; other disassembly modes return `invalid_argument`.

### `resolveSource` parameter

Three tools surface a `SourceLocation` (file + line from DWARF/PDB debug info).
All three expose a `resolveSource: bool` parameter to control whether PDB I/O is performed.

| Tool                  | Default       | Rationale                                                                         |
|-----------------------|---------------|-----------------------------------------------------------------------------------|
| `resolve_symbols`     | `true`        | User explicitly asked to resolve symbols; file:line is the natural next step.     |
| `find_native_callers` | `true`        | Caller context with file:line is high-value; opt out for perf-sensitive scans.    |
| `disassemble`         | `false`       | Instruction streams can be thousands of lines; per-instruction lookup defaults off.|

Set `resolveSource=false` when scanning large binaries where PDB reads are slow.

### Worked example

```jsonc
// Step 1 — load the binary (once per binary, reuse the handle)
// Tool: load_native_binary
{ "path": "/app/MyService", "buildId": "d8f3a4b1c2e5f607" }
// -> { "summary": "Loaded NativeAOT ELF...", "data": { "imageHandle": "i:d8f3a4b1c2e5f607:3e9c" } }

// Step 2 — resolve addresses from the hotspot report
// Tool: resolve_symbols
{
  "imageHandle": "i:d8f3a4b1c2e5f607:3e9c",
  "addresses": ["0x4012a0", "0x401380", "0xdeadbeef"]
}
// -> {
//   "summary": "Resolved 2 of 3 addresses (1 error) in 'i:d8f3a4b1c2e5f607:3e9c'.",
//   "data": {
//     "rows": [
//       { "inputAddress": "0x4012a0", "mangledName": "S_P_CoreLib_System_String__Concat",
//         "demangledName": "System.String.Concat", "sectionName": ".text", "displacement": 0, "error": null },
//       { "inputAddress": "0x401380", "mangledName": "S_P____MyApp_Program__Main",
//         "demangledName": "MyApp.Program.Main", "sectionName": ".text", "displacement": 12, "error": null },
//       { "inputAddress": "0xdeadbeef", "mangledName": null, "demangledName": null,
//         "sectionName": null, "displacement": null, "error": "address_out_of_range" }
//     ]
//   }
// }

// Step 3 — optional: disassemble a hot symbol
// Tool: disassemble
{ "imageHandle": "i:d8f3a4b1c2e5f607:3e9c", "address": "4012a0", "maxInstructions": 32 }
```

The consumer MUST NOT trust the path verbatim across container boundaries —
treat `binary` as a hint, verify on `buildId` when present, otherwise return a
typed `binary_mismatch` error so the producer (or the LLM) can retry with a
different path.

## Error kinds (initial set)

| Kind                          | Meaning                                                             |
|-------------------------------|---------------------------------------------------------------------|
| `binary_not_found`            | Path doesn't resolve on the consumer's host.                        |
| `binary_mismatch`             | `buildId` disagrees with on-disk binary.                            |
| `not_a_native_dotnet_image`   | The binary opened, but isn't a managed-flavored native build.       |
| `symbol_not_found`            | Symbol not in `.map` or `.symtab`.                                  |
| `address_out_of_range`        | Address is not inside any known section.                            |
| `mstat_not_found`             | No paired `.mstat` sidecar could be located.                        |
| `dgml_not_found`              | No paired `.dgml` sidecar could be located.                         |
| `disassembly_unsupported`     | Architecture not supported in this version (e.g. ARM64 pre-V1).     |
| `invalid_argument`            | A supplied argument value was invalid (bad format, out of range).   |
| `path_not_allowed`            | Canonicalised path resolved outside the configured trusted-root allowlist (see "Path hints are untrusted"). |
| `internal_error`              | An unexpected internal failure occurred.                            |

Once a kind is published it is **never** repurposed. Add new kinds instead.

## Versioning

This contract is versioned via the `nativeFrameContractVersion` field on the
producer side. The consumer accepts any version it explicitly supports; older
or unknown versions fall back to best-effort. Bump on **any** shape change.

Current version: **1** (V0 shipped; all fields in the `NativeFrame` object are stable).

## Cross-links

- Companion producer: <https://github.com/pedrosakuma/dotnet-diagnostics-mcp>
- Companion managed-handoff target: <https://github.com/pedrosakuma/dotnet-assembly-mcp>
- Tool budget + response conventions: [`docs/mcp-conventions.md`](./mcp-conventions.md)

## Xref disk cache

The `find_native_callers` tool builds a full call-graph index on first use.
This index is persisted under `~/.cache/dotnet-native-mcp/<build-id>.xref`
(format: 4-byte magic `NXR1` + 4-byte little-endian version + JSON body)
so subsequent sessions skip the scan entirely. The build-id from the
`NativeFrame.buildId` field is used as the cache key, giving per-binary
stable identity across rebuilds that don't change content.

Set `DOTNET_NATIVE_MCP_XREF_CACHE=0` to disable disk I/O (CI, read-only mounts).
Clear with `rm -rf ~/.cache/dotnet-native-mcp/`; no automated eviction.

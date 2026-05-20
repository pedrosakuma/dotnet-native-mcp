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
  ├─ resolve_symbols(image, [address, ...])       -> [{ demangled, section, displacement }]
  └─ disassemble(image, address, n)              -> List<NativeInstruction>
```

`resolve_symbols` is the batch variant: pass up to 200 hex or decimal address strings in a
single call. Per-address failures are reported inline without aborting the whole batch.

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

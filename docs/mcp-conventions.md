# MCP Conventions — `dotnet-native-mcp`

> Mirror of the conventions in `dotnet-assembly-mcp/docs/mcp-conventions.md`
> and `dotnet-diagnostics-mcp/docs/mcp-conventions.md`. Drift between the
> three is the cost we are explicitly trying to avoid.

## 1. Transport

Two modes, same tool set:

- **stdio** — `--stdio` or `NATIVE_MCP_TRANSPORT=stdio`. JSON-RPC over
  STDIN/STDOUT; every log goes to STDERR.
- **HTTP streamable** — default. `/mcp` route on `127.0.0.1:8789`.

## 2. Tool budget

**Hard cap: 10 tools.** When in doubt, expose new capability as a parameter on
an existing tool or as an MCP Resource, not a new tool. The cap exists because
LLMs degrade rapidly as the tool surface grows.

### 2.1 What goes where

- **Tier 1 (cheap, list/walk):** `list_*`, `get_*`, `resolve_*`. Must be
  O(symbols) or better. Must not disassemble.
- **Tier 2 (medium, structural):** `find_*`. Walks references / xrefs.
- **Tier 3 (expensive, per-target):** `disassemble`. Called only on demand,
  never as part of a list walk.

Never disassemble in a Tier-1 path.

## 3. Response shape

All tools return a `NativeResult<T>` envelope (mirrors `AssemblyResult<T>` in
`dotnet-assembly-mcp`):

```jsonc
{
  "summary": "Resolved 3 of 3 addresses in apphost",
  "data": { /* typed payload */ },
  "hints": [
    {
      "nextTool": "disassemble",
      "reason": "Disassemble the hot frame to inspect the native code.",
      "suggestedArguments": { "imageHandle": "i:d8f3...:42a1", "address": "004012a0" }
    }
  ],
  "error": null
}
```

- `summary` is a compact, user-facing description suitable for chat output.
- `data` is the typed payload for the tool.
- `hints` is an optional list of `NextActionHint` values and is a first-class
  part of the contract; callers should preserve and surface these hints.
- `error` is either `null` or a structured error object with a stable `kind`.

Error `kind` values are part of the contract. Once published, never repurposed
(add new ones).

| Kind | Meaning |
|------|---------|
| `binary_not_found` | Path doesn't resolve on the consumer's host. |
| `binary_mismatch` | buildId disagrees with the on-disk binary. |
| `not_a_native_dotnet_image` | The binary opened but isn't a managed-flavoured native build. |
| `symbol_not_found` | Symbol not in `.map` or `.symtab`. |
| `address_out_of_range` | Address is not inside any known section. |
| `mstat_not_found` | No paired `.mstat` sidecar file could be located. |
| `mstat_invalid` | The `.mstat` sidecar exists and was opened, but its contents are not a parseable NativeAOT `.mstat` image (truncated, wrong format, bad table offsets, etc.). |
| `dgml_not_found` | No paired `.dgml` sidecar file could be located. |
| `disassembly_unsupported` | Architecture not supported in this version (e.g. ARM64 pre-V1). |
| `invalid_argument` | A supplied argument value was invalid. |
| `build_id_mismatch` | Build-id provided in an eager manifest import did not match the on-disk binary. |
| `macho_feature_unsupported` | The Mach-O binary uses a feature not yet supported in this version (e.g. 32-bit thin, `LC_DYLD_CHAINED_FIXUPS`, embedded bitcode via `__LLVM` segment, or a fat binary with no x86_64/arm64 slice). |
| `internal_error` | An unexpected internal failure occurred. |

## 4. Handles, not paths

Identities are stable handles, mirroring `(ModuleVersionId, MetadataToken)`
from `dotnet-assembly-mcp`:

- `i:<buildId>:<binaryNameHash>` — image handle.
- `s:<imageHandle>:<symbolIndex>` — symbol handle.
- Addresses are bare hex strings.

The LLM passes these around as opaque tokens; only the server interprets them.

## 5. Bootstrap

Server startup pre-warms only what it needs to answer `initialize` quickly. No
binary is opened until an explicit `load_native_binary` call. Lazy is correct.

## 6. Auth

Static bearer token, opt-in. Honors `NativeMcp:BearerToken`,
`NATIVE_MCP_BEARER_TOKEN`, then `MCP_BEARER_TOKEN` as the shared fallback (so
one secret can gate the whole triad). `/health` always exempt; STDIO stays
unauthenticated. Same shape as the two sibling repos.

## 7. What this server is not

- Not a managed-metadata navigator (use `dotnet-assembly-mcp`).
- Not a process attacher (use `dotnet-diagnostics-mcp`).
- Not a Ghidra-class decompiler. Not a kernel debugger. Not a fuzzer.

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
  "summary": "Loaded symbols for apphost",
  "data": { /* typed payload */ },
  "hints": [
    {
      "kind": "next_action",
      "message": "Call get_symbol_by_handle with s:i:...:42 for details"
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

Static bearer token, opt-in. Honors `NATIVE_MCP_BEARER_TOKEN` first,
`MCP_BEARER_TOKEN` as the shared fallback (so one secret can gate the whole
triad). `/health` always exempt. Same shape as the two sibling repos.

## 7. What this server is not

- Not a managed-metadata navigator (use `dotnet-assembly-mcp`).
- Not a process attacher (use `dotnet-diagnostics-mcp`).
- Not a Ghidra-class decompiler. Not a kernel debugger. Not a fuzzer.

# CLI conventions — `dotnet-native-cli`

The standalone CLI packages the same native-binary inspection capabilities that power the
`dotnet-native-mcp` server, but optimized for local shell workflows.

Use this document alongside:

- [docs/mcp-conventions.md](./mcp-conventions.md) for the shared `NativeResult<T>` envelope,
  stable error kinds, and overall repo philosophy.
- [docs/handoff-contract.md](./handoff-contract.md) for the producer/consumer contract that the
  server follows when `dotnet-diagnostics-mcp` hands off native frames.

## Install and packaging

Release builds ship in two forms:

- **NuGet global tool**: `dotnet tool install -g dotnet-native-cli`
- **Self-contained single-file archives**: `dotnet-native-cli-<version>-<rid>.tar.gz` or `.zip`

The tool command name is `dotnet-native-cli`.

## Verb naming conventions

The CLI intentionally mirrors the server's vocabulary, while shortening a few names for an
interactive shell:

| CLI verb | Relationship to the MCP/server surface |
|---|---|
| `version` | CLI-only convenience verb for package/version/path-policy introspection. |
| `r2r header` | Mirrors `get_r2r_header`. |
| `r2r runtime-functions` | Mirrors `list_r2r_runtime_functions`. |
| `disasm` | Short CLI form of `disassemble`. |
| `resolve` | Short CLI form of `resolve_symbols`. |
| `callers` | Short CLI form of `find_native_callers`. |
| `symbols` | Short CLI form of `list_native_symbols`. |
| `imports` | Short CLI form of `list_native_imports`. |
| `size` | Short CLI form of `get_size_breakdown`. |
| `size-diff` | CLI-only diff wrapper over the `.mstat` size-comparison flow. |
| `strings` | Short CLI form of `extract_strings`. |
| `retention` | Short CLI form of `explain_retention`. |

Conventions used across the verb set:

- Prefer short, noun- or action-oriented names (`symbols`, `imports`, `resolve`, `callers`) for shell ergonomics.
- Keep hyphenated compound verbs when they describe one concept (`size-diff`, `runtime-functions`).
- Group R2R-only functionality under the `r2r` verb instead of flattening many top-level verbs.
- Reuse the same domain words as the sibling repos where possible so the server and CLI surfaces stay aligned.

## Output modes

Every command accepts a global `--output json|table` option. The default is `json`.

### `--output json` (default)

`JsonOutputWriter` serializes the result as an indented camelCase JSON object with the shared
`NativeResult<T>` shape:

- `summary`
- `data`
- `hints`
- `error`

This is the best mode for scripts, `jq`, snapshot tests, and piping one command's output into
other tools.

### `--output table`

`TableOutputWriter` prints a human-oriented rendering:

1. the result `summary` first,
2. then key/value rows or tables depending on the payload type,
3. and, for failures, `kind`, `message`, and optional `detail` rows.

Commands with richer payloads (`resolve`, `callers`, `r2r`, `size`, `size-diff`, `strings`,
`retention`) provide specialized table renderings; simpler payloads fall back to public-property
inspection.

## Exit codes

The current CLI returns these documented application-level exit codes:

| Code | Meaning |
|---|---|
| `0` | Command completed successfully. This includes `version` and successful domain queries. |
| `1` | Command produced a structured error result or failed during command-specific validation/loading (for example invalid addresses, rejected paths, unsupported binaries, or missing sidecars). |
| `2` | `size-diff` completed successfully but `--fail-on-increase-bytes` was supplied and the total attributed-byte growth exceeded the threshold. |

Notes:

- Most verbs return `result.IsError ? 1 : 0` after emitting the structured payload.
- `version` always returns `0` once invoked.
- Parser-managed behavior such as `--help` is handled by `System.CommandLine`; those flows are outside the command-specific `NativeResult` exit mapping above.

## Path and allow-list semantics

The CLI uses the same shared `PathAccessPolicy` as the server. `CliPathPolicyFactory` merges roots
from all of these inputs:

1. `NativeMcp:AllowedBinaryRoots`
2. `NATIVE_MCP_ALLOWED_ROOTS` (split by the platform path separator: `:` on POSIX, `;` on Windows)
3. `BINARIES_DIR`
4. repeated `--allow <path>` command-line arguments

Important behavior:

- Paths are canonicalized before use (`Path.GetFullPath` plus symlink/junction resolution).
- The policy is **permissive by default** until at least one operator root or `--allow` root is configured.
- Once any root is configured, the policy becomes **enforcing** and rejects paths outside the allow-list with `path_not_allowed`.
- When enforcing, well-known roots are also allowed so common .NET locations keep working: the NuGet package cache, the .NET runtime install, and the system temp directory used for sidecar staging.
- `dotnet-native-cli version` reports `pathPolicyEnforcing` plus the effective `allowedRoots`, which is the easiest way to verify the active configuration.

## Consistency with the sibling repos

This CLI follows the same broader philosophy described in this repository's AGENTS.md and shared MCP docs:
consistent naming, stable error kinds, a bounded surface area, and structured results that can move
between automated and human-driven workflows. Where `dotnet-diagnostics-mcp` or `dotnet-assembly-mcp`
expose companion CLI/server surfaces, prefer keeping terminology and result shapes aligned instead of
inventing one-off conventions here.

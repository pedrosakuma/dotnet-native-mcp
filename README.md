# dotnet-native-mcp

> **Status:** V1 in progress. Eleven MCP tools are live:
> `load_native_binary`, `import_native_manifest`, `list_native_symbols`, `list_native_imports`, `resolve_symbols`, `extract_strings`, `get_size_breakdown`, `explain_retention`, `compare_native_binaries`, `disassemble`, `find_native_callers`.
> See the [V0 tracking issue](https://github.com/pedrosakuma/dotnet-native-mcp/issues/1).

MCP server for **navigating native .NET binaries** — NativeAOT, R2R-only,
single-file native — when ECMA-335 metadata is stripped or absent. Designed as
the third leg of a tooling triad with [`dotnet-assembly-mcp`](https://github.com/pedrosakuma/dotnet-assembly-mcp)
(managed metadata) and [`dotnet-diagnostics-mcp`](https://github.com/pedrosakuma/dotnet-diagnostics-mcp)
(live process events).

## Why this exists

`dotnet-diagnostics-mcp` already attaches to NativeAOT processes and emits
hotspot frames whose symbols are mangled ILC names (`S_P_____...`) and whose
addresses point at native code. `dotnet-assembly-mcp` cannot answer queries on
those frames — its `load_assembly` rejects NativeAOT binaries with
`module_load_failed: not a managed PE`. Today, the LLM receives a hex address
and a mangled symbol and has nowhere to take them.

This server closes the gap. It accepts the `NativeFrame` handoff (binary +
symbol + address), demangles ILC symbols back to managed-looking names,
disassembles the native code with [Iced](https://github.com/icedland/iced), and
will read the sidecar artifacts ILC emits (`.mstat`, `.map`, DGML) when they
are available.

## Where it does **not** belong

- **Managed metadata, IL, decompile-to-C#.** That's `dotnet-assembly-mcp`.
- **Live process attach, EventPipe / ETW collection.** That's `dotnet-diagnostics-mcp`.
- **Generic reverse engineering** (full Ghidra-class decompilation, full
  dynamic instrumentation, kernel-mode debuggers). Out of scope by design.

## Tool surface (11 tools, V1)

| Tool                     | Purpose                                                                 |
|--------------------------|-------------------------------------------------------------------------|
| `load_native_binary`     | Open a PE/ELF/Mach-O, verify it's a managed-flavored native build, return a handle. Accepts `path` + optional `buildId`. |
| `import_native_manifest` | Bulk handshake from a producer (typically `dotnet-diagnostics-mcp`): register a list of native binaries in one call. Accepts `entries: [{path, name?, buildId?}]` + `mode` (`lazy`\|`eager`). Per-entry failures reported inline without failing the batch. |
| `list_native_symbols`    | Paginated symbol table. Source priority: `.map` sidecar → ELF `.symtab`/`.dynsym` → PE export table. Includes raw + demangled names. |
| `list_native_imports`    | Paginated import/dependency walk. `kind="functions"` lists ELF undefined `.dynsym` imports or PE Import Directory entries; `kind="libraries"` lists ELF `DT_NEEDED` or PE imported DLL names. |
| `resolve_symbols`        | Batch address ↔ symbol lookup with ILC demangling. Accepts up to 200 hex (`0x`-prefixed or bare) or decimal address strings against a single image. Per-address failures are reported inline without failing the whole batch. Replaces the former single-address `resolve_symbol` and multi-frame `symbolicate_stack` tools. |
| `extract_strings`        | Paginated printable ASCII / UTF-16LE scan over `.rodata` / `.rdata` / `.data.rel.ro` / `__const` (with `.data` fallback). Returns section + offset for forensics. |
| `get_size_breakdown`     | Read the `.mstat` sidecar emitted by NativeAOT and aggregate native bytes by assembly, namespace, type, or method. |
| `explain_retention`      | Read the DGML reachability sidecar emitted by NativeAOT and return the shortest root → target path that kept a type or method reachable. |
| `compare_native_binaries`| Diff two loaded images: build-id, format, arch, file/section size deltas, added/removed/size-changed symbols. |
| `disassemble`            | Iced x86/x64 disassembly with CALL/JMP cross-ref hints. Default 64 instructions, capped at 2048. ARM64 returns `disassembly_unsupported`. `resolveSource=false` (default) skips per-instruction DWARF lookup; set `true` to annotate each instruction with file:line. **Two modes:** registered-handle mode (`imageHandle` + `address`/`symbolName`) for images loaded via `load_native_binary`; or raw-bytes mode (`imagePath` + `rva` + `size`) for any PE/ELF/Mach-O — including managed PEs with R2R bodies — without a prior `load_native_binary` call. |
| `find_native_callers`    | Lazily-built xref index: scan all executable sections of a loaded x86-64 image and return every CALL/JMP instruction that targets a given symbol name or hex address. The index is cached in-process (L1) and persisted to disk under `~/.cache/dotnet-native-mcp/<build-id>.xref` (L2) so large NativeAOT binaries pay the scan cost only once across sessions. ARM64 returns `disassembly_unsupported`. `resolveSource=true` (default) annotates each call site with file:line; set `false` to skip PDB I/O for large binaries. |

For crash logs or sampled stacks where `dotnet-diagnostics-mcp` is not in the loop, use `load_native_binary` once and then call `resolve_symbols` with a list of raw hex addresses. When you already have `NativeFrame` handoffs with mangled addresses, pass those address strings directly to `resolve_symbols`.

## Sidecar tier (V1+)

ILC emits structured sidecars on request:

| Artifact | Switch                          | What it gives us                                   |
|----------|---------------------------------|----------------------------------------------------|
| `.mstat` | `IlcGenerateMstatFile=true`     | per-type / per-method native size                  |
| `.map`   | `IlcMapFileType=Normal`         | symbol → address map                               |
| DGML     | `IlcGenerateDgmlFile=true`      | reachability graph from the trimmer                |

`.mstat` parsing answers "what blew up my AOT binary"; DGML reachability answers
"why was this type or method kept?".

## Install

```bash
# stdio (local MCP client)
dotnet tool install -g dotnet-native-mcp
dotnet-native-mcp --stdio

# HTTP (sidecar / multi-client)
docker run --rm -p 8789:8080 \
  -v /path/to/binaries:/binaries:ro \
  ghcr.io/pedrosakuma/dotnet-native-mcp:latest
```

Default port: **8789**. Slot picked to continue the convention started by
`dotnet-diagnostics-mcp` (8787) and `dotnet-assembly-mcp` (8788).

## Running the triad with Docker Compose

To bring up all three servers together (diagnostics on 8787, assembly on 8788,
native on 8789):

```bash
export BINARIES_DIR=/path/to/your/nativeaot/binaries
export ASSEMBLIES_DIR=/path/to/your/managed/assemblies
docker compose -f deploy/docker-compose.yml up -d
```

See **[docs/compose.md](docs/compose.md)** for the full guide: env vars,
bearer-token setup, MCP client configuration, and how to attach to live
processes.

## Verifying releases

Every release artifact (NuGet package and self-contained binary archive) is
published with a **SLSA build provenance attestation** generated by
[`actions/attest-build-provenance`](https://github.com/actions/attest-build-provenance)
and signed by Sigstore via GitHub's OIDC issuer. The attestation proves the
artifact was built by this repository on a specific commit by GitHub-hosted
runners — no separate cert to install, no key to rotate.

Verify with the GitHub CLI:

```bash
# NuGet package
gh attestation verify dotnet-native-mcp.0.1.0.nupkg \
  --repo pedrosakuma/dotnet-native-mcp

# Self-contained binary tarball / zip
gh attestation verify dotnet-native-mcp-0.1.0-linux-x64.tar.gz \
  --repo pedrosakuma/dotnet-native-mcp
```

A passing verification confirms the build came from `pedrosakuma/dotnet-native-mcp`
on the expected commit and tag.

## Disk cache

`find_native_callers` maintains a persistent on-disk xref index so large NativeAOT
binaries are scanned only once across server restarts.

| Detail | Value |
|--------|-------|
| **Location** | `~/.cache/dotnet-native-mcp/<build-id>.xref` (Linux: `$XDG_CACHE_HOME/dotnet-native-mcp/` if set) |
| **Cache key** | ELF build-id / PE CodeView GUID+Age / Mach-O LC_UUID, falling back to a SHA-256 prefix of the file bytes |
| **Format** | 4-byte magic `NXR1` + 4-byte version + JSON body |
| **Invalidation** | Automatic on binary rebuild (new build-id → new file). On format upgrade the old file is silently ignored and rebuilt. |
| **Eviction** | None (files accumulate by build-id). Run `rm -rf ~/.cache/dotnet-native-mcp/` to clear manually. |
| **Disable** | Set `DOTNET_NATIVE_MCP_XREF_CACHE=0` to bypass all disk I/O (useful in CI or read-only environments). |

## Authentication

HTTP transport supports optional bearer-token auth. Leave it unset for local/dev
back-compat; set either `NATIVE_MCP_BEARER_TOKEN` or `NativeMcp:BearerToken` to
require `Authorization: Bearer <token>` on every `/mcp` request. `/health`
remains open. STDIO transport stays unauthenticated.

```bash
export NATIVE_MCP_BEARER_TOKEN="replace-me"
dotnet-native-mcp
```

## Building blocks

- [`Iced`](https://github.com/icedland/iced) — MIT, .NET-native x86/x64 disassembler.
- `System.Reflection.PortableExecutable` — for PE headers and section reads.
- `System.IO.Pipelines` — for streaming reads of large native binaries.
- A small ELF reader (Linux NativeAOT binaries are ELF).
- (V1) Capstone P/Invoke for ARM64 disassembly.

## License

MIT.

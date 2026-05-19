# dotnet-native-mcp

> **Status:** scaffold phase. The repository builds and serves `scaffold_status`
> plus a first V1 utility tool: `extract_strings`. Real V0 navigation tools land
> next — see the
> [V0 tracking issue](https://github.com/pedrosakuma/dotnet-native-mcp/issues).

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

## V0 surface (planned)

| Tool                     | Purpose                                                                 |
|--------------------------|-------------------------------------------------------------------------|
| `load_native_binary`     | Open a PE/ELF, verify it's a managed-flavored native build, return a handle. |
| `list_native_symbols`    | Symbol table from `.map` when available, ELF/PE symtab fallback.        |
| `resolve_symbol`         | Address ↔ symbol, with ILC demangling to a managed-shaped name.         |
| `disassemble`            | Iced disassembly around a symbol or address, with cross-ref hints.     |

## Sidecar tier (V1+)

ILC emits structured sidecars on request:

| Artifact | Switch                          | What it gives us                                   |
|----------|---------------------------------|----------------------------------------------------|
| `.mstat` | `IlcGenerateMstatFile=true`     | per-type / per-method native size                  |
| `.map`   | `IlcMapFileType=Normal`         | symbol → address map                               |
| DGML     | `IlcGenerateDgmlFile=true`      | reachability graph from the trimmer                |

`.mstat` parsing is the highest-value V1 item — it answers "what blew up my AOT
binary".

## Install

Scaffold-phase only. Once the V0 tools land:

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

## Building blocks

- [`Iced`](https://github.com/icedland/iced) — MIT, .NET-native x86/x64 disassembler.
- `System.Reflection.PortableExecutable` — for PE headers and section reads.
- `System.IO.Pipelines` — for streaming reads of large native binaries.
- A small ELF reader (Linux NativeAOT binaries are ELF).
- (V1) Capstone P/Invoke for ARM64 disassembly.

## License

MIT.

# AGENTS.md

> Operating guide for AI coding agents working on `dotnet-native-mcp`.
> Mirrors the AGENTS.md of `dotnet-assembly-mcp` and `dotnet-diagnostics-mcp`
> — drift between the three is the single biggest cost.

## What this project is

`dotnet-native-mcp` is the third MCP server in the .NET tooling triad:

| Server                       | Surface                                                          |
|------------------------------|------------------------------------------------------------------|
| `dotnet-diagnostics-mcp`     | Live process attach, EventPipe / ETW / perf — emits handles.     |
| `dotnet-assembly-mcp`        | Static managed metadata, IL, decompilation — resolves managed handles. |
| **`dotnet-native-mcp`**      | **Static native binary navigation — resolves NativeAOT / R2R handles.** |

Triggered by NativeAOT: `dotnet-diagnostics-mcp` already attaches to AOT
processes via `EtwNativeAotCpuSampler` / `PerfScriptParser`, but the resulting
`NativeFrame { Binary, Symbol (mangled), Address }` payloads have nowhere to
go — `dotnet-assembly-mcp` rejects AOT binaries (`module_load_failed: not a
managed PE`). This server is the handoff target for those frames.

**Status:** scaffold. The repository builds and exposes a single
`scaffold_status` MCP tool. V0 tracking issue defines the real surface.

## Read before contributing

1. **`docs/handoff-contract.md`** — the `NativeFrame` wire format consumed from
   `dotnet-diagnostics-mcp`. Coordinate any change with the companion repo.
2. **`docs/mcp-conventions.md`** — tool budget, error kinds, response shape,
   bootstrap. Mirrors the conventions in the sibling repos.
3. The two sibling AGENTS.md files — they document the conventions we are
   mirroring.

## Critical rules

- **Never reference MCP packages from `DotnetNativeMcp.Core`.** Core is the
  testable domain; MCP attributes live in `Server`.
- **Never `Assembly.Load` or `dlopen` an inspected binary.** We parse PE/ELF
  metadata only. Executing code from a binary the user asked us to inspect is
  a sandbox-escape vector.
- **Never repurpose an `Error.Kind` value once published.**
- **Tool budget of 10**: hard cap. New capabilities go behind Resources or
  parameters, not new tools.
- **Mirror the siblings** for build conventions (CPM, warnings-as-errors,
  slnx, `net10.0`, SDK 10.0.201 via `global.json`). Do not invent a new
  convention.

## Build, test, run

```bash
dotnet build -c Release
dotnet test -c Release --no-build
dotnet run --project src/DotnetNativeMcp.Server -c Release          # HTTP on 8789
dotnet run --project src/DotnetNativeMcp.Server -c Release -- --stdio
```

## Pre-PR review

Before opening or merging any PR, dispatch a `code-review` sub-agent with
model `gpt-5.5` over the PR diff (`git diff main...HEAD`, triple-dot from
the merge-base) and address its findings. This is a standing rule — do not
skip even for "obvious" or "trivial" changes.

## Out of scope (by design)

- Managed metadata / IL / decompile-to-C# — use `dotnet-assembly-mcp`.
- Live process attach, EventPipe — use `dotnet-diagnostics-mcp`.
- Ghidra-class full decompilation; kernel debuggers; dynamic instrumentation.

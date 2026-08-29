# MCP 2026 draft-spec stateless protocol migration assessment

**Issue**: #143 (spike) · **Assessed**: 2026-08-29
**Status**: ✅ Spike complete. **Recommendation: GO — bump `ModelContextProtocol[.AspNetCore]`
1.3.0 → 2.2.0 in a normal follow-up PR.** No architecture changes required.

## Executive summary

The spec referenced by issue #143 (SEP-2567, SEP-2575, SEP-2322) has finalized. Stable SDK
`2.2.0` is on NuGet, and `dotnet-diagnostics-mcp` — this repo's handoff partner and the harder
case (session-bound orchestrator handles, server-initiated elicitation) — already shipped the
production migration in v0.24.0 (2026-08-19), bumping `1.4.0` → `2.2.0`. That means the "wait for
a stable SDK" gate from the original issue text no longer applies: the SDK is stable *today*.

This repo's own exposure is materially smaller than dotnet-diagnostics-mcp's, and the spike
confirms it end-to-end:

- No source in `src/` references `SessionId`, `ElicitAsync`, sampling, or roots (confirmed by
  grep across `src/DotnetNativeMcp.Core` and `src/DotnetNativeMcp.Server` — zero matches).
- `INativeBinaryRegistry` (`src/DotnetNativeMcp.Core/Imaging/NativeBinaryRegistry.cs:33-38`) is a
  plain process-wide singleton keyed by an opaque handle string / canonical path — never bound to
  a protocol session. It already matches the SEP-2567 "explicit handle" model; no orchestrator-style
  redesign (the one `dotnet-diagnostics-mcp` needed) is applicable here.
- `Program.cs` does not hard-code `options.ProtocolVersion` (unlike `dotnet-diagnostics-mcp`,
  which had `"2025-11-25"` baked in and had to update it). One less migration item.
- Tool registration is a single fixed `.WithTools<NativeTools>()` call — no per-session tool
  list, no custom `initialize`/handshake code, no custom HTTP header handling beyond the existing
  bearer-token middleware (which reads the `Authorization` header directly, independent of MCP
  handshake state).

## What was actually done (not just read)

1. **Grep confirmation** — `grep -rn "SessionId\|ElicitAsync\|Sampling\|RequestRoots\|ListRoots"
   src/` returned no matches. `grep -rn "ProtocolVersion\|McpServerOptions\|RequestContext<"`
   found only the `AddMcpServer(options => { options.ServerInfo = ...; })` call in `Program.cs` —
   no `ProtocolVersion` assignment, no custom `RequestContext<...>` handling anywhere in the repo.
2. **Read the draft changes** via the sibling repo's already-completed, more thorough per-SEP
   inventory (`dotnet-diagnostics` `docs/research/mcp-2026-draft-migration.md`, and its shipped
   `CHANGELOG.md` v0.24.0 entry) rather than re-deriving the same SEP analysis from scratch, per
   this issue's non-goals ("don't duplicate the full research already done"). Cross-checked
   against this repo's actual code (see previous section) to confirm the "narrower exposure"
   claim is code-verified, not assumed.
3. **Spiked the real upgrade** on a throwaway branch (`spike/mcp-2026-draft-assessment`, not
   merged, SDK bump reverted after the spike):
   - Bumped `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` from `1.3.0` → `2.2.0` in
     `Directory.Packages.props`.
   - `dotnet build -c Release`: **0 errors, 0 warnings** — no source changes needed anywhere in
     `Core` or `Server`.
   - `dotnet test -c Release --no-build`: **866/866 passed** (730 Core + 136 Server), unchanged.
   - Ran the HTTP server built against `2.2.0` and drove raw JSON-RPC over `/mcp` with `curl`,
     with **no `initialize` call**:
     - `tools/list` → succeeded, returned the full tool catalog.
     - `tools/call load_native_binary` (deliberately bad path) → succeeded, returned the normal
       tool error envelope (`isError: true`, `binary_not_found`), no session/handshake error.
     - No `Mcp-Session-Id` header was emitted or required in the response.
   - This directly confirms the SDK's stateless mode works for this server's actual tool surface,
     not just in theory.
4. **Reverted** the version bump on the spike branch before writing this doc — the branch is not
   merged and can be deleted; the real bump should land as its own reviewed PR (see below).

## Repo impact map (delta vs. the dotnet-diagnostics-mcp assessment)

| SEP | dotnet-diagnostics-mcp finding | dotnet-native-mcp finding |
|---|---|---|
| 2567 (remove protocol sessions) | Orchestrator investigation-handle model was session-bound; required a redesign (shipped separately in #554/PR #559 before the SDK bump). | **No equivalent blocker.** `NativeBinaryRegistry` never used `SessionId`. Nothing to redesign. |
| 2575 (remove `initialize`, add `server/discover`) | `options.ProtocolVersion = "2025-11-25"` hard-coded; had to be updated. | No hard-coded protocol version anywhere — nothing to update beyond the package bump itself. `server/discover` confirmed automatic via `.WithHttpTransport()` + `.MapMcp(...)`, same as upstream's finding. |
| 2322 (MRTR replaces server-push elicitation/sampling/roots) | `DumpApprovalElicitation` used `McpServer.ElicitAsync(...)`, which throws under stateless mode; required an MRTR rewrite. | **Not applicable.** No tool in this repo uses elicitation, sampling, or roots. Nothing to rewrite. |

**Bottom line:** this server has none of the two concrete migration blockers dotnet-diagnostics-mcp
had (session-bound orchestrator state, server-initiated elicitation). The migration here is a
package-version bump plus normal regression testing — confirmed by the spike above, not just
inferred from the absence of grep matches.

## Recommendation

**GO.** Open a normal (non-spike) follow-up PR that:

1. Bumps `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` to `2.2.0` (or the latest
   `2.x` at PR time) in `Directory.Packages.props`.
2. Re-runs the full test suite + a manual raw-HTTP smoke pass (`tools/list`, `tools/call` on a
   couple of representative tools, with and without an `initialize` call) to confirm behavior
   parity, mirroring `dotnet-diagnostics-mcp`'s
   [manual MCP smoke test](https://github.com/pedrosakuma/dotnet-diagnostics/blob/main/docs/manual-mcp-smoke-test.md)
   pattern.
3. Notes in the PR description (and `CHANGELOG.md` if one exists) that this is a wire-compatible
   bump: older clients pinned to the pre-2026-07-28 protocol revision continue to work unaffected
   (verified upstream by dotnet-diagnostics-mcp's own backward-compatibility smoke test; this
   repo has no session/elicitation-dependent behavior that could regress for either era of
   client).

No further spike or assessment work is needed before that PR — this issue can close once the
bump PR is filed/linked.

## References

- pedrosakuma/dotnet-diagnostics#546 (original, more detailed assessment — session-bound
  orchestrator + elicitation blockers, both resolved before their v0.24.0 ship)
- `dotnet-diagnostics` `docs/research/mcp-2026-draft-migration.md` (per-SEP technical inventory)
- `dotnet-diagnostics` `CHANGELOG.md` v0.24.0 entry (shipped migration notes, SDK `1.4.0` →
  `2.2.0`)
- SEP-2567: <https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2567>
- SEP-2575: <https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2575>
- SEP-2322: <https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2322>
- Draft changelog: <https://modelcontextprotocol.io/specification/draft/changelog>

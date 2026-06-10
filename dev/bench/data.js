window.BENCHMARK_DATA = {
  "lastUpdate": 1781118556962,
  "repoUrl": "https://github.com/pedrosakuma/dotnet-native-mcp",
  "entries": {
    "FindNativeCallers Benchmark": [
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "ef20124ae8299193a0a95950588e2f7b178cc5b7",
          "message": "Fix benchmark branch bootstrap (#87)\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-21T13:01:05-03:00",
          "tree_id": "2f4cdfe7c82d510e3066501d61b91484c8526e8b",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/ef20124ae8299193a0a95950588e2f7b178cc5b7"
        },
        "date": 1779379945747,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 12340752203.466667,
            "unit": "ns",
            "range": "± 39788087.92516588"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 29151785.98161765,
            "unit": "ns",
            "range": "± 572165.945561123"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 30.93987194299698,
            "unit": "ns",
            "range": "± 0.19571179899610144"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 227631.62662597655,
            "unit": "ns",
            "range": "± 25147.544544569388"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 14620.101157633464,
            "unit": "ns",
            "range": "± 18.37543138741368"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 23.092827692627907,
            "unit": "ns",
            "range": "± 0.13424511911140052"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "7299bed916595372a444b83cfbb2d42332510ecb",
          "message": "Bump GitHub Actions to Node 24-based majors (#90) (#91)\n\nEliminates the Node 20 deprecation warnings emitted on every workflow\nrun since the runner started warning. Node 20 will be removed from\nGitHub-hosted runners on 2026-09-16; default flips to Node 24 on\n2026-06-02.\n\nUpgrade map (chosen to be minimal Node-24 bumps, skipping majors that\nintroduce ESM or other API breaks we don't need):\n- actions/checkout v4 → v5\n- actions/setup-dotnet v4 → v5\n- actions/cache v4 → v5\n- actions/upload-artifact v4 → v6\n- actions/download-artifact v4 → v7\n- actions/attest-build-provenance v2 → v4\n- softprops/action-gh-release v2 → v3\n\nCross-repo mirror to be opened in dotnet-assembly-mcp and\ndotnet-diagnostics-mcp after this PR validates the pattern.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T11:17:11-03:00",
          "tree_id": "cd525c4877e6b09cc6cf0f13d29477b74bffefcd",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/7299bed916595372a444b83cfbb2d42332510ecb"
        },
        "date": 1779460118885,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13165326260.266666,
            "unit": "ns",
            "range": "± 23866601.323289588"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 30277493.30926724,
            "unit": "ns",
            "range": "± 868916.6337835335"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 29.689884548385937,
            "unit": "ns",
            "range": "± 0.08051889720826659"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 171756.67658691405,
            "unit": "ns",
            "range": "± 13649.713546468212"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 16045.25623028095,
            "unit": "ns",
            "range": "± 76.27309715359401"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.652559910501754,
            "unit": "ns",
            "range": "± 0.08833981148669108"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "9c30cf9d9b2e256846cb8a871eeed46b23bd86d0",
          "message": "Replace dynamic permissions expression in bench.yml with static value (#107)\n\nPR #92 set the bench job's permissions to\n`contents: ${{ github.event_name == 'push' && 'write' || 'read' }}`.\nGitHub Actions does not support expressions in the `permissions:` map\n(values must be literal read/write/none), so the workflow file failed\nparse and every bench run since the #92 merge completed in 0 seconds\nwith 'This run likely failed because of a workflow file issue'. No\nBenchmarkDotNet history has been published to gh-pages since v0.5.4.\n\nThis hotfix reverts to a static `contents: write` on the bench job.\nThe workflow-level `contents: read` default added by #92 stays in\nplace, so the regression vs. the pre-#92 state is strictly tighter at\nworkflow scope. PR exposure is documented honestly in the comment and\nthe proper fix (split push vs. PR into two jobs with static\npermissions) is tracked in #106.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T15:09:18-03:00",
          "tree_id": "16887e2c426a2056972ec16157125175d0a35b85",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/9c30cf9d9b2e256846cb8a871eeed46b23bd86d0"
        },
        "date": 1779474009308,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 11875492848.733334,
            "unit": "ns",
            "range": "± 64632464.23260687"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 27621194.989583332,
            "unit": "ns",
            "range": "± 249396.23135081775"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 30.183708504835764,
            "unit": "ns",
            "range": "± 0.19876522773517025"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 461395.64534505206,
            "unit": "ns",
            "range": "± 8621.32473784679"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20172.883371988934,
            "unit": "ns",
            "range": "± 50.534807525056934"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.48819341191224,
            "unit": "ns",
            "range": "± 0.13891747906394483"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8764ef474031923558cf29b051bdf601160ece56",
          "message": "Split bench into push vs. PR jobs with static least-privilege permissions (#108)\n\nCloses #106.\n\nThe hotfix in #107 restored bench.yml to a single job with a static\n`contents: write` permission so the YAML would parse, at the cost of\ngranting maintainer-labeled `perf` PRs a writable GITHUB_TOKEN. The\nbenchmark-action's `auto-push` step is gated on push events, but\nPR-controlled `dotnet run` code in the bench fixtures could in\nprinciple use the token before that gate. #106 tracked the structural\nfix.\n\nThis change introduces a composite action under\n`.github/actions/run-bench/` that owns the setup, build, run and\nstorage steps. The workflow now defines two jobs with complementary\nevent filters and statically declared permissions:\n\n- bench-push: `if: github.event_name != 'pull_request'` (covers push +\n  workflow_dispatch), `permissions: contents: write`, calls the\n  composite with `auto-push: 'true'` / `fail-on-alert: 'false'`.\n- bench-pr: `if: github.event_name == 'pull_request' &&\n  contains(labels, 'perf')`, `permissions: contents: read`, calls the\n  composite with `auto-push: 'false'` / `fail-on-alert: 'true'`.\n\nPR-controlled bench code therefore runs under a strictly read-only\nGITHUB_TOKEN and the benchmark history can only be advanced by the\npush baseline path. GitHub Actions still does not allow expressions in\nthe `permissions:` map, so the split is enforced at the job level via\nevent filtering instead.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T15:40:41-03:00",
          "tree_id": "9c36fdab158009ac152e50c99d84947723a2d5b4",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8764ef474031923558cf29b051bdf601160ece56"
        },
        "date": 1779475853014,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 12919203796.066668,
            "unit": "ns",
            "range": "± 49101770.09862263"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28299892.19419643,
            "unit": "ns",
            "range": "± 331125.03808911506"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 28.52180231469018,
            "unit": "ns",
            "range": "± 0.06498884077890871"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 326031.16373258026,
            "unit": "ns",
            "range": "± 10963.143144582118"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 9810.176822916666,
            "unit": "ns",
            "range": "± 52.24213577891438"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 23.815137308835983,
            "unit": "ns",
            "range": "± 0.13100097477669662"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "aa2e0c221ae3d34514d4d5a938f683feedc5e6b5",
          "message": "test: add differential (oracle) harness for the ELF reader vs readelf (#110)\n\nAdds a differential test harness that cross-checks ElfReader against GNU\nreadelf, complementing the existing fuzz harness (which only proves the\nparsers never throw) with a correctness oracle.\n\nSurfaces covered:\n- symbols (readelf -sW): per-index name, value, size, function flag + count\n- sections (readelf -SW): per-name virtual address, file offset, size\n- imports: DT_NEEDED libraries (readelf -dW) and undefined .dynsym symbols\n\nNotes:\n- Shared ReadelfOracle helper shells out to readelf and parses its wide\n  (-W) output; drains stdout/stderr asynchronously with an effective\n  timeout. Symbol Size is decimal in -sW; section geometry is hex in -SW.\n- Tests no-op when readelf or the NativeAOT fixture are unavailable, so\n  the suite stays green on hosts without binutils. CI (ubuntu-latest) has\n  readelf and builds the fixture, so the comparison runs for real.\n- Version suffixes (@GLIBC_x.y) are normalized on both sides; SHT_NOBITS\n  FileSize is not checked; duplicate section names fall back to a\n  geometry-existence match.\n\nSee docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T11:59:55-03:00",
          "tree_id": "9a7a776f87447e5bac73499ce540ffc6550d3e39",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/aa2e0c221ae3d34514d4d5a938f683feedc5e6b5"
        },
        "date": 1781017833920,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 14077927580.857143,
            "unit": "ns",
            "range": "± 45865522.46980509"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28285854.057291668,
            "unit": "ns",
            "range": "± 605291.4427787367"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 29.81184527703694,
            "unit": "ns",
            "range": "± 0.09520387437784449"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 469820.0335286458,
            "unit": "ns",
            "range": "± 8364.075044983945"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20624.071352132163,
            "unit": "ns",
            "range": "± 187.72589858936897"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 21.82876957456271,
            "unit": "ns",
            "range": "± 0.08995490490796983"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "f26222daedc074587159bc8e849e72b615bd904d",
          "message": "test: add differential (oracle) harness for the x86/x64 disassembler vs objdump (#111)\n\nCross-checks IcedDisassembler (via RawDisassembler) against GNU\nobjdump -d -M intel, extending the differential-testing approach from the\nELF reader to the decoder.\n\nThe hard oracle is instruction-boundary + raw-byte agreement: two\nindependent decoders walking the same bytes must segment them identically.\nFor each .text function symbol in the SampleAot fixture the harness\ndisassembles the body both ways and asserts the in-range instruction\naddress SETS are equal (catching early-stop bugs in either decoder), then\nasserts identical raw bytes per address. Mnemonics are compared as a\nsofter signal after normalizing objdump's display (segment/rep/lock/REX\nprefix tokens stripped, movabs->mov, nop/xchg NOP family treated as one).\n\n- New OracleProcess shared process runner (concurrent stdout/stderr drain,\n  timeout, missing-tool skip); ReadelfOracle now delegates to it.\n- objdump is invoked with --insn-width=15 so long instructions never wrap\n  their byte column onto a continuation line.\n- Tests no-op when objdump or the fixture are unavailable; CI has both.\n\nSee docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T15:32:23-03:00",
          "tree_id": "eff6c022b7334406dd3f5f29069b7f0758ed7a30",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/f26222daedc074587159bc8e849e72b615bd904d"
        },
        "date": 1781030615177,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 12188765404,
            "unit": "ns",
            "range": "± 35841514.07591211"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28553798.16875,
            "unit": "ns",
            "range": "± 281182.15379183454"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 30.698818219559534,
            "unit": "ns",
            "range": "± 0.17093170598254148"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 493605.65502232144,
            "unit": "ns",
            "range": "± 15982.75173520142"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20438.40424194336,
            "unit": "ns",
            "range": "± 71.16055185223881"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.506488335132598,
            "unit": "ns",
            "range": "± 0.2922598948951802"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e05f1f566372189956bd51c694de515e7c0a6291",
          "message": "test: add differential (oracle) harness for the PE reader vs llvm-readobj (#112)\n\nCross-checks PeNativeReader's section table against LLVM\nllvm-readobj --sections, extending the differential-testing approach to\nthe PE reader (after the ELF reader and the x86/x64 disassembler).\n\nFor every section the harness asserts the name set is equal and the\ngeometry matches exactly: virtual address, virtual size, file offset, and\nfile size. PeNativeReader emits the full COFF section table with no\nfiltering, so unlike the ELF section comparison this asserts the complete\nset; duplicate section names fall back to a geometry-existence match.\n\n- New LlvmReadobjOracle: stateful parser over `Section { ... }` blocks;\n  hex for 0x-prefixed addresses/offsets, decimal for sizes.\n- Primary target is the always-present managed DotnetNativeMcp.Core.dll, so\n  the comparison runs everywhere instead of skipping on a missing fixture; a\n  second test additionally exercises the published ReadyToRun\n  System.Private.CoreLib.dll (the real asm-mcp -> native-mcp handoff target).\n- CI installs `llvm` so llvm-readobj is present and the comparison runs for\n  real rather than skipping.\n\nMach-O is intentionally not covered yet: the repo has no real Mach-O fixture\non disk to point an oracle at. See docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T15:57:48-03:00",
          "tree_id": "bd5f772bbcdc59b3a22cf3f3fbdb7a9288a726fd",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/e05f1f566372189956bd51c694de515e7c0a6291"
        },
        "date": 1781032118935,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 12723436605.6,
            "unit": "ns",
            "range": "± 31711623.894866217"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 26986661.347916666,
            "unit": "ns",
            "range": "± 303597.44810332113"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 32.40142783522606,
            "unit": "ns",
            "range": "± 0.01329067029587062"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 369622.5987548828,
            "unit": "ns",
            "range": "± 5614.6884376020525"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 21932.518851143974,
            "unit": "ns",
            "range": "± 55.03133651977547"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.4103132555118,
            "unit": "ns",
            "range": "± 0.02803789166783313"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "b1e569eff2db61ac03701358946b70485ca20a02",
          "message": "test: add Mach-O section differential harness vs llvm-readobj (#113)\n\nCompletes the ELF/PE/Mach-O differential (oracle) triad. Parses tiny\ncommitted Mach-O relocatable objects (x86_64 + arm64) both with MachOReader\nand with llvm-readobj --sections, then asserts per-section geometry agrees\n(virtual address, virtual size, file offset, file size).\n\nRelocatable .o objects are used as fixtures because MachOReader rejects\nLC_DYLD_CHAINED_FIXUPS (present in linked dylibs/executables); a .o never\ncarries chained fixups so it round-trips through the reader. Fixtures are\ncommitted (not built at test time) so only the oracle (llvm-readobj) is\nneeded and the test skips cleanly when LLVM is absent.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T16:53:45-03:00",
          "tree_id": "eb0dcf4ef00cf1278cd8f152c020f603e0c67548",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/b1e569eff2db61ac03701358946b70485ca20a02"
        },
        "date": 1781035467682,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 10299669749,
            "unit": "ns",
            "range": "± 98265921.63427569"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 21948509.38125,
            "unit": "ns",
            "range": "± 223763.57824798187"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 24.328440693872317,
            "unit": "ns",
            "range": "± 0.08292326015634442"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 1934674.0024088542,
            "unit": "ns",
            "range": "± 1075672.3672574614"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 17550.137072049656,
            "unit": "ns",
            "range": "± 38.79092133786903"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 17.53348892075675,
            "unit": "ns",
            "range": "± 0.05950391261101391"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8227247d82a1d82a6f52d3a97b0b9179b19ee791",
          "message": "test: add ARM64 disassembly differential harness vs llvm-objdump (#114)\n\nCompletes disassembler oracle coverage: Arm64Disassembler is now compared\nagainst llvm-objdump the same way x86/x64 is compared against GNU objdump.\n\nBuilding the harness surfaced a real production bug, fixed here:\nInstructionView.Mnemonic was sourced from instr.Mnemonic.ToText(false),\nwhich collapses every B.cond (b.eq, b.ne, ...) to a bare 'b', dropping the\ncondition suffix. Mnemonic and operands are now both derived from a single\ninstr.TryFormat pass via FormatMnemonicAndOperands, preserving the suffix.\nApplied at both call sites (Disassemble and ScanSection).\n\n- Rich ARM64 fixture (arm64rich.s/.o, 42 diverse instructions) via llvm-mc\n- LlvmObjdumpArm64Oracle: parses addr/word/mnemonic, reverses big-endian\n  word to little-endian file-order hex for raw-byte comparison\n- Arm64DisassemblyDifferentialTests: address-set, raw-word and exact\n  mnemonic equality against the fixture\n- Arm64DisassemblerTests: b.cond regression test (no LLVM dependency)\n- docs/differential-testing.md and fixtures README updated\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T18:53:20-03:00",
          "tree_id": "daa91ac1fd19777530b282e7eb813c1e41311fbb",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8227247d82a1d82a6f52d3a97b0b9179b19ee791"
        },
        "date": 1781042580739,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 12220688724.166666,
            "unit": "ns",
            "range": "± 17501393.643658318"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 29015821.29963235,
            "unit": "ns",
            "range": "± 578777.2714335114"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 32.350051283836365,
            "unit": "ns",
            "range": "± 0.06397622514810963"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 502778.61169433594,
            "unit": "ns",
            "range": "± 15404.362420827314"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20597.827033409707,
            "unit": "ns",
            "range": "± 122.26815626725624"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.603405650456747,
            "unit": "ns",
            "range": "± 0.15471504795122742"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "33fecbd8e883ea5d90b0c8efcc4f59175c0c1d98",
          "message": "sec: enforce trusted-path allowlist for untrusted path hints (#109) (#115)\n\nHonor the cross-MCP handoff contract's \"Path hints are untrusted\" rule:\nevery filesystem path arriving off the wire is now canonicalised (symlinks\nand junctions resolved, `..` flattened) and, when enforcement is enabled,\nchecked against an allowlist of trusted roots before any file is opened.\n\n- Add PathCanonicalizer (ResolveRealPath + boundary-aware IsUnderAllowedRoot),\n  PathAccessPolicy (Validate choke point, Permissive default) and\n  PathPolicyBuilder (operator roots ∪ well-known roots).\n- New `path_not_allowed` error kind (no published kind repurposed).\n- Wire validation into NativeBinaryRegistry.Load/RegisterHint and every tool\n  entry point (load, import manifest, disassemble imagePath/ilMapPath, and the\n  get_size_breakdown/explain_retention sidecar overrides AND their defaults).\n- Guard the implicit `.map` sidecar merge against a symlink escaping the\n  already-trusted binary directory.\n- Containment is case-insensitive only on Windows; case-sensitive elsewhere\n  (including case-sensitive macOS volumes) to avoid a containment bypass.\n- Enforcement is opt-in: permissive by default (still canonicalises) with a\n  one-time startup warning; enforces once an operator configures a root via\n  NativeMcp:AllowedBinaryRoots / NATIVE_MCP_ALLOWED_ROOTS / BINARIES_DIR.\n- Document the model in docs/handoff-contract.md and README.md.\n\nCloses #109\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T20:09:31-03:00",
          "tree_id": "9a120da82bc07d7b68df9d12d58190a0aadc546d",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/33fecbd8e883ea5d90b0c8efcc4f59175c0c1d98"
        },
        "date": 1781047213436,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 12110700498.538462,
            "unit": "ns",
            "range": "± 12989588.820459956"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28531916.783333335,
            "unit": "ns",
            "range": "± 483370.9012371691"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 32.11463366563503,
            "unit": "ns",
            "range": "± 0.12815394958653623"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 478185.6532689145,
            "unit": "ns",
            "range": "± 10131.240035389781"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20449.63459777832,
            "unit": "ns",
            "range": "± 49.125331289711504"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.052001092831294,
            "unit": "ns",
            "range": "± 0.11359511767669354"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "c1f92df80c034440ef681b516e1cc6716955aeec",
          "message": "test: add Mach-O nlist symbol differential harness vs llvm-readobj (#116)\n\nClose the Mach-O differential coverage gap: the oracle harness previously\nchecked only section geometry, leaving the nlist symbol reader unverified\nagainst an independent tool.\n\n- LlvmReadobjOracle.TryReadMachOSymbols parses `llvm-readobj --syms` into a\n  multiset of (name, n_value), mirroring MachOReader's emission exactly:\n  excludes undefined (Type: Undef) and STAB/debug entries, includes the\n  non-N_UNDF defined classes (Section/Absolute/Indirect), and strips the macOS\n  leading `_`.\n- MachOSymbolDifferentialTests compares MachOReader symbols vs the oracle on the\n  x64, arm64, and arm64rich committed fixtures (skips cleanly without LLVM).\n- A symbol-specific name regex captures the whole name and strips only the\n  trailing ` (N)` string-table-index gloss, so a symbol name containing spaces\n  is not truncated.\n- Document the new surface and comparison strategy in docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T22:12:35-03:00",
          "tree_id": "621e8aa04934d7cfc515a8a031a3505ca6dd0062",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/c1f92df80c034440ef681b516e1cc6716955aeec"
        },
        "date": 1781054616337,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 11576922062.5,
            "unit": "ns",
            "range": "± 99739782.71875545"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 25919904.633333333,
            "unit": "ns",
            "range": "± 382139.8191074185"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 27.64631255183901,
            "unit": "ns",
            "range": "± 0.32999720955374445"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 1508953.6221590908,
            "unit": "ns",
            "range": "± 770549.88991719"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 19367.17646891276,
            "unit": "ns",
            "range": "± 143.7498387813973"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 19.649578001101812,
            "unit": "ns",
            "range": "± 0.31406324674945074"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "616be79dda9adaf108d231f4a8b4674b68d8a78b",
          "message": "fix(r2r): correct ReadyToRunSectionType enum to match readytorun.h (#117)\n\nThe ReadyToRunSectionType enum was fabricated with incorrect values\n(RuntimeFunctions = 5 plus fictional low/high-numbered members). The\nauthoritative runtime header src/coreclr/inc/readytorun.h defines every\nR2R section type in the 100+ range, with RuntimeFunctions = 102.\n\nBecause the reader searched for section type 5, list_r2r_runtime_functions\nALWAYS returned r2r_section_not_present for real .NET 8/9/10 R2R images —\nthe primary handoff target. Verified empirically: the .NET 10\nSystem.Private.CoreLib.dll R2R image (v16.0) carries section type 102 with\n50471 valid x64 RUNTIME_FUNCTION entries. The earlier premise that modern\nR2R replaced RuntimeFunctions with \"MethodHeaderAndCodeInfo (type 105)\" was\nfalse — type 105 is DebugInfo; RuntimeFunctions is alive at type 102.\n\nChanges:\n- Rewrite ReadyToRunSectionType.cs to mirror readytorun.h exactly, removing\n  all fabricated members (incl. the fictional MethodHeaderAndCodeInfo = 105).\n- RawDisassembler: gate the decode-probe arch fallback on RuntimeFunctions\n  being absent (was gated on the fictional MethodHeaderAndCodeInfo section).\n- Fix error messages and tool/XML-doc descriptions referencing type 5/105.\n- Update synthetic R2R PE test builders to write the corrected section type.\n- Add real-image regression tests (RuntimeFunctions_SectionType_Is102 and\n  ReadRuntimeFunctions_RealR2RImage_ReturnsDecodableEntries) that would have\n  caught the bug; they skip gracefully when no R2R fixture is available.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T09:02:20-03:00",
          "tree_id": "e800b2b3e8258f5f6c06a6a234aa69cf0cc338b2",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/616be79dda9adaf108d231f4a8b4674b68d8a78b"
        },
        "date": 1781093552689,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 11877351717.266666,
            "unit": "ns",
            "range": "± 59523729.890686795"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 27464865.667410713,
            "unit": "ns",
            "range": "± 184708.2489219744"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 32.06015139023463,
            "unit": "ns",
            "range": "± 0.15935814392391587"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 460871.41399739584,
            "unit": "ns",
            "range": "± 8500.026798337329"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20271.984228515626,
            "unit": "ns",
            "range": "± 161.4506887635486"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 21.953592936197918,
            "unit": "ns",
            "range": "± 0.14099365289068425"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8f7a2b4f3ea26fbe3a6466f5d191175e93c4c95d",
          "message": "test(r2r): add differential harness for RuntimeFunctions vs PE exception directory (#118)\n\nAdds regression coverage for the R2R RuntimeFunctions reader using the\nestablished differential-testing harness. In a crossgen2 R2R image the\nRuntimeFunctions section (type 102) IS the PE exception data directory\n(.pdata) — identical RVA and size — giving two independent paths to the\nsame table:\n\n- ReadyToRunReader: managed-native header -> R2R signature -> section 102.\n- A battle-tested PE reader: optional-header data directories.\n\nThe two paths must agree; that invariant is exactly what the section-type\nenum bug (RuntimeFunctions mis-mapped to type 5) violated, so this guards\nagainst regression.\n\n- LlvmReadobjOracle.TryReadPeExceptionDirectory: parses the exception data\n  directory from `llvm-readobj --file-headers` (decoded regardless of the\n  CoreCLR per-OS machine override, e.g. 0xFD1D on linux-x64).\n- R2RRuntimeFunctionsDifferentialTests: location match vs llvm-readobj, plus\n  an in-process independent PEReader decode of the first 32 x64\n  RUNTIME_FUNCTION rows compared against ReadRuntimeFunctions. Both no-op\n  when the fixture is unbuilt (and the location test when llvm-readobj is\n  absent).\n- docs/differential-testing.md: matrix row + explanatory note.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T09:52:12-03:00",
          "tree_id": "1746b2283790530b537795d55f357ba50907c3b4",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8f7a2b4f3ea26fbe3a6466f5d191175e93c4c95d"
        },
        "date": 1781096549462,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 12927942825.285715,
            "unit": "ns",
            "range": "± 20922106.266511466"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 26474462.355769232,
            "unit": "ns",
            "range": "± 81067.29963553238"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 31.241214563449223,
            "unit": "ns",
            "range": "± 0.018872141226937242"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 358504.7840820312,
            "unit": "ns",
            "range": "± 4307.298707825345"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 22173.75714111328,
            "unit": "ns",
            "range": "± 33.176042583452855"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.296212399235138,
            "unit": "ns",
            "range": "± 0.02246174739050758"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "61c0bace78e1d4f4481cedcee73192eeeb75a120",
          "message": "feat(r2r): decode ReadyToRun header flags in get_r2r_header (#119)\n\nThe get_r2r_header tool previously surfaced only the raw uint Flags value.\nThis adds decoding of the set bits into their READYTORUN_FLAG_* names so\ncallers can tell at a glance whether an image is a composite Component,\nPartial, EmbeddedMsil, has StrippedIlBodies, etc. — without manually\ndecoding the bitmask.\n\n- ReadyToRunHeaderAttributes: new [Flags] enum mirroring ReadyToRunFlag in\n  coreclr/inc/readytorun.h (12 flags, 0x1..0x800). Named with the BCL flags-\n  enum convention (cf. TypeAttributes) to satisfy CA1711.\n- DecodeNames(uint): decodes set bits to names; any bit not covered by a\n  known flag is reported as a single Unknown(0x...) entry so information is\n  never silently dropped.\n- R2RHeaderResult gains FlagsHex (e.g. \"0x00000003\") and FlagNames; the tool\n  summary lists the decoded names. Raw Flags is retained for back-compat.\n- Tests: Core decode unit tests (single/multiple/all-known/unknown-residue),\n  a real-image round-trip regression (decoded names must re-OR back to the\n  raw flags), plus a synthetic server-tool assertion.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T10:25:24-03:00",
          "tree_id": "8e9351180348ab1179bc6370ff3600978fc2af67",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/61c0bace78e1d4f4481cedcee73192eeeb75a120"
        },
        "date": 1781098511657,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13311188843.642857,
            "unit": "ns",
            "range": "± 45034921.47125282"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 27955104.5125,
            "unit": "ns",
            "range": "± 249306.28564376294"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 32.320960712432864,
            "unit": "ns",
            "range": "± 0.13183088098374293"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 446631.8107910156,
            "unit": "ns",
            "range": "± 11203.165131415693"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 22633.49169108073,
            "unit": "ns",
            "range": "± 121.18486412787733"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.43360763149602,
            "unit": "ns",
            "range": "± 0.13674195754132235"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "d7bf24d1d7f8b0b4613dc656c18abff0ba37437b",
          "message": "feat(r2r): decode ImportSections (type 101) behind includeImportSections (#120)\n\nAdd structural decoding of the R2R ImportSections section (type 101) —\neach READYTORUN_IMPORT_SECTION (20-byte) entry is decoded into RVA/size,\ndecoded Type and Flags, EntrySize, and the Signatures/AuxiliaryData RVAs.\nIndividual fixup signatures are intentionally not decoded (would require\nInternal.TypeSystem — out of scope).\n\nExposed via a new `includeImportSections=false` parameter on the existing\n`get_r2r_header` tool (respecting the hard tool budget — no new tool).\n`R2RHeaderResult.ImportSections` is a nullable additive field, so the\nresponse stays back-compatible. A NextActionHint advertises the parameter\nwhen the section is present but not requested.\n\n`ReadImportSections` validates the declared table byte range against the\nfile size up front (long arithmetic) before allocating, rejecting crafted\nheaders whose Size would otherwise drive a huge allocation or overflow the\nper-entry offset math.\n\nTests: Core decoder/reader unit tests, an oversized-section hardening test,\na real-image regression (System.Private.CoreLib carries a 7-entry section),\nand Server tool tests. 430 Core + 111 Server green, 0 warnings.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:01:52-03:00",
          "tree_id": "f66e403e53ae2e137bda94903c073ed9c5344609",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/d7bf24d1d7f8b0b4613dc656c18abff0ba37437b"
        },
        "date": 1781100745907,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13397430584.4,
            "unit": "ns",
            "range": "± 34312722.74400811"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 26901218.233333334,
            "unit": "ns",
            "range": "± 346370.45170275494"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 31.246219878013317,
            "unit": "ns",
            "range": "± 0.024951192101950136"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 376092.9146484375,
            "unit": "ns",
            "range": "± 4248.8108218435045"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 22278.14901936849,
            "unit": "ns",
            "range": "± 104.68131181502528"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 21.871358613882744,
            "unit": "ns",
            "range": "± 0.06604460617928334"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "46b427752ca9f6531bdb668d75026d578c1fc408",
          "message": "feat(r2r): decode CompilerIdentifier + OwnerCompositeExecutable strings (#121)\n\nDecode the two ReadyToRun identification-string sections into the\nget_r2r_header result:\n- CompilerIdentifier (type 100): the crossgen2 / compiler that produced\n  the image (e.g. \"Crossgen2 10.0.526.1541\").\n- OwnerCompositeExecutable (type 116): the composite executable filename\n  that owns a component image (null for non-composite images).\n\nBoth payloads are a single zero-terminated UTF-8 string; the decoder reads\nSize-1 bytes (excluding the terminator), mirroring\nILCompiler.Reflection.ReadyToRun. Decoding is best-effort auxiliary\nmetadata — ReadSectionUtf8String returns null (never throws) when the\nsection is absent, empty, or its declared range runs past the end of the\nfile, validated with long arithmetic before slicing.\n\nExposed eagerly (no new param — cheap single strings) via two nullable\nadditive fields on R2RHeaderResult, so the response stays back-compatible.\n\nTests: synthetic decode, absent/empty/oversized-size graceful-null cases,\nand a real-image regression (System.Private.CoreLib CompilerIdentifier).\n436 Core + 114 Server green, 0 warnings.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:38:36-03:00",
          "tree_id": "414c1232fa0cd279f23d0c95034d09620443cd72",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/46b427752ca9f6531bdb668d75026d578c1fc408"
        },
        "date": 1781102924324,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 11765882307.5,
            "unit": "ns",
            "range": "± 18177058.807057183"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28294495.28125,
            "unit": "ns",
            "range": "± 588777.4127165396"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 31.160134939047005,
            "unit": "ns",
            "range": "± 0.05166004766272223"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 451387.75948079425,
            "unit": "ns",
            "range": "± 9235.834576413921"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20133.19203186035,
            "unit": "ns",
            "range": "± 42.54044940921475"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 27.226359496514004,
            "unit": "ns",
            "range": "± 0.027929450627132764"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "5ca7a6b34ad4172dfa23f49514b852fc176925f8",
          "message": "Add R2R composite-image metadata decoding (ComponentAssemblies + ManifestAssemblyMvids) (#122)\n\nDecode the composite ReadyToRun structural sections behind a new\nincludeCompositeInfo parameter on get_r2r_header:\n\n- ComponentAssemblies (type 115): array of 16-byte entries\n  {CorHeaderRVA, CorHeaderSize, AssemblyHeaderRVA, AssemblyHeaderSize}.\n- ManifestAssemblyMvids (type 118): array of 16-byte module-version GUIDs.\n\nBoth readers validate the full declared table against the file length up\nfront (long arithmetic) before allocating or indexing, mirroring the\nReadImportSections hardening. Section-absent -> Fail(R2RSectionNotPresent);\nempty -> Ok(empty); oversized -> Fail(InvalidArgument).\n\nSurfaced as two additive nullable fields on R2RHeaderResult plus a new\nR2RComponentAssemblyView record; a NextActionHint is offered when the\nsections are present but not included. No new MCP tool.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:55:31-03:00",
          "tree_id": "d02b4df6c640100450361c6b7fb8196d3aaf7a47",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/5ca7a6b34ad4172dfa23f49514b852fc176925f8"
        },
        "date": 1781103988124,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13378051047.642857,
            "unit": "ns",
            "range": "± 27245972.592006005"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28140038.50892857,
            "unit": "ns",
            "range": "± 405731.6179533193"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 31.599730704511916,
            "unit": "ns",
            "range": "± 0.05054331695969745"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 386684.27677408856,
            "unit": "ns",
            "range": "± 9127.566108810126"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 22722.708064152645,
            "unit": "ns",
            "range": "± 25.02456810749943"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.396705152732984,
            "unit": "ns",
            "range": "± 0.11527336699880074"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e172fd7aae7ba7e6e0a7fae77f6c0518297417f6",
          "message": "feat(r2r): NativeFormat reader primitives (foundation) (#124)\n\nSafe, span-based port of the runtime's Internal.NativeFormat\nvariable-length integer reader — the foundation for decoding the\nNativeFormat-encoded R2R sections that are currently out-of-scope\n(MethodDefEntryPoints, AvailableTypes, ...).\n\nNew (internal) types under src/DotnetNativeMcp.Core/R2R/NativeFormat/:\n- NativePrimitiveDecoder: faithful port of DecodeUnsigned/Signed/\n  UnsignedLong/SignedLong/SkipInteger + fixed-width ReadUInt8/16/32/64,\n  re-expressed over ReadOnlySpan<byte> + a ref-uint cursor. Stricter than\n  the runtime: it bounds-checks the 5/9-byte raw forms, and the fixed-width\n  reads use non-overflowing (end - offset < N) checks so a near-uint.MaxValue\n  offset is rejected as NativeFormatException rather than wrapping.\n- NativeReader: bounds-checked random-access wrapper over a section blob.\n- NativeParser: forward cursor.\n- NativeFormatException: internal sentinel; tool-facing readers (PR2/PR3)\n  will catch it -> Fail(InvalidArgument) so tools never throw.\n\nNo tool wiring yet (foundation only). Comprehensive round-trip, 5000-iter\nfuzz, encoding-width, fixed-width-LE, truncation/OOB and wrapped-offset\nhardening tests (114 NativeFormat tests).\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T13:51:53-03:00",
          "tree_id": "b040a7f6d9882f96b1d78b625931fa2395d7b78c",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/e172fd7aae7ba7e6e0a7fae77f6c0518297417f6"
        },
        "date": 1781110939268,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13123612293.266666,
            "unit": "ns",
            "range": "± 16474803.478189139"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 29593192.439583335,
            "unit": "ns",
            "range": "± 501609.09701737604"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 33.88098505338033,
            "unit": "ns",
            "range": "± 0.08581635717850274"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 315913.66399739584,
            "unit": "ns",
            "range": "± 5338.365594790978"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 9856.454182942709,
            "unit": "ns",
            "range": "± 36.421426405389006"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.663659818967183,
            "unit": "ns",
            "range": "± 0.06961868340083914"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "0750b47dbb57b21278a053b9d42c056b1086c715",
          "message": "Add NativeArray + MethodDefEntryPoints (type 103) R2R decode (#125)\n\nPR2 of the NativeFormat reader epic. Ports the runtime's sparse\nindex-addressable NativeArray (16-element-block bit-tree) and decodes the\nMethodDefEntryPoints section, mapping each present MethodDef RID to its\nentry-point RUNTIME_FUNCTION index and hasFixups flag. Rides on the existing\nget_r2r_header tool via additive includeMethodEntryPoints /\nmethodEntryPointsLimit params and a nullable result field (no new tool).\n\nHardening:\n- Bound the decode loop with a 2,000,000-slot scan cap so a crafted section\n  advertising a huge untrusted Count backed by an in-bounds all-absent index\n  cannot spin unbounded; over-cap results are flagged Truncated.\n- DecodeMethodEntryPoint now consumes the trailing delta-encoded fixup offset\n  when the id marks it (id & 2), faithfully mirroring the runtime's\n  GetRuntimeFunctionIndexFromOffset so truncated fixup entries fail with\n  InvalidArgument instead of being silently accepted.\n\nTests: synthetic end-to-end decode, limit/truncation, fixup-delta consume and\ntruncated-delta failure, bounded-scan regression, and a real\nSystem.Private.CoreLib regression cross-checking every entry against the\nRuntimeFunctions bound.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T14:29:56-03:00",
          "tree_id": "8ac0bfc4ba2e6c6a2f223606e6363f25fe77e0a5",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/0750b47dbb57b21278a053b9d42c056b1086c715"
        },
        "date": 1781113184466,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13243129469.266666,
            "unit": "ns",
            "range": "± 19422099.404040515"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 26260446.908653848,
            "unit": "ns",
            "range": "± 200702.93289029485"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 30.928715910230363,
            "unit": "ns",
            "range": "± 0.03653395590905127"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 368182.25419921876,
            "unit": "ns",
            "range": "± 4636.242434881507"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 22162.63275349935,
            "unit": "ns",
            "range": "± 61.16004937076116"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 23.41044707596302,
            "unit": "ns",
            "range": "± 0.02613676352154744"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "1c0c9d2b9308893af9deaeabbab6c40a006b0ab2",
          "message": "Decode R2R AvailableTypes (type 108) via NativeFormat hashtable (#126)\n\nPR3 of the NativeFormat-reader epic. Adds a safe, span-based port of the\nruntime's Internal.NativeFormat.NativeHashtable and wires it to decode the\nReadyToRun AvailableTypes section (type 108).\n\nEach hashtable entry yields a metadata RID whose low bit flags ExportedType\n(table 0x27) vs TypeDef (table 0x02); the RID is widened into a full metadata\ntoken for handoff to dotnet-assembly-mcp's get_type. Type names are not\nresolved here (that needs managed ECMA metadata, out of scope).\n\nRides additively on get_r2r_header via includeAvailableTypes /\navailableTypesLimit (no new tool — tool budget hard cap of 10). The bucket\ncount comes from untrusted header bytes, so the traversal is bounded by a\nmaxScan step cap that flags Truncated rather than spinning on a crafted huge\nbucket count. Out-of-range RIDs (0 or > 0x00FFFFFF) fail as InvalidArgument\ninstead of corrupting the synthesised token.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:01:55-03:00",
          "tree_id": "3b6f0110ef066b5490b8f4e8fd3137b2ebcfc8e8",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/1c0c9d2b9308893af9deaeabbab6c40a006b0ab2"
        },
        "date": 1781115162577,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13127016180,
            "unit": "ns",
            "range": "± 43522462.465394706"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28317847.966666665,
            "unit": "ns",
            "range": "± 450746.9640468306"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 30.40364300409953,
            "unit": "ns",
            "range": "± 0.08165775344453201"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 314290.85894097225,
            "unit": "ns",
            "range": "± 8576.936365029253"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 9680.093463134766,
            "unit": "ns",
            "range": "± 116.04758158791462"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 23.730182268222173,
            "unit": "ns",
            "range": "± 0.11669480333404243"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "f3ac70dc7cc049c69cb93f7b52226e64a4d85318",
          "message": "feat(r2r): decode V9 RID-indexed info maps (121/122/123) (#127)\n\nDecode the three .NET 9 (R2R v9.0) info-map sections, riding additively on\nget_r2r_header via a single includeInfoMaps flag (capped by infoMapsLimit):\n\n- EnclosingTypeMap (122): u16 count + u16[] enclosing RIDs; emit nested→\n  enclosing TypeDef token pairs (skip top-level / RID 0).\n- MethodIsGenericMap (121): i32 count + ceil(count/8) MSB-first bit array;\n  emit MethodDef tokens for set bits; count all generic methods past the\n  limit and flag truncation.\n- TypeGenericInfoMap (123): u32 count + ceil(count/2) nibbles (even index in\n  the high nibble); emit per-type generic arity / variance / constraints for\n  generic types only.\n\nAll three are fixed-width, little-endian, dependency-free decodes over a\nbounds-validated section slice (shared MapSectionBytes helper). Counts are\nread from untrusted bytes and validated against the section size before the\ndecode loop (overflow-safe widened arithmetic), so malformed input surfaces\nas InvalidArgument rather than throwing. Tokens are emitted for handoff to\ndotnet-assembly-mcp; names are not resolved.\n\nTests: 12 Core (incl. real-image SPC regression + int.MaxValue overflow\nguard) + 4 Server. README coverage table updated.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:28:36-03:00",
          "tree_id": "41674796a8344bfa5fd559b6b992b685c3dd0670",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/f3ac70dc7cc049c69cb93f7b52226e64a4d85318"
        },
        "date": 1781116727368,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13072218232.333334,
            "unit": "ns",
            "range": "± 23237723.08767736"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 26304149.114583332,
            "unit": "ns",
            "range": "± 91549.33219206608"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 31.133968170483907,
            "unit": "ns",
            "range": "± 0.06457530482768652"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 363958.39404296875,
            "unit": "ns",
            "range": "± 6285.59120888375"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 22153.621810913086,
            "unit": "ns",
            "range": "± 26.26264754303433"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.379363824214256,
            "unit": "ns",
            "range": "± 0.03971142321524758"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "fa98888c7cf785061513a9924563b9ac1f5c6f99",
          "message": "feat(r2r): surface ManifestMetadata (112) ECMA blob handoff descriptor (#128)\n\nThe ManifestMetadata section content is an embedded ECMA-335 metadata blob\n(the R2R manifest of referenced assemblies). Rather than decode the managed\nmetadata (dotnet-assembly-mcp's job), surface a handoff descriptor:\n\n- file offset / RVA / size of the blob\n- BSJB signature validation\n- parsed metadata-root header (ECMA-335 II.24.2.1): version string +\n  stream directory (#~, #Strings, #US, #GUID, #Blob)\n\nRides additively on get_r2r_header via a single includeManifestMetadata flag\n+ nullable ManifestMetadata field (tool budget held at 10). Every read is\nbounds-checked over the section slice with overflow-safe arithmetic; the\nversion string must be null-terminated and every stream-name padding must fit\nthe blob, so malformed/truncated input surfaces as InvalidArgument rather\nthan throwing.\n\nTests: 9 Core (incl. real-image SPC regression, bad signature, truncated\nheader, oversized version length, non-null-terminated version, truncated\nstream-name padding, stream-count-beyond-blob) + 2 Server. README updated.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:48:30-03:00",
          "tree_id": "07b4beaa1606398e128f8e832434a22108bf4f5b",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/fa98888c7cf785061513a9924563b9ac1f5c6f99"
        },
        "date": 1781117888492,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 11953581729.866667,
            "unit": "ns",
            "range": "± 37214713.76780606"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 27764218.37723214,
            "unit": "ns",
            "range": "± 415096.6646351784"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 31.91224692662557,
            "unit": "ns",
            "range": "± 0.26784804432953213"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 441726.3515082465,
            "unit": "ns",
            "range": "± 8988.040953641597"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 20125.13312639509,
            "unit": "ns",
            "range": "± 122.07977093783629"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.254019044912777,
            "unit": "ns",
            "range": "± 0.08667627080145117"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "bd8e9a0aca3c41bf757e0be8bc9a05b5ca5df912",
          "message": "feat(r2r): decode HotColdMap (type 120) hot/cold pairs (#129)\n\nAdds ReadyToRunReader.ReadHotColdMap which decodes the HotColdMap section\ninto (cold, hot) RUNTIME_FUNCTION index pairs (flat uint[], pairCount =\nsize/8). Wired additively into get_r2r_header via includeHotColdMap + a\nnullable R2RHotColdMapView result field; capped by infoMapsLimit. Never\nthrows to the tool layer (InvalidArgument on non-pair-aligned/empty\nsections, R2RSectionNotPresent when absent).\n\nAdds 5 Core + 2 Server tests (synthetic-only — SPC has no HotColdMap).\nUpdates README coverage table + tool description; removes HotColdMap from\nthe out-of-scope list.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:59:35-03:00",
          "tree_id": "c8d3c9165c14df8dc0b37bec3c8f6e69008da649",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/bd8e9a0aca3c41bf757e0be8bc9a05b5ca5df912"
        },
        "date": 1781118555722,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SampleAot\")",
            "value": 13072802069.333334,
            "unit": "ns",
            "range": "± 36505675.3645878"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SampleAot\")",
            "value": 28764641.51785714,
            "unit": "ns",
            "range": "± 243530.58401635883"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SampleAot\")",
            "value": 30.78323651277102,
            "unit": "ns",
            "range": "± 0.06623762938383015"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.Cold(Input: \"SystemPrivateCoreLib\")",
            "value": 340455.4959960937,
            "unit": "ns",
            "range": "± 7377.776798542753"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL2(Input: \"SystemPrivateCoreLib\")",
            "value": 9863.552230834961,
            "unit": "ns",
            "range": "± 61.06771133810872"
          },
          {
            "name": "DotnetNativeMcp.Bench.FindNativeCallersBench.WarmL1(Input: \"SystemPrivateCoreLib\")",
            "value": 22.97657973567645,
            "unit": "ns",
            "range": "± 0.06139689747717536"
          }
        ]
      }
    ],
    "Disassemble Benchmark": [
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "ef20124ae8299193a0a95950588e2f7b178cc5b7",
          "message": "Fix benchmark branch bootstrap (#87)\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-21T13:01:05-03:00",
          "tree_id": "2f4cdfe7c82d510e3066501d61b91484c8526e8b",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/ef20124ae8299193a0a95950588e2f7b178cc5b7"
        },
        "date": 1779379948385,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7439446.209895833,
            "unit": "ns",
            "range": "± 90300.52078337714"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 102.73519719309277,
            "unit": "ns",
            "range": "± 2.2284241740842443"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "7299bed916595372a444b83cfbb2d42332510ecb",
          "message": "Bump GitHub Actions to Node 24-based majors (#90) (#91)\n\nEliminates the Node 20 deprecation warnings emitted on every workflow\nrun since the runner started warning. Node 20 will be removed from\nGitHub-hosted runners on 2026-09-16; default flips to Node 24 on\n2026-06-02.\n\nUpgrade map (chosen to be minimal Node-24 bumps, skipping majors that\nintroduce ESM or other API breaks we don't need):\n- actions/checkout v4 → v5\n- actions/setup-dotnet v4 → v5\n- actions/cache v4 → v5\n- actions/upload-artifact v4 → v6\n- actions/download-artifact v4 → v7\n- actions/attest-build-provenance v2 → v4\n- softprops/action-gh-release v2 → v3\n\nCross-repo mirror to be opened in dotnet-assembly-mcp and\ndotnet-diagnostics-mcp after this PR validates the pattern.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T11:17:11-03:00",
          "tree_id": "cd525c4877e6b09cc6cf0f13d29477b74bffefcd",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/7299bed916595372a444b83cfbb2d42332510ecb"
        },
        "date": 1779460120129,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7540451.593229166,
            "unit": "ns",
            "range": "± 80933.61764603917"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 87.67594785349709,
            "unit": "ns",
            "range": "± 1.450744479487887"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "9c30cf9d9b2e256846cb8a871eeed46b23bd86d0",
          "message": "Replace dynamic permissions expression in bench.yml with static value (#107)\n\nPR #92 set the bench job's permissions to\n`contents: ${{ github.event_name == 'push' && 'write' || 'read' }}`.\nGitHub Actions does not support expressions in the `permissions:` map\n(values must be literal read/write/none), so the workflow file failed\nparse and every bench run since the #92 merge completed in 0 seconds\nwith 'This run likely failed because of a workflow file issue'. No\nBenchmarkDotNet history has been published to gh-pages since v0.5.4.\n\nThis hotfix reverts to a static `contents: write` on the bench job.\nThe workflow-level `contents: read` default added by #92 stays in\nplace, so the regression vs. the pre-#92 state is strictly tighter at\nworkflow scope. PR exposure is documented honestly in the comment and\nthe proper fix (split push vs. PR into two jobs with static\npermissions) is tracked in #106.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T15:09:18-03:00",
          "tree_id": "16887e2c426a2056972ec16157125175d0a35b85",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/9c30cf9d9b2e256846cb8a871eeed46b23bd86d0"
        },
        "date": 1779474010510,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7045576.471153846,
            "unit": "ns",
            "range": "± 68309.10863946997"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 95.5113714507648,
            "unit": "ns",
            "range": "± 1.4436335324465048"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8764ef474031923558cf29b051bdf601160ece56",
          "message": "Split bench into push vs. PR jobs with static least-privilege permissions (#108)\n\nCloses #106.\n\nThe hotfix in #107 restored bench.yml to a single job with a static\n`contents: write` permission so the YAML would parse, at the cost of\ngranting maintainer-labeled `perf` PRs a writable GITHUB_TOKEN. The\nbenchmark-action's `auto-push` step is gated on push events, but\nPR-controlled `dotnet run` code in the bench fixtures could in\nprinciple use the token before that gate. #106 tracked the structural\nfix.\n\nThis change introduces a composite action under\n`.github/actions/run-bench/` that owns the setup, build, run and\nstorage steps. The workflow now defines two jobs with complementary\nevent filters and statically declared permissions:\n\n- bench-push: `if: github.event_name != 'pull_request'` (covers push +\n  workflow_dispatch), `permissions: contents: write`, calls the\n  composite with `auto-push: 'true'` / `fail-on-alert: 'false'`.\n- bench-pr: `if: github.event_name == 'pull_request' &&\n  contains(labels, 'perf')`, `permissions: contents: read`, calls the\n  composite with `auto-push: 'false'` / `fail-on-alert: 'true'`.\n\nPR-controlled bench code therefore runs under a strictly read-only\nGITHUB_TOKEN and the benchmark history can only be advanced by the\npush baseline path. GitHub Actions still does not allow expressions in\nthe `permissions:` map, so the split is enforced at the job level via\nevent filtering instead.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T15:40:41-03:00",
          "tree_id": "9c36fdab158009ac152e50c99d84947723a2d5b4",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8764ef474031923558cf29b051bdf601160ece56"
        },
        "date": 1779475854339,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7803536.038541666,
            "unit": "ns",
            "range": "± 107466.99269266399"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 102.2530642802065,
            "unit": "ns",
            "range": "± 3.897783869463223"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "aa2e0c221ae3d34514d4d5a938f683feedc5e6b5",
          "message": "test: add differential (oracle) harness for the ELF reader vs readelf (#110)\n\nAdds a differential test harness that cross-checks ElfReader against GNU\nreadelf, complementing the existing fuzz harness (which only proves the\nparsers never throw) with a correctness oracle.\n\nSurfaces covered:\n- symbols (readelf -sW): per-index name, value, size, function flag + count\n- sections (readelf -SW): per-name virtual address, file offset, size\n- imports: DT_NEEDED libraries (readelf -dW) and undefined .dynsym symbols\n\nNotes:\n- Shared ReadelfOracle helper shells out to readelf and parses its wide\n  (-W) output; drains stdout/stderr asynchronously with an effective\n  timeout. Symbol Size is decimal in -sW; section geometry is hex in -SW.\n- Tests no-op when readelf or the NativeAOT fixture are unavailable, so\n  the suite stays green on hosts without binutils. CI (ubuntu-latest) has\n  readelf and builds the fixture, so the comparison runs for real.\n- Version suffixes (@GLIBC_x.y) are normalized on both sides; SHT_NOBITS\n  FileSize is not checked; duplicate section names fall back to a\n  geometry-existence match.\n\nSee docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T11:59:55-03:00",
          "tree_id": "9a7a776f87447e5bac73499ce540ffc6550d3e39",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/aa2e0c221ae3d34514d4d5a938f683feedc5e6b5"
        },
        "date": 1781017835400,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 6809153.075334822,
            "unit": "ns",
            "range": "± 35593.27979282391"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 93.7999546783311,
            "unit": "ns",
            "range": "± 0.3435435713224244"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "f26222daedc074587159bc8e849e72b615bd904d",
          "message": "test: add differential (oracle) harness for the x86/x64 disassembler vs objdump (#111)\n\nCross-checks IcedDisassembler (via RawDisassembler) against GNU\nobjdump -d -M intel, extending the differential-testing approach from the\nELF reader to the decoder.\n\nThe hard oracle is instruction-boundary + raw-byte agreement: two\nindependent decoders walking the same bytes must segment them identically.\nFor each .text function symbol in the SampleAot fixture the harness\ndisassembles the body both ways and asserts the in-range instruction\naddress SETS are equal (catching early-stop bugs in either decoder), then\nasserts identical raw bytes per address. Mnemonics are compared as a\nsofter signal after normalizing objdump's display (segment/rep/lock/REX\nprefix tokens stripped, movabs->mov, nop/xchg NOP family treated as one).\n\n- New OracleProcess shared process runner (concurrent stdout/stderr drain,\n  timeout, missing-tool skip); ReadelfOracle now delegates to it.\n- objdump is invoked with --insn-width=15 so long instructions never wrap\n  their byte column onto a continuation line.\n- Tests no-op when objdump or the fixture are unavailable; CI has both.\n\nSee docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T15:32:23-03:00",
          "tree_id": "eff6c022b7334406dd3f5f29069b7f0758ed7a30",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/f26222daedc074587159bc8e849e72b615bd904d"
        },
        "date": 1781030616742,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7256581.925520834,
            "unit": "ns",
            "range": "± 100911.92191220418"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 100.2676835963803,
            "unit": "ns",
            "range": "± 3.083977157053974"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e05f1f566372189956bd51c694de515e7c0a6291",
          "message": "test: add differential (oracle) harness for the PE reader vs llvm-readobj (#112)\n\nCross-checks PeNativeReader's section table against LLVM\nllvm-readobj --sections, extending the differential-testing approach to\nthe PE reader (after the ELF reader and the x86/x64 disassembler).\n\nFor every section the harness asserts the name set is equal and the\ngeometry matches exactly: virtual address, virtual size, file offset, and\nfile size. PeNativeReader emits the full COFF section table with no\nfiltering, so unlike the ELF section comparison this asserts the complete\nset; duplicate section names fall back to a geometry-existence match.\n\n- New LlvmReadobjOracle: stateful parser over `Section { ... }` blocks;\n  hex for 0x-prefixed addresses/offsets, decimal for sizes.\n- Primary target is the always-present managed DotnetNativeMcp.Core.dll, so\n  the comparison runs everywhere instead of skipping on a missing fixture; a\n  second test additionally exercises the published ReadyToRun\n  System.Private.CoreLib.dll (the real asm-mcp -> native-mcp handoff target).\n- CI installs `llvm` so llvm-readobj is present and the comparison runs for\n  real rather than skipping.\n\nMach-O is intentionally not covered yet: the repo has no real Mach-O fixture\non disk to point an oracle at. See docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T15:57:48-03:00",
          "tree_id": "bd5f772bbcdc59b3a22cf3f3fbdb7a9288a726fd",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/e05f1f566372189956bd51c694de515e7c0a6291"
        },
        "date": 1781032120059,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7958431.369419643,
            "unit": "ns",
            "range": "± 87977.8640753368"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 93.33426185577146,
            "unit": "ns",
            "range": "± 2.6947321391778862"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "b1e569eff2db61ac03701358946b70485ca20a02",
          "message": "test: add Mach-O section differential harness vs llvm-readobj (#113)\n\nCompletes the ELF/PE/Mach-O differential (oracle) triad. Parses tiny\ncommitted Mach-O relocatable objects (x86_64 + arm64) both with MachOReader\nand with llvm-readobj --sections, then asserts per-section geometry agrees\n(virtual address, virtual size, file offset, file size).\n\nRelocatable .o objects are used as fixtures because MachOReader rejects\nLC_DYLD_CHAINED_FIXUPS (present in linked dylibs/executables); a .o never\ncarries chained fixups so it round-trips through the reader. Fixtures are\ncommitted (not built at test time) so only the oracle (llvm-readobj) is\nneeded and the test skips cleanly when LLVM is absent.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T16:53:45-03:00",
          "tree_id": "eb0dcf4ef00cf1278cd8f152c020f603e0c67548",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/b1e569eff2db61ac03701358946b70485ca20a02"
        },
        "date": 1781035468935,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 6007056.465104166,
            "unit": "ns",
            "range": "± 93314.01056588371"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 79.18147502626691,
            "unit": "ns",
            "range": "± 1.0274964940637403"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8227247d82a1d82a6f52d3a97b0b9179b19ee791",
          "message": "test: add ARM64 disassembly differential harness vs llvm-objdump (#114)\n\nCompletes disassembler oracle coverage: Arm64Disassembler is now compared\nagainst llvm-objdump the same way x86/x64 is compared against GNU objdump.\n\nBuilding the harness surfaced a real production bug, fixed here:\nInstructionView.Mnemonic was sourced from instr.Mnemonic.ToText(false),\nwhich collapses every B.cond (b.eq, b.ne, ...) to a bare 'b', dropping the\ncondition suffix. Mnemonic and operands are now both derived from a single\ninstr.TryFormat pass via FormatMnemonicAndOperands, preserving the suffix.\nApplied at both call sites (Disassemble and ScanSection).\n\n- Rich ARM64 fixture (arm64rich.s/.o, 42 diverse instructions) via llvm-mc\n- LlvmObjdumpArm64Oracle: parses addr/word/mnemonic, reverses big-endian\n  word to little-endian file-order hex for raw-byte comparison\n- Arm64DisassemblyDifferentialTests: address-set, raw-word and exact\n  mnemonic equality against the fixture\n- Arm64DisassemblerTests: b.cond regression test (no LLVM dependency)\n- docs/differential-testing.md and fixtures README updated\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T18:53:20-03:00",
          "tree_id": "daa91ac1fd19777530b282e7eb813c1e41311fbb",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8227247d82a1d82a6f52d3a97b0b9179b19ee791"
        },
        "date": 1781042582124,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 8534014.519791666,
            "unit": "ns",
            "range": "± 139045.6216925693"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 97.13880992787224,
            "unit": "ns",
            "range": "± 1.5805919158676793"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "33fecbd8e883ea5d90b0c8efcc4f59175c0c1d98",
          "message": "sec: enforce trusted-path allowlist for untrusted path hints (#109) (#115)\n\nHonor the cross-MCP handoff contract's \"Path hints are untrusted\" rule:\nevery filesystem path arriving off the wire is now canonicalised (symlinks\nand junctions resolved, `..` flattened) and, when enforcement is enabled,\nchecked against an allowlist of trusted roots before any file is opened.\n\n- Add PathCanonicalizer (ResolveRealPath + boundary-aware IsUnderAllowedRoot),\n  PathAccessPolicy (Validate choke point, Permissive default) and\n  PathPolicyBuilder (operator roots ∪ well-known roots).\n- New `path_not_allowed` error kind (no published kind repurposed).\n- Wire validation into NativeBinaryRegistry.Load/RegisterHint and every tool\n  entry point (load, import manifest, disassemble imagePath/ilMapPath, and the\n  get_size_breakdown/explain_retention sidecar overrides AND their defaults).\n- Guard the implicit `.map` sidecar merge against a symlink escaping the\n  already-trusted binary directory.\n- Containment is case-insensitive only on Windows; case-sensitive elsewhere\n  (including case-sensitive macOS volumes) to avoid a containment bypass.\n- Enforcement is opt-in: permissive by default (still canonicalises) with a\n  one-time startup warning; enforces once an operator configures a root via\n  NativeMcp:AllowedBinaryRoots / NATIVE_MCP_ALLOWED_ROOTS / BINARIES_DIR.\n- Document the model in docs/handoff-contract.md and README.md.\n\nCloses #109\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T20:09:31-03:00",
          "tree_id": "9a120da82bc07d7b68df9d12d58190a0aadc546d",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/33fecbd8e883ea5d90b0c8efcc4f59175c0c1d98"
        },
        "date": 1781047214529,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 6928264.141666667,
            "unit": "ns",
            "range": "± 50333.74155241396"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 94.18860940535863,
            "unit": "ns",
            "range": "± 0.8359528204734444"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "c1f92df80c034440ef681b516e1cc6716955aeec",
          "message": "test: add Mach-O nlist symbol differential harness vs llvm-readobj (#116)\n\nClose the Mach-O differential coverage gap: the oracle harness previously\nchecked only section geometry, leaving the nlist symbol reader unverified\nagainst an independent tool.\n\n- LlvmReadobjOracle.TryReadMachOSymbols parses `llvm-readobj --syms` into a\n  multiset of (name, n_value), mirroring MachOReader's emission exactly:\n  excludes undefined (Type: Undef) and STAB/debug entries, includes the\n  non-N_UNDF defined classes (Section/Absolute/Indirect), and strips the macOS\n  leading `_`.\n- MachOSymbolDifferentialTests compares MachOReader symbols vs the oracle on the\n  x64, arm64, and arm64rich committed fixtures (skips cleanly without LLVM).\n- A symbol-specific name regex captures the whole name and strips only the\n  trailing ` (N)` string-table-index gloss, so a symbol name containing spaces\n  is not truncated.\n- Document the new surface and comparison strategy in docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T22:12:35-03:00",
          "tree_id": "621e8aa04934d7cfc515a8a031a3505ca6dd0062",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/c1f92df80c034440ef681b516e1cc6716955aeec"
        },
        "date": 1781054617642,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 6651013.96875,
            "unit": "ns",
            "range": "± 109581.73187338514"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 81.13936638063,
            "unit": "ns",
            "range": "± 2.5003927639854933"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "616be79dda9adaf108d231f4a8b4674b68d8a78b",
          "message": "fix(r2r): correct ReadyToRunSectionType enum to match readytorun.h (#117)\n\nThe ReadyToRunSectionType enum was fabricated with incorrect values\n(RuntimeFunctions = 5 plus fictional low/high-numbered members). The\nauthoritative runtime header src/coreclr/inc/readytorun.h defines every\nR2R section type in the 100+ range, with RuntimeFunctions = 102.\n\nBecause the reader searched for section type 5, list_r2r_runtime_functions\nALWAYS returned r2r_section_not_present for real .NET 8/9/10 R2R images —\nthe primary handoff target. Verified empirically: the .NET 10\nSystem.Private.CoreLib.dll R2R image (v16.0) carries section type 102 with\n50471 valid x64 RUNTIME_FUNCTION entries. The earlier premise that modern\nR2R replaced RuntimeFunctions with \"MethodHeaderAndCodeInfo (type 105)\" was\nfalse — type 105 is DebugInfo; RuntimeFunctions is alive at type 102.\n\nChanges:\n- Rewrite ReadyToRunSectionType.cs to mirror readytorun.h exactly, removing\n  all fabricated members (incl. the fictional MethodHeaderAndCodeInfo = 105).\n- RawDisassembler: gate the decode-probe arch fallback on RuntimeFunctions\n  being absent (was gated on the fictional MethodHeaderAndCodeInfo section).\n- Fix error messages and tool/XML-doc descriptions referencing type 5/105.\n- Update synthetic R2R PE test builders to write the corrected section type.\n- Add real-image regression tests (RuntimeFunctions_SectionType_Is102 and\n  ReadRuntimeFunctions_RealR2RImage_ReturnsDecodableEntries) that would have\n  caught the bug; they skip gracefully when no R2R fixture is available.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T09:02:20-03:00",
          "tree_id": "e800b2b3e8258f5f6c06a6a234aa69cf0cc338b2",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/616be79dda9adaf108d231f4a8b4674b68d8a78b"
        },
        "date": 1781093554161,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 6969488.197916667,
            "unit": "ns",
            "range": "± 57777.81696893155"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 90.73028745821544,
            "unit": "ns",
            "range": "± 1.1866732545421075"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8f7a2b4f3ea26fbe3a6466f5d191175e93c4c95d",
          "message": "test(r2r): add differential harness for RuntimeFunctions vs PE exception directory (#118)\n\nAdds regression coverage for the R2R RuntimeFunctions reader using the\nestablished differential-testing harness. In a crossgen2 R2R image the\nRuntimeFunctions section (type 102) IS the PE exception data directory\n(.pdata) — identical RVA and size — giving two independent paths to the\nsame table:\n\n- ReadyToRunReader: managed-native header -> R2R signature -> section 102.\n- A battle-tested PE reader: optional-header data directories.\n\nThe two paths must agree; that invariant is exactly what the section-type\nenum bug (RuntimeFunctions mis-mapped to type 5) violated, so this guards\nagainst regression.\n\n- LlvmReadobjOracle.TryReadPeExceptionDirectory: parses the exception data\n  directory from `llvm-readobj --file-headers` (decoded regardless of the\n  CoreCLR per-OS machine override, e.g. 0xFD1D on linux-x64).\n- R2RRuntimeFunctionsDifferentialTests: location match vs llvm-readobj, plus\n  an in-process independent PEReader decode of the first 32 x64\n  RUNTIME_FUNCTION rows compared against ReadRuntimeFunctions. Both no-op\n  when the fixture is unbuilt (and the location test when llvm-readobj is\n  absent).\n- docs/differential-testing.md: matrix row + explanatory note.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T09:52:12-03:00",
          "tree_id": "1746b2283790530b537795d55f357ba50907c3b4",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8f7a2b4f3ea26fbe3a6466f5d191175e93c4c95d"
        },
        "date": 1781096550721,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7466214.472916666,
            "unit": "ns",
            "range": "± 85603.39734380931"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 84.88604099016923,
            "unit": "ns",
            "range": "± 0.13159301398866882"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "61c0bace78e1d4f4481cedcee73192eeeb75a120",
          "message": "feat(r2r): decode ReadyToRun header flags in get_r2r_header (#119)\n\nThe get_r2r_header tool previously surfaced only the raw uint Flags value.\nThis adds decoding of the set bits into their READYTORUN_FLAG_* names so\ncallers can tell at a glance whether an image is a composite Component,\nPartial, EmbeddedMsil, has StrippedIlBodies, etc. — without manually\ndecoding the bitmask.\n\n- ReadyToRunHeaderAttributes: new [Flags] enum mirroring ReadyToRunFlag in\n  coreclr/inc/readytorun.h (12 flags, 0x1..0x800). Named with the BCL flags-\n  enum convention (cf. TypeAttributes) to satisfy CA1711.\n- DecodeNames(uint): decodes set bits to names; any bit not covered by a\n  known flag is reported as a single Unknown(0x...) entry so information is\n  never silently dropped.\n- R2RHeaderResult gains FlagsHex (e.g. \"0x00000003\") and FlagNames; the tool\n  summary lists the decoded names. Raw Flags is retained for back-compat.\n- Tests: Core decode unit tests (single/multiple/all-known/unknown-residue),\n  a real-image round-trip regression (decoded names must re-OR back to the\n  raw flags), plus a synthetic server-tool assertion.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T10:25:24-03:00",
          "tree_id": "8e9351180348ab1179bc6370ff3600978fc2af67",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/61c0bace78e1d4f4481cedcee73192eeeb75a120"
        },
        "date": 1781098513251,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7519999.011160715,
            "unit": "ns",
            "range": "± 96430.38062174115"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 86.06280561004367,
            "unit": "ns",
            "range": "± 1.2363124171350672"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "d7bf24d1d7f8b0b4613dc656c18abff0ba37437b",
          "message": "feat(r2r): decode ImportSections (type 101) behind includeImportSections (#120)\n\nAdd structural decoding of the R2R ImportSections section (type 101) —\neach READYTORUN_IMPORT_SECTION (20-byte) entry is decoded into RVA/size,\ndecoded Type and Flags, EntrySize, and the Signatures/AuxiliaryData RVAs.\nIndividual fixup signatures are intentionally not decoded (would require\nInternal.TypeSystem — out of scope).\n\nExposed via a new `includeImportSections=false` parameter on the existing\n`get_r2r_header` tool (respecting the hard tool budget — no new tool).\n`R2RHeaderResult.ImportSections` is a nullable additive field, so the\nresponse stays back-compatible. A NextActionHint advertises the parameter\nwhen the section is present but not requested.\n\n`ReadImportSections` validates the declared table byte range against the\nfile size up front (long arithmetic) before allocating, rejecting crafted\nheaders whose Size would otherwise drive a huge allocation or overflow the\nper-entry offset math.\n\nTests: Core decoder/reader unit tests, an oversized-section hardening test,\na real-image regression (System.Private.CoreLib carries a 7-entry section),\nand Server tool tests. 430 Core + 111 Server green, 0 warnings.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:01:52-03:00",
          "tree_id": "f66e403e53ae2e137bda94903c073ed9c5344609",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/d7bf24d1d7f8b0b4613dc656c18abff0ba37437b"
        },
        "date": 1781100747235,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7541588.986778846,
            "unit": "ns",
            "range": "± 70084.0851891285"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 85.7194544752439,
            "unit": "ns",
            "range": "± 0.6204975972495308"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "46b427752ca9f6531bdb668d75026d578c1fc408",
          "message": "feat(r2r): decode CompilerIdentifier + OwnerCompositeExecutable strings (#121)\n\nDecode the two ReadyToRun identification-string sections into the\nget_r2r_header result:\n- CompilerIdentifier (type 100): the crossgen2 / compiler that produced\n  the image (e.g. \"Crossgen2 10.0.526.1541\").\n- OwnerCompositeExecutable (type 116): the composite executable filename\n  that owns a component image (null for non-composite images).\n\nBoth payloads are a single zero-terminated UTF-8 string; the decoder reads\nSize-1 bytes (excluding the terminator), mirroring\nILCompiler.Reflection.ReadyToRun. Decoding is best-effort auxiliary\nmetadata — ReadSectionUtf8String returns null (never throws) when the\nsection is absent, empty, or its declared range runs past the end of the\nfile, validated with long arithmetic before slicing.\n\nExposed eagerly (no new param — cheap single strings) via two nullable\nadditive fields on R2RHeaderResult, so the response stays back-compatible.\n\nTests: synthetic decode, absent/empty/oversized-size graceful-null cases,\nand a real-image regression (System.Private.CoreLib CompilerIdentifier).\n436 Core + 114 Server green, 0 warnings.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:38:36-03:00",
          "tree_id": "414c1232fa0cd279f23d0c95034d09620443cd72",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/46b427752ca9f6531bdb668d75026d578c1fc408"
        },
        "date": 1781102925552,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 6817131.123325893,
            "unit": "ns",
            "range": "± 39334.44649910622"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 88.49386450648308,
            "unit": "ns",
            "range": "± 0.22714804133945757"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "5ca7a6b34ad4172dfa23f49514b852fc176925f8",
          "message": "Add R2R composite-image metadata decoding (ComponentAssemblies + ManifestAssemblyMvids) (#122)\n\nDecode the composite ReadyToRun structural sections behind a new\nincludeCompositeInfo parameter on get_r2r_header:\n\n- ComponentAssemblies (type 115): array of 16-byte entries\n  {CorHeaderRVA, CorHeaderSize, AssemblyHeaderRVA, AssemblyHeaderSize}.\n- ManifestAssemblyMvids (type 118): array of 16-byte module-version GUIDs.\n\nBoth readers validate the full declared table against the file length up\nfront (long arithmetic) before allocating or indexing, mirroring the\nReadImportSections hardening. Section-absent -> Fail(R2RSectionNotPresent);\nempty -> Ok(empty); oversized -> Fail(InvalidArgument).\n\nSurfaced as two additive nullable fields on R2RHeaderResult plus a new\nR2RComponentAssemblyView record; a NextActionHint is offered when the\nsections are present but not included. No new MCP tool.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:55:31-03:00",
          "tree_id": "d02b4df6c640100450361c6b7fb8196d3aaf7a47",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/5ca7a6b34ad4172dfa23f49514b852fc176925f8"
        },
        "date": 1781103989540,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7489943.290625,
            "unit": "ns",
            "range": "± 72700.74419031377"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 90.99248455524445,
            "unit": "ns",
            "range": "± 2.479493079772519"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e172fd7aae7ba7e6e0a7fae77f6c0518297417f6",
          "message": "feat(r2r): NativeFormat reader primitives (foundation) (#124)\n\nSafe, span-based port of the runtime's Internal.NativeFormat\nvariable-length integer reader — the foundation for decoding the\nNativeFormat-encoded R2R sections that are currently out-of-scope\n(MethodDefEntryPoints, AvailableTypes, ...).\n\nNew (internal) types under src/DotnetNativeMcp.Core/R2R/NativeFormat/:\n- NativePrimitiveDecoder: faithful port of DecodeUnsigned/Signed/\n  UnsignedLong/SignedLong/SkipInteger + fixed-width ReadUInt8/16/32/64,\n  re-expressed over ReadOnlySpan<byte> + a ref-uint cursor. Stricter than\n  the runtime: it bounds-checks the 5/9-byte raw forms, and the fixed-width\n  reads use non-overflowing (end - offset < N) checks so a near-uint.MaxValue\n  offset is rejected as NativeFormatException rather than wrapping.\n- NativeReader: bounds-checked random-access wrapper over a section blob.\n- NativeParser: forward cursor.\n- NativeFormatException: internal sentinel; tool-facing readers (PR2/PR3)\n  will catch it -> Fail(InvalidArgument) so tools never throw.\n\nNo tool wiring yet (foundation only). Comprehensive round-trip, 5000-iter\nfuzz, encoding-width, fixed-width-LE, truncation/OOB and wrapped-offset\nhardening tests (114 NativeFormat tests).\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T13:51:53-03:00",
          "tree_id": "b040a7f6d9882f96b1d78b625931fa2395d7b78c",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/e172fd7aae7ba7e6e0a7fae77f6c0518297417f6"
        },
        "date": 1781110940690,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7874227.558333334,
            "unit": "ns",
            "range": "± 89544.99872593298"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 93.62585722517085,
            "unit": "ns",
            "range": "± 4.057786378735384"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "0750b47dbb57b21278a053b9d42c056b1086c715",
          "message": "Add NativeArray + MethodDefEntryPoints (type 103) R2R decode (#125)\n\nPR2 of the NativeFormat reader epic. Ports the runtime's sparse\nindex-addressable NativeArray (16-element-block bit-tree) and decodes the\nMethodDefEntryPoints section, mapping each present MethodDef RID to its\nentry-point RUNTIME_FUNCTION index and hasFixups flag. Rides on the existing\nget_r2r_header tool via additive includeMethodEntryPoints /\nmethodEntryPointsLimit params and a nullable result field (no new tool).\n\nHardening:\n- Bound the decode loop with a 2,000,000-slot scan cap so a crafted section\n  advertising a huge untrusted Count backed by an in-bounds all-absent index\n  cannot spin unbounded; over-cap results are flagged Truncated.\n- DecodeMethodEntryPoint now consumes the trailing delta-encoded fixup offset\n  when the id marks it (id & 2), faithfully mirroring the runtime's\n  GetRuntimeFunctionIndexFromOffset so truncated fixup entries fail with\n  InvalidArgument instead of being silently accepted.\n\nTests: synthetic end-to-end decode, limit/truncation, fixup-delta consume and\ntruncated-delta failure, bounded-scan regression, and a real\nSystem.Private.CoreLib regression cross-checking every entry against the\nRuntimeFunctions bound.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T14:29:56-03:00",
          "tree_id": "8ac0bfc4ba2e6c6a2f223606e6363f25fe77e0a5",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/0750b47dbb57b21278a053b9d42c056b1086c715"
        },
        "date": 1781113185796,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7294694.459375,
            "unit": "ns",
            "range": "± 59085.925226816085"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 85.48247716029485,
            "unit": "ns",
            "range": "± 0.9337905181939736"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "1c0c9d2b9308893af9deaeabbab6c40a006b0ab2",
          "message": "Decode R2R AvailableTypes (type 108) via NativeFormat hashtable (#126)\n\nPR3 of the NativeFormat-reader epic. Adds a safe, span-based port of the\nruntime's Internal.NativeFormat.NativeHashtable and wires it to decode the\nReadyToRun AvailableTypes section (type 108).\n\nEach hashtable entry yields a metadata RID whose low bit flags ExportedType\n(table 0x27) vs TypeDef (table 0x02); the RID is widened into a full metadata\ntoken for handoff to dotnet-assembly-mcp's get_type. Type names are not\nresolved here (that needs managed ECMA metadata, out of scope).\n\nRides additively on get_r2r_header via includeAvailableTypes /\navailableTypesLimit (no new tool — tool budget hard cap of 10). The bucket\ncount comes from untrusted header bytes, so the traversal is bounded by a\nmaxScan step cap that flags Truncated rather than spinning on a crafted huge\nbucket count. Out-of-range RIDs (0 or > 0x00FFFFFF) fail as InvalidArgument\ninstead of corrupting the synthesised token.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:01:55-03:00",
          "tree_id": "3b6f0110ef066b5490b8f4e8fd3137b2ebcfc8e8",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/1c0c9d2b9308893af9deaeabbab6c40a006b0ab2"
        },
        "date": 1781115163867,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7557194.271354167,
            "unit": "ns",
            "range": "± 79115.57107626523"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 86.07605045182365,
            "unit": "ns",
            "range": "± 1.0520243520714019"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "f3ac70dc7cc049c69cb93f7b52226e64a4d85318",
          "message": "feat(r2r): decode V9 RID-indexed info maps (121/122/123) (#127)\n\nDecode the three .NET 9 (R2R v9.0) info-map sections, riding additively on\nget_r2r_header via a single includeInfoMaps flag (capped by infoMapsLimit):\n\n- EnclosingTypeMap (122): u16 count + u16[] enclosing RIDs; emit nested→\n  enclosing TypeDef token pairs (skip top-level / RID 0).\n- MethodIsGenericMap (121): i32 count + ceil(count/8) MSB-first bit array;\n  emit MethodDef tokens for set bits; count all generic methods past the\n  limit and flag truncation.\n- TypeGenericInfoMap (123): u32 count + ceil(count/2) nibbles (even index in\n  the high nibble); emit per-type generic arity / variance / constraints for\n  generic types only.\n\nAll three are fixed-width, little-endian, dependency-free decodes over a\nbounds-validated section slice (shared MapSectionBytes helper). Counts are\nread from untrusted bytes and validated against the section size before the\ndecode loop (overflow-safe widened arithmetic), so malformed input surfaces\nas InvalidArgument rather than throwing. Tokens are emitted for handoff to\ndotnet-assembly-mcp; names are not resolved.\n\nTests: 12 Core (incl. real-image SPC regression + int.MaxValue overflow\nguard) + 4 Server. README coverage table updated.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:28:36-03:00",
          "tree_id": "41674796a8344bfa5fd559b6b992b685c3dd0670",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/f3ac70dc7cc049c69cb93f7b52226e64a4d85318"
        },
        "date": 1781116728599,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7475989.940290178,
            "unit": "ns",
            "range": "± 76326.02776688524"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 85.7839641491572,
            "unit": "ns",
            "range": "± 0.3008163274240552"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "fa98888c7cf785061513a9924563b9ac1f5c6f99",
          "message": "feat(r2r): surface ManifestMetadata (112) ECMA blob handoff descriptor (#128)\n\nThe ManifestMetadata section content is an embedded ECMA-335 metadata blob\n(the R2R manifest of referenced assemblies). Rather than decode the managed\nmetadata (dotnet-assembly-mcp's job), surface a handoff descriptor:\n\n- file offset / RVA / size of the blob\n- BSJB signature validation\n- parsed metadata-root header (ECMA-335 II.24.2.1): version string +\n  stream directory (#~, #Strings, #US, #GUID, #Blob)\n\nRides additively on get_r2r_header via a single includeManifestMetadata flag\n+ nullable ManifestMetadata field (tool budget held at 10). Every read is\nbounds-checked over the section slice with overflow-safe arithmetic; the\nversion string must be null-terminated and every stream-name padding must fit\nthe blob, so malformed/truncated input surfaces as InvalidArgument rather\nthan throwing.\n\nTests: 9 Core (incl. real-image SPC regression, bad signature, truncated\nheader, oversized version length, non-null-terminated version, truncated\nstream-name padding, stream-count-beyond-blob) + 2 Server. README updated.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:48:30-03:00",
          "tree_id": "07b4beaa1606398e128f8e832434a22108bf4f5b",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/fa98888c7cf785061513a9924563b9ac1f5c6f99"
        },
        "date": 1781117889914,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 6854666.390066965,
            "unit": "ns",
            "range": "± 55526.9893948848"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 88.7397449016571,
            "unit": "ns",
            "range": "± 0.1319031480022939"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "bd8e9a0aca3c41bf757e0be8bc9a05b5ca5df912",
          "message": "feat(r2r): decode HotColdMap (type 120) hot/cold pairs (#129)\n\nAdds ReadyToRunReader.ReadHotColdMap which decodes the HotColdMap section\ninto (cold, hot) RUNTIME_FUNCTION index pairs (flat uint[], pairCount =\nsize/8). Wired additively into get_r2r_header via includeHotColdMap + a\nnullable R2RHotColdMapView result field; capped by infoMapsLimit. Never\nthrows to the tool layer (InvalidArgument on non-pair-aligned/empty\nsections, R2RSectionNotPresent when absent).\n\nAdds 5 Core + 2 Server tests (synthetic-only — SPC has no HotColdMap).\nUpdates README coverage table + tool description; removes HotColdMap from\nthe out-of-scope list.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:59:35-03:00",
          "tree_id": "c8d3c9165c14df8dc0b37bec3c8f6e69008da649",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/bd8e9a0aca3c41bf757e0be8bc9a05b5ca5df912"
        },
        "date": 1781118556942,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SampleAot\")",
            "value": 7787340.058333334,
            "unit": "ns",
            "range": "± 100967.57874128298"
          },
          {
            "name": "DotnetNativeMcp.Bench.DisassembleBench.Disassemble(Input: \"SystemPrivateCoreLib\")",
            "value": 98.51980414698201,
            "unit": "ns",
            "range": "± 3.0742992972112932"
          }
        ]
      }
    ],
    "ExtractStrings Benchmark": [
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "ef20124ae8299193a0a95950588e2f7b178cc5b7",
          "message": "Fix benchmark branch bootstrap (#87)\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-21T13:01:05-03:00",
          "tree_id": "2f4cdfe7c82d510e3066501d61b91484c8526e8b",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/ef20124ae8299193a0a95950588e2f7b178cc5b7"
        },
        "date": 1779379950535,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 969488.3234375,
            "unit": "ns",
            "range": "± 7066.077701004503"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 14432443.114583334,
            "unit": "ns",
            "range": "± 53633.12817884468"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "7299bed916595372a444b83cfbb2d42332510ecb",
          "message": "Bump GitHub Actions to Node 24-based majors (#90) (#91)\n\nEliminates the Node 20 deprecation warnings emitted on every workflow\nrun since the runner started warning. Node 20 will be removed from\nGitHub-hosted runners on 2026-09-16; default flips to Node 24 on\n2026-06-02.\n\nUpgrade map (chosen to be minimal Node-24 bumps, skipping majors that\nintroduce ESM or other API breaks we don't need):\n- actions/checkout v4 → v5\n- actions/setup-dotnet v4 → v5\n- actions/cache v4 → v5\n- actions/upload-artifact v4 → v6\n- actions/download-artifact v4 → v7\n- actions/attest-build-provenance v2 → v4\n- softprops/action-gh-release v2 → v3\n\nCross-repo mirror to be opened in dotnet-assembly-mcp and\ndotnet-diagnostics-mcp after this PR validates the pattern.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T11:17:11-03:00",
          "tree_id": "cd525c4877e6b09cc6cf0f13d29477b74bffefcd",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/7299bed916595372a444b83cfbb2d42332510ecb"
        },
        "date": 1779460121272,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1069350.2454927885,
            "unit": "ns",
            "range": "± 3865.7260540624625"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 16407762.460416667,
            "unit": "ns",
            "range": "± 174133.6266564153"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "9c30cf9d9b2e256846cb8a871eeed46b23bd86d0",
          "message": "Replace dynamic permissions expression in bench.yml with static value (#107)\n\nPR #92 set the bench job's permissions to\n`contents: ${{ github.event_name == 'push' && 'write' || 'read' }}`.\nGitHub Actions does not support expressions in the `permissions:` map\n(values must be literal read/write/none), so the workflow file failed\nparse and every bench run since the #92 merge completed in 0 seconds\nwith 'This run likely failed because of a workflow file issue'. No\nBenchmarkDotNet history has been published to gh-pages since v0.5.4.\n\nThis hotfix reverts to a static `contents: write` on the bench job.\nThe workflow-level `contents: read` default added by #92 stays in\nplace, so the regression vs. the pre-#92 state is strictly tighter at\nworkflow scope. PR exposure is documented honestly in the comment and\nthe proper fix (split push vs. PR into two jobs with static\npermissions) is tracked in #106.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T15:09:18-03:00",
          "tree_id": "16887e2c426a2056972ec16157125175d0a35b85",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/9c30cf9d9b2e256846cb8a871eeed46b23bd86d0"
        },
        "date": 1779474011770,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 940661.0634765625,
            "unit": "ns",
            "range": "± 5278.044336222775"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 14952010.360677084,
            "unit": "ns",
            "range": "± 24625.662780137263"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8764ef474031923558cf29b051bdf601160ece56",
          "message": "Split bench into push vs. PR jobs with static least-privilege permissions (#108)\n\nCloses #106.\n\nThe hotfix in #107 restored bench.yml to a single job with a static\n`contents: write` permission so the YAML would parse, at the cost of\ngranting maintainer-labeled `perf` PRs a writable GITHUB_TOKEN. The\nbenchmark-action's `auto-push` step is gated on push events, but\nPR-controlled `dotnet run` code in the bench fixtures could in\nprinciple use the token before that gate. #106 tracked the structural\nfix.\n\nThis change introduces a composite action under\n`.github/actions/run-bench/` that owns the setup, build, run and\nstorage steps. The workflow now defines two jobs with complementary\nevent filters and statically declared permissions:\n\n- bench-push: `if: github.event_name != 'pull_request'` (covers push +\n  workflow_dispatch), `permissions: contents: write`, calls the\n  composite with `auto-push: 'true'` / `fail-on-alert: 'false'`.\n- bench-pr: `if: github.event_name == 'pull_request' &&\n  contains(labels, 'perf')`, `permissions: contents: read`, calls the\n  composite with `auto-push: 'false'` / `fail-on-alert: 'true'`.\n\nPR-controlled bench code therefore runs under a strictly read-only\nGITHUB_TOKEN and the benchmark history can only be advanced by the\npush baseline path. GitHub Actions still does not allow expressions in\nthe `permissions:` map, so the split is enforced at the job level via\nevent filtering instead.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-05-22T15:40:41-03:00",
          "tree_id": "9c36fdab158009ac152e50c99d84947723a2d5b4",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8764ef474031923558cf29b051bdf601160ece56"
        },
        "date": 1779475855701,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1068995.9981863839,
            "unit": "ns",
            "range": "± 3344.1498162704984"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 16118583.764583332,
            "unit": "ns",
            "range": "± 25243.33627779526"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "aa2e0c221ae3d34514d4d5a938f683feedc5e6b5",
          "message": "test: add differential (oracle) harness for the ELF reader vs readelf (#110)\n\nAdds a differential test harness that cross-checks ElfReader against GNU\nreadelf, complementing the existing fuzz harness (which only proves the\nparsers never throw) with a correctness oracle.\n\nSurfaces covered:\n- symbols (readelf -sW): per-index name, value, size, function flag + count\n- sections (readelf -SW): per-name virtual address, file offset, size\n- imports: DT_NEEDED libraries (readelf -dW) and undefined .dynsym symbols\n\nNotes:\n- Shared ReadelfOracle helper shells out to readelf and parses its wide\n  (-W) output; drains stdout/stderr asynchronously with an effective\n  timeout. Symbol Size is decimal in -sW; section geometry is hex in -SW.\n- Tests no-op when readelf or the NativeAOT fixture are unavailable, so\n  the suite stays green on hosts without binutils. CI (ubuntu-latest) has\n  readelf and builds the fixture, so the comparison runs for real.\n- Version suffixes (@GLIBC_x.y) are normalized on both sides; SHT_NOBITS\n  FileSize is not checked; duplicate section names fall back to a\n  geometry-existence match.\n\nSee docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T11:59:55-03:00",
          "tree_id": "9a7a776f87447e5bac73499ce540ffc6550d3e39",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/aa2e0c221ae3d34514d4d5a938f683feedc5e6b5"
        },
        "date": 1781017836626,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 902091.8608774039,
            "unit": "ns",
            "range": "± 1002.0032284838028"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 15083963.029166667,
            "unit": "ns",
            "range": "± 41644.421660012675"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "f26222daedc074587159bc8e849e72b615bd904d",
          "message": "test: add differential (oracle) harness for the x86/x64 disassembler vs objdump (#111)\n\nCross-checks IcedDisassembler (via RawDisassembler) against GNU\nobjdump -d -M intel, extending the differential-testing approach from the\nELF reader to the decoder.\n\nThe hard oracle is instruction-boundary + raw-byte agreement: two\nindependent decoders walking the same bytes must segment them identically.\nFor each .text function symbol in the SampleAot fixture the harness\ndisassembles the body both ways and asserts the in-range instruction\naddress SETS are equal (catching early-stop bugs in either decoder), then\nasserts identical raw bytes per address. Mnemonics are compared as a\nsofter signal after normalizing objdump's display (segment/rep/lock/REX\nprefix tokens stripped, movabs->mov, nop/xchg NOP family treated as one).\n\n- New OracleProcess shared process runner (concurrent stdout/stderr drain,\n  timeout, missing-tool skip); ReadelfOracle now delegates to it.\n- objdump is invoked with --insn-width=15 so long instructions never wrap\n  their byte column onto a continuation line.\n- Tests no-op when objdump or the fixture are unavailable; CI has both.\n\nSee docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T15:32:23-03:00",
          "tree_id": "eff6c022b7334406dd3f5f29069b7f0758ed7a30",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/f26222daedc074587159bc8e849e72b615bd904d"
        },
        "date": 1781030617887,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 921705.2278645834,
            "unit": "ns",
            "range": "± 2824.008730369002"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 14960850.875,
            "unit": "ns",
            "range": "± 27710.80045992833"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e05f1f566372189956bd51c694de515e7c0a6291",
          "message": "test: add differential (oracle) harness for the PE reader vs llvm-readobj (#112)\n\nCross-checks PeNativeReader's section table against LLVM\nllvm-readobj --sections, extending the differential-testing approach to\nthe PE reader (after the ELF reader and the x86/x64 disassembler).\n\nFor every section the harness asserts the name set is equal and the\ngeometry matches exactly: virtual address, virtual size, file offset, and\nfile size. PeNativeReader emits the full COFF section table with no\nfiltering, so unlike the ELF section comparison this asserts the complete\nset; duplicate section names fall back to a geometry-existence match.\n\n- New LlvmReadobjOracle: stateful parser over `Section { ... }` blocks;\n  hex for 0x-prefixed addresses/offsets, decimal for sizes.\n- Primary target is the always-present managed DotnetNativeMcp.Core.dll, so\n  the comparison runs everywhere instead of skipping on a missing fixture; a\n  second test additionally exercises the published ReadyToRun\n  System.Private.CoreLib.dll (the real asm-mcp -> native-mcp handoff target).\n- CI installs `llvm` so llvm-readobj is present and the comparison runs for\n  real rather than skipping.\n\nMach-O is intentionally not covered yet: the repo has no real Mach-O fixture\non disk to point an oracle at. See docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T15:57:48-03:00",
          "tree_id": "bd5f772bbcdc59b3a22cf3f3fbdb7a9288a726fd",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/e05f1f566372189956bd51c694de515e7c0a6291"
        },
        "date": 1781032121102,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1034404.7475260417,
            "unit": "ns",
            "range": "± 4153.873349973972"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 15922051.060096154,
            "unit": "ns",
            "range": "± 19687.759037248667"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "b1e569eff2db61ac03701358946b70485ca20a02",
          "message": "test: add Mach-O section differential harness vs llvm-readobj (#113)\n\nCompletes the ELF/PE/Mach-O differential (oracle) triad. Parses tiny\ncommitted Mach-O relocatable objects (x86_64 + arm64) both with MachOReader\nand with llvm-readobj --sections, then asserts per-section geometry agrees\n(virtual address, virtual size, file offset, file size).\n\nRelocatable .o objects are used as fixtures because MachOReader rejects\nLC_DYLD_CHAINED_FIXUPS (present in linked dylibs/executables); a .o never\ncarries chained fixups so it round-trips through the reader. Fixtures are\ncommitted (not built at test time) so only the oracle (llvm-readobj) is\nneeded and the test skips cleanly when LLVM is absent.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T16:53:45-03:00",
          "tree_id": "eb0dcf4ef00cf1278cd8f152c020f603e0c67548",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/b1e569eff2db61ac03701358946b70485ca20a02"
        },
        "date": 1781035470157,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 814271.5576923077,
            "unit": "ns",
            "range": "± 1656.9965002893803"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 12617690.354166666,
            "unit": "ns",
            "range": "± 94473.50273740863"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8227247d82a1d82a6f52d3a97b0b9179b19ee791",
          "message": "test: add ARM64 disassembly differential harness vs llvm-objdump (#114)\n\nCompletes disassembler oracle coverage: Arm64Disassembler is now compared\nagainst llvm-objdump the same way x86/x64 is compared against GNU objdump.\n\nBuilding the harness surfaced a real production bug, fixed here:\nInstructionView.Mnemonic was sourced from instr.Mnemonic.ToText(false),\nwhich collapses every B.cond (b.eq, b.ne, ...) to a bare 'b', dropping the\ncondition suffix. Mnemonic and operands are now both derived from a single\ninstr.TryFormat pass via FormatMnemonicAndOperands, preserving the suffix.\nApplied at both call sites (Disassemble and ScanSection).\n\n- Rich ARM64 fixture (arm64rich.s/.o, 42 diverse instructions) via llvm-mc\n- LlvmObjdumpArm64Oracle: parses addr/word/mnemonic, reverses big-endian\n  word to little-endian file-order hex for raw-byte comparison\n- Arm64DisassemblyDifferentialTests: address-set, raw-word and exact\n  mnemonic equality against the fixture\n- Arm64DisassemblerTests: b.cond regression test (no LLVM dependency)\n- docs/differential-testing.md and fixtures README updated\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T18:53:20-03:00",
          "tree_id": "daa91ac1fd19777530b282e7eb813c1e41311fbb",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8227247d82a1d82a6f52d3a97b0b9179b19ee791"
        },
        "date": 1781042583532,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 944094.2811748798,
            "unit": "ns",
            "range": "± 2039.4784113074006"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 14169750.71986607,
            "unit": "ns",
            "range": "± 50197.07335407172"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "33fecbd8e883ea5d90b0c8efcc4f59175c0c1d98",
          "message": "sec: enforce trusted-path allowlist for untrusted path hints (#109) (#115)\n\nHonor the cross-MCP handoff contract's \"Path hints are untrusted\" rule:\nevery filesystem path arriving off the wire is now canonicalised (symlinks\nand junctions resolved, `..` flattened) and, when enforcement is enabled,\nchecked against an allowlist of trusted roots before any file is opened.\n\n- Add PathCanonicalizer (ResolveRealPath + boundary-aware IsUnderAllowedRoot),\n  PathAccessPolicy (Validate choke point, Permissive default) and\n  PathPolicyBuilder (operator roots ∪ well-known roots).\n- New `path_not_allowed` error kind (no published kind repurposed).\n- Wire validation into NativeBinaryRegistry.Load/RegisterHint and every tool\n  entry point (load, import manifest, disassemble imagePath/ilMapPath, and the\n  get_size_breakdown/explain_retention sidecar overrides AND their defaults).\n- Guard the implicit `.map` sidecar merge against a symlink escaping the\n  already-trusted binary directory.\n- Containment is case-insensitive only on Windows; case-sensitive elsewhere\n  (including case-sensitive macOS volumes) to avoid a containment bypass.\n- Enforcement is opt-in: permissive by default (still canonicalises) with a\n  one-time startup warning; enforces once an operator configures a root via\n  NativeMcp:AllowedBinaryRoots / NATIVE_MCP_ALLOWED_ROOTS / BINARIES_DIR.\n- Document the model in docs/handoff-contract.md and README.md.\n\nCloses #109\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T20:09:31-03:00",
          "tree_id": "9a120da82bc07d7b68df9d12d58190a0aadc546d",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/33fecbd8e883ea5d90b0c8efcc4f59175c0c1d98"
        },
        "date": 1781047215517,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 943403.7639160156,
            "unit": "ns",
            "range": "± 1539.66712764257"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 14144978.559495192,
            "unit": "ns",
            "range": "± 11336.212842016637"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "c1f92df80c034440ef681b516e1cc6716955aeec",
          "message": "test: add Mach-O nlist symbol differential harness vs llvm-readobj (#116)\n\nClose the Mach-O differential coverage gap: the oracle harness previously\nchecked only section geometry, leaving the nlist symbol reader unverified\nagainst an independent tool.\n\n- LlvmReadobjOracle.TryReadMachOSymbols parses `llvm-readobj --syms` into a\n  multiset of (name, n_value), mirroring MachOReader's emission exactly:\n  excludes undefined (Type: Undef) and STAB/debug entries, includes the\n  non-N_UNDF defined classes (Section/Absolute/Indirect), and strips the macOS\n  leading `_`.\n- MachOSymbolDifferentialTests compares MachOReader symbols vs the oracle on the\n  x64, arm64, and arm64rich committed fixtures (skips cleanly without LLVM).\n- A symbol-specific name regex captures the whole name and strips only the\n  trailing ` (N)` string-table-index gloss, so a symbol name containing spaces\n  is not truncated.\n- Document the new surface and comparison strategy in docs/differential-testing.md.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-09T22:12:35-03:00",
          "tree_id": "621e8aa04934d7cfc515a8a031a3505ca6dd0062",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/c1f92df80c034440ef681b516e1cc6716955aeec"
        },
        "date": 1781054618930,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 899574.2172526042,
            "unit": "ns",
            "range": "± 8650.835397074465"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 13387278.29375,
            "unit": "ns",
            "range": "± 145664.99306515136"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "616be79dda9adaf108d231f4a8b4674b68d8a78b",
          "message": "fix(r2r): correct ReadyToRunSectionType enum to match readytorun.h (#117)\n\nThe ReadyToRunSectionType enum was fabricated with incorrect values\n(RuntimeFunctions = 5 plus fictional low/high-numbered members). The\nauthoritative runtime header src/coreclr/inc/readytorun.h defines every\nR2R section type in the 100+ range, with RuntimeFunctions = 102.\n\nBecause the reader searched for section type 5, list_r2r_runtime_functions\nALWAYS returned r2r_section_not_present for real .NET 8/9/10 R2R images —\nthe primary handoff target. Verified empirically: the .NET 10\nSystem.Private.CoreLib.dll R2R image (v16.0) carries section type 102 with\n50471 valid x64 RUNTIME_FUNCTION entries. The earlier premise that modern\nR2R replaced RuntimeFunctions with \"MethodHeaderAndCodeInfo (type 105)\" was\nfalse — type 105 is DebugInfo; RuntimeFunctions is alive at type 102.\n\nChanges:\n- Rewrite ReadyToRunSectionType.cs to mirror readytorun.h exactly, removing\n  all fabricated members (incl. the fictional MethodHeaderAndCodeInfo = 105).\n- RawDisassembler: gate the decode-probe arch fallback on RuntimeFunctions\n  being absent (was gated on the fictional MethodHeaderAndCodeInfo section).\n- Fix error messages and tool/XML-doc descriptions referencing type 5/105.\n- Update synthetic R2R PE test builders to write the corrected section type.\n- Add real-image regression tests (RuntimeFunctions_SectionType_Is102 and\n  ReadRuntimeFunctions_RealR2RImage_ReturnsDecodableEntries) that would have\n  caught the bug; they skip gracefully when no R2R fixture is available.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T09:02:20-03:00",
          "tree_id": "e800b2b3e8258f5f6c06a6a234aa69cf0cc338b2",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/616be79dda9adaf108d231f4a8b4674b68d8a78b"
        },
        "date": 1781093555566,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 959215.6182942708,
            "unit": "ns",
            "range": "± 5910.615254097958"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 14412605.686458332,
            "unit": "ns",
            "range": "± 21802.708732300114"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8f7a2b4f3ea26fbe3a6466f5d191175e93c4c95d",
          "message": "test(r2r): add differential harness for RuntimeFunctions vs PE exception directory (#118)\n\nAdds regression coverage for the R2R RuntimeFunctions reader using the\nestablished differential-testing harness. In a crossgen2 R2R image the\nRuntimeFunctions section (type 102) IS the PE exception data directory\n(.pdata) — identical RVA and size — giving two independent paths to the\nsame table:\n\n- ReadyToRunReader: managed-native header -> R2R signature -> section 102.\n- A battle-tested PE reader: optional-header data directories.\n\nThe two paths must agree; that invariant is exactly what the section-type\nenum bug (RuntimeFunctions mis-mapped to type 5) violated, so this guards\nagainst regression.\n\n- LlvmReadobjOracle.TryReadPeExceptionDirectory: parses the exception data\n  directory from `llvm-readobj --file-headers` (decoded regardless of the\n  CoreCLR per-OS machine override, e.g. 0xFD1D on linux-x64).\n- R2RRuntimeFunctionsDifferentialTests: location match vs llvm-readobj, plus\n  an in-process independent PEReader decode of the first 32 x64\n  RUNTIME_FUNCTION rows compared against ReadRuntimeFunctions. Both no-op\n  when the fixture is unbuilt (and the location test when llvm-readobj is\n  absent).\n- docs/differential-testing.md: matrix row + explanatory note.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T09:52:12-03:00",
          "tree_id": "1746b2283790530b537795d55f357ba50907c3b4",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/8f7a2b4f3ea26fbe3a6466f5d191175e93c4c95d"
        },
        "date": 1781096552012,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1026712.4013671875,
            "unit": "ns",
            "range": "± 4068.3559182821978"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 15909305.229910715,
            "unit": "ns",
            "range": "± 32114.19168893442"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "61c0bace78e1d4f4481cedcee73192eeeb75a120",
          "message": "feat(r2r): decode ReadyToRun header flags in get_r2r_header (#119)\n\nThe get_r2r_header tool previously surfaced only the raw uint Flags value.\nThis adds decoding of the set bits into their READYTORUN_FLAG_* names so\ncallers can tell at a glance whether an image is a composite Component,\nPartial, EmbeddedMsil, has StrippedIlBodies, etc. — without manually\ndecoding the bitmask.\n\n- ReadyToRunHeaderAttributes: new [Flags] enum mirroring ReadyToRunFlag in\n  coreclr/inc/readytorun.h (12 flags, 0x1..0x800). Named with the BCL flags-\n  enum convention (cf. TypeAttributes) to satisfy CA1711.\n- DecodeNames(uint): decodes set bits to names; any bit not covered by a\n  known flag is reported as a single Unknown(0x...) entry so information is\n  never silently dropped.\n- R2RHeaderResult gains FlagsHex (e.g. \"0x00000003\") and FlagNames; the tool\n  summary lists the decoded names. Raw Flags is retained for back-compat.\n- Tests: Core decode unit tests (single/multiple/all-known/unknown-residue),\n  a real-image round-trip regression (decoded names must re-OR back to the\n  raw flags), plus a synthetic server-tool assertion.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T10:25:24-03:00",
          "tree_id": "8e9351180348ab1179bc6370ff3600978fc2af67",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/61c0bace78e1d4f4481cedcee73192eeeb75a120"
        },
        "date": 1781098514770,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1027198.1484375,
            "unit": "ns",
            "range": "± 3507.0913985511775"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 15881776.153846154,
            "unit": "ns",
            "range": "± 18465.182434197613"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "d7bf24d1d7f8b0b4613dc656c18abff0ba37437b",
          "message": "feat(r2r): decode ImportSections (type 101) behind includeImportSections (#120)\n\nAdd structural decoding of the R2R ImportSections section (type 101) —\neach READYTORUN_IMPORT_SECTION (20-byte) entry is decoded into RVA/size,\ndecoded Type and Flags, EntrySize, and the Signatures/AuxiliaryData RVAs.\nIndividual fixup signatures are intentionally not decoded (would require\nInternal.TypeSystem — out of scope).\n\nExposed via a new `includeImportSections=false` parameter on the existing\n`get_r2r_header` tool (respecting the hard tool budget — no new tool).\n`R2RHeaderResult.ImportSections` is a nullable additive field, so the\nresponse stays back-compatible. A NextActionHint advertises the parameter\nwhen the section is present but not requested.\n\n`ReadImportSections` validates the declared table byte range against the\nfile size up front (long arithmetic) before allocating, rejecting crafted\nheaders whose Size would otherwise drive a huge allocation or overflow the\nper-entry offset math.\n\nTests: Core decoder/reader unit tests, an oversized-section hardening test,\na real-image regression (System.Private.CoreLib carries a 7-entry section),\nand Server tool tests. 430 Core + 111 Server green, 0 warnings.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:01:52-03:00",
          "tree_id": "f66e403e53ae2e137bda94903c073ed9c5344609",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/d7bf24d1d7f8b0b4613dc656c18abff0ba37437b"
        },
        "date": 1781100748456,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1042739.5203575721,
            "unit": "ns",
            "range": "± 3542.8318091168308"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 15887794.08173077,
            "unit": "ns",
            "range": "± 17705.995686041977"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "46b427752ca9f6531bdb668d75026d578c1fc408",
          "message": "feat(r2r): decode CompilerIdentifier + OwnerCompositeExecutable strings (#121)\n\nDecode the two ReadyToRun identification-string sections into the\nget_r2r_header result:\n- CompilerIdentifier (type 100): the crossgen2 / compiler that produced\n  the image (e.g. \"Crossgen2 10.0.526.1541\").\n- OwnerCompositeExecutable (type 116): the composite executable filename\n  that owns a component image (null for non-composite images).\n\nBoth payloads are a single zero-terminated UTF-8 string; the decoder reads\nSize-1 bytes (excluding the terminator), mirroring\nILCompiler.Reflection.ReadyToRun. Decoding is best-effort auxiliary\nmetadata — ReadSectionUtf8String returns null (never throws) when the\nsection is absent, empty, or its declared range runs past the end of the\nfile, validated with long arithmetic before slicing.\n\nExposed eagerly (no new param — cheap single strings) via two nullable\nadditive fields on R2RHeaderResult, so the response stays back-compatible.\n\nTests: synthetic decode, absent/empty/oversized-size graceful-null cases,\nand a real-image regression (System.Private.CoreLib CompilerIdentifier).\n436 Core + 114 Server green, 0 warnings.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:38:36-03:00",
          "tree_id": "414c1232fa0cd279f23d0c95034d09620443cd72",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/46b427752ca9f6531bdb668d75026d578c1fc408"
        },
        "date": 1781102926930,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 926579.924235026,
            "unit": "ns",
            "range": "± 1419.8259804511615"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 14556495.496875,
            "unit": "ns",
            "range": "± 21860.425516152507"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "5ca7a6b34ad4172dfa23f49514b852fc176925f8",
          "message": "Add R2R composite-image metadata decoding (ComponentAssemblies + ManifestAssemblyMvids) (#122)\n\nDecode the composite ReadyToRun structural sections behind a new\nincludeCompositeInfo parameter on get_r2r_header:\n\n- ComponentAssemblies (type 115): array of 16-byte entries\n  {CorHeaderRVA, CorHeaderSize, AssemblyHeaderRVA, AssemblyHeaderSize}.\n- ManifestAssemblyMvids (type 118): array of 16-byte module-version GUIDs.\n\nBoth readers validate the full declared table against the file length up\nfront (long arithmetic) before allocating or indexing, mirroring the\nReadImportSections hardening. Section-absent -> Fail(R2RSectionNotPresent);\nempty -> Ok(empty); oversized -> Fail(InvalidArgument).\n\nSurfaced as two additive nullable fields on R2RHeaderResult plus a new\nR2RComponentAssemblyView record; a NextActionHint is offered when the\nsections are present but not included. No new MCP tool.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T11:55:31-03:00",
          "tree_id": "d02b4df6c640100450361c6b7fb8196d3aaf7a47",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/5ca7a6b34ad4172dfa23f49514b852fc176925f8"
        },
        "date": 1781103991486,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1027285.8039362981,
            "unit": "ns",
            "range": "± 1353.5951990903047"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 15893685.96205357,
            "unit": "ns",
            "range": "± 68891.95633047658"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e172fd7aae7ba7e6e0a7fae77f6c0518297417f6",
          "message": "feat(r2r): NativeFormat reader primitives (foundation) (#124)\n\nSafe, span-based port of the runtime's Internal.NativeFormat\nvariable-length integer reader — the foundation for decoding the\nNativeFormat-encoded R2R sections that are currently out-of-scope\n(MethodDefEntryPoints, AvailableTypes, ...).\n\nNew (internal) types under src/DotnetNativeMcp.Core/R2R/NativeFormat/:\n- NativePrimitiveDecoder: faithful port of DecodeUnsigned/Signed/\n  UnsignedLong/SignedLong/SkipInteger + fixed-width ReadUInt8/16/32/64,\n  re-expressed over ReadOnlySpan<byte> + a ref-uint cursor. Stricter than\n  the runtime: it bounds-checks the 5/9-byte raw forms, and the fixed-width\n  reads use non-overflowing (end - offset < N) checks so a near-uint.MaxValue\n  offset is rejected as NativeFormatException rather than wrapping.\n- NativeReader: bounds-checked random-access wrapper over a section blob.\n- NativeParser: forward cursor.\n- NativeFormatException: internal sentinel; tool-facing readers (PR2/PR3)\n  will catch it -> Fail(InvalidArgument) so tools never throw.\n\nNo tool wiring yet (foundation only). Comprehensive round-trip, 5000-iter\nfuzz, encoding-width, fixed-width-LE, truncation/OOB and wrapped-offset\nhardening tests (114 NativeFormat tests).\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T13:51:53-03:00",
          "tree_id": "b040a7f6d9882f96b1d78b625931fa2395d7b78c",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/e172fd7aae7ba7e6e0a7fae77f6c0518297417f6"
        },
        "date": 1781110942053,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1092604.8545572916,
            "unit": "ns",
            "range": "± 1777.808247526445"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 16281967.214583334,
            "unit": "ns",
            "range": "± 99801.55331546393"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "0750b47dbb57b21278a053b9d42c056b1086c715",
          "message": "Add NativeArray + MethodDefEntryPoints (type 103) R2R decode (#125)\n\nPR2 of the NativeFormat reader epic. Ports the runtime's sparse\nindex-addressable NativeArray (16-element-block bit-tree) and decodes the\nMethodDefEntryPoints section, mapping each present MethodDef RID to its\nentry-point RUNTIME_FUNCTION index and hasFixups flag. Rides on the existing\nget_r2r_header tool via additive includeMethodEntryPoints /\nmethodEntryPointsLimit params and a nullable result field (no new tool).\n\nHardening:\n- Bound the decode loop with a 2,000,000-slot scan cap so a crafted section\n  advertising a huge untrusted Count backed by an in-bounds all-absent index\n  cannot spin unbounded; over-cap results are flagged Truncated.\n- DecodeMethodEntryPoint now consumes the trailing delta-encoded fixup offset\n  when the id marks it (id & 2), faithfully mirroring the runtime's\n  GetRuntimeFunctionIndexFromOffset so truncated fixup entries fail with\n  InvalidArgument instead of being silently accepted.\n\nTests: synthetic end-to-end decode, limit/truncation, fixup-delta consume and\ntruncated-delta failure, bounded-scan regression, and a real\nSystem.Private.CoreLib regression cross-checking every entry against the\nRuntimeFunctions bound.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T14:29:56-03:00",
          "tree_id": "8ac0bfc4ba2e6c6a2f223606e6363f25fe77e0a5",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/0750b47dbb57b21278a053b9d42c056b1086c715"
        },
        "date": 1781113187128,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1018978.3779296875,
            "unit": "ns",
            "range": "± 3137.1371359910654"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 16042154.104166666,
            "unit": "ns",
            "range": "± 17665.855791119295"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "1c0c9d2b9308893af9deaeabbab6c40a006b0ab2",
          "message": "Decode R2R AvailableTypes (type 108) via NativeFormat hashtable (#126)\n\nPR3 of the NativeFormat-reader epic. Adds a safe, span-based port of the\nruntime's Internal.NativeFormat.NativeHashtable and wires it to decode the\nReadyToRun AvailableTypes section (type 108).\n\nEach hashtable entry yields a metadata RID whose low bit flags ExportedType\n(table 0x27) vs TypeDef (table 0x02); the RID is widened into a full metadata\ntoken for handoff to dotnet-assembly-mcp's get_type. Type names are not\nresolved here (that needs managed ECMA metadata, out of scope).\n\nRides additively on get_r2r_header via includeAvailableTypes /\navailableTypesLimit (no new tool — tool budget hard cap of 10). The bucket\ncount comes from untrusted header bytes, so the traversal is bounded by a\nmaxScan step cap that flags Truncated rather than spinning on a crafted huge\nbucket count. Out-of-range RIDs (0 or > 0x00FFFFFF) fail as InvalidArgument\ninstead of corrupting the synthesised token.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:01:55-03:00",
          "tree_id": "3b6f0110ef066b5490b8f4e8fd3137b2ebcfc8e8",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/1c0c9d2b9308893af9deaeabbab6c40a006b0ab2"
        },
        "date": 1781115165149,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1084009.205078125,
            "unit": "ns",
            "range": "± 2456.4400038800627"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 16485412.047916668,
            "unit": "ns",
            "range": "± 49677.488130476806"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "f3ac70dc7cc049c69cb93f7b52226e64a4d85318",
          "message": "feat(r2r): decode V9 RID-indexed info maps (121/122/123) (#127)\n\nDecode the three .NET 9 (R2R v9.0) info-map sections, riding additively on\nget_r2r_header via a single includeInfoMaps flag (capped by infoMapsLimit):\n\n- EnclosingTypeMap (122): u16 count + u16[] enclosing RIDs; emit nested→\n  enclosing TypeDef token pairs (skip top-level / RID 0).\n- MethodIsGenericMap (121): i32 count + ceil(count/8) MSB-first bit array;\n  emit MethodDef tokens for set bits; count all generic methods past the\n  limit and flag truncation.\n- TypeGenericInfoMap (123): u32 count + ceil(count/2) nibbles (even index in\n  the high nibble); emit per-type generic arity / variance / constraints for\n  generic types only.\n\nAll three are fixed-width, little-endian, dependency-free decodes over a\nbounds-validated section slice (shared MapSectionBytes helper). Counts are\nread from untrusted bytes and validated against the section size before the\ndecode loop (overflow-safe widened arithmetic), so malformed input surfaces\nas InvalidArgument rather than throwing. Tokens are emitted for handoff to\ndotnet-assembly-mcp; names are not resolved.\n\nTests: 12 Core (incl. real-image SPC regression + int.MaxValue overflow\nguard) + 4 Server. README coverage table updated.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:28:36-03:00",
          "tree_id": "41674796a8344bfa5fd559b6b992b685c3dd0670",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/f3ac70dc7cc049c69cb93f7b52226e64a4d85318"
        },
        "date": 1781116729707,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 1037978.3990885416,
            "unit": "ns",
            "range": "± 6076.832978120505"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 15896457.677884616,
            "unit": "ns",
            "range": "± 23409.73449341071"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "39205549+pedrosakuma@users.noreply.github.com",
            "name": "Pedro Sakuma Travi",
            "username": "pedrosakuma"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "fa98888c7cf785061513a9924563b9ac1f5c6f99",
          "message": "feat(r2r): surface ManifestMetadata (112) ECMA blob handoff descriptor (#128)\n\nThe ManifestMetadata section content is an embedded ECMA-335 metadata blob\n(the R2R manifest of referenced assemblies). Rather than decode the managed\nmetadata (dotnet-assembly-mcp's job), surface a handoff descriptor:\n\n- file offset / RVA / size of the blob\n- BSJB signature validation\n- parsed metadata-root header (ECMA-335 II.24.2.1): version string +\n  stream directory (#~, #Strings, #US, #GUID, #Blob)\n\nRides additively on get_r2r_header via a single includeManifestMetadata flag\n+ nullable ManifestMetadata field (tool budget held at 10). Every read is\nbounds-checked over the section slice with overflow-safe arithmetic; the\nversion string must be null-terminated and every stream-name padding must fit\nthe blob, so malformed/truncated input surfaces as InvalidArgument rather\nthan throwing.\n\nTests: 9 Core (incl. real-image SPC regression, bad signature, truncated\nheader, oversized version length, non-null-terminated version, truncated\nstream-name padding, stream-count-beyond-blob) + 2 Server. README updated.\n\nCo-authored-by: GitHub Copilot <copilot@github.com>\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
          "timestamp": "2026-06-10T15:48:30-03:00",
          "tree_id": "07b4beaa1606398e128f8e832434a22108bf4f5b",
          "url": "https://github.com/pedrosakuma/dotnet-native-mcp/commit/fa98888c7cf785061513a9924563b9ac1f5c6f99"
        },
        "date": 1781117891294,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SampleAot\")",
            "value": 909120.2335379465,
            "unit": "ns",
            "range": "± 1642.042837897153"
          },
          {
            "name": "DotnetNativeMcp.Bench.ExtractStringsBench.ExtractStrings(Input: \"SystemPrivateCoreLib\")",
            "value": 13593721.551041666,
            "unit": "ns",
            "range": "± 84882.52574889285"
          }
        ]
      }
    ]
  }
}
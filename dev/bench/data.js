window.BENCHMARK_DATA = {
  "lastUpdate": 1781042582145,
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
      }
    ]
  }
}
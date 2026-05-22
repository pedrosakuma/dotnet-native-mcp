window.BENCHMARK_DATA = {
  "lastUpdate": 1779474010536,
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
      }
    ]
  }
}
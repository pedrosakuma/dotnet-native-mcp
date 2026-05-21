window.BENCHMARK_DATA = {
  "lastUpdate": 1779379950557,
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
      }
    ]
  }
}
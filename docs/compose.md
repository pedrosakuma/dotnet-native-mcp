# Running the triad with Docker Compose

> All three MCP servers — `dotnet-diagnostics-mcp`, `dotnet-assembly-mcp`, and
> `dotnet-native-mcp` — exposed as local HTTP endpoints behind a single
> `docker compose up` command.

## Quick start

```bash
# Clone this repo (if you haven't already)
git clone https://github.com/pedrosakuma/dotnet-native-mcp.git
cd dotnet-native-mcp

# Point at your binary / assembly directories (optional but recommended)
export BINARIES_DIR=/path/to/your/nativeaot/binaries   # for native-mcp
export ASSEMBLIES_DIR=/path/to/your/managed/assemblies # for assembly-mcp

docker compose -f deploy/docker-compose.yml up -d
```

Once healthy, the three servers are reachable at:

| Server | URL | Purpose |
|--------|-----|---------|
| `dotnet-diagnostics-mcp` | `http://127.0.0.1:8787/mcp` | Live process attach, EventPipe / ETW |
| `dotnet-assembly-mcp` | `http://127.0.0.1:8788/mcp` | Managed metadata, IL, decompilation |
| `dotnet-native-mcp` | `http://127.0.0.1:8789/mcp` | NativeAOT / R2R binary navigation |

Check server health:

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8788/health
curl http://127.0.0.1:8789/health
```

## Environment variables

| Variable | Description | Default |
|----------|-------------|---------|
| `BINARIES_DIR` | Host path mounted read-only at `/binaries` inside `dotnet-native-mcp` | docker-managed `binary-cache` volume |
| `ASSEMBLIES_DIR` | Host path mounted read-only at `/assemblies` inside `dotnet-assembly-mcp` | docker-managed `assembly-cache` volume |
| `MCP_BEARER_TOKEN` | Shared bearer token forwarded to all three servers. Required for any non-localhost deploy. | _(unset — no auth)_ |

### Bearer token

```bash
MCP_BEARER_TOKEN=$(openssl rand -hex 32) \
  docker compose -f deploy/docker-compose.yml up -d
```

When set, every `/mcp` request must carry `Authorization: Bearer <token>`.
The `/health` endpoint remains open on all three servers.

Individual per-server overrides are also accepted:

| Server | Per-server env var |
|--------|--------------------|
| `dotnet-diagnostics-mcp` | `MCP_BEARER_TOKEN` |
| `dotnet-assembly-mcp` | `ASSEMBLY_MCP_BEARER_TOKEN` |
| `dotnet-native-mcp` | `NATIVE_MCP_BEARER_TOKEN` |

`MCP_BEARER_TOKEN` is the shared fallback for all three.

## Pointing an MCP client at the triad

### VS Code / Cursor / Claude Desktop

Add the three servers to your MCP client configuration:

```jsonc
{
  "mcpServers": {
    "dotnet-diagnostics": {
      "url": "http://127.0.0.1:8787/mcp"
    },
    "dotnet-assembly": {
      "url": "http://127.0.0.1:8788/mcp"
    },
    "dotnet-native": {
      "url": "http://127.0.0.1:8789/mcp"
    }
  }
}
```

With bearer auth:

```jsonc
{
  "mcpServers": {
    "dotnet-native": {
      "url": "http://127.0.0.1:8789/mcp",
      "headers": {
        "Authorization": "Bearer <your-token>"
      }
    }
  }
}
```

## Attaching to live .NET processes

`dotnet-diagnostics-mcp` needs PID visibility to attach to a running process.
The default compose configuration does not grant this. For local development
you can uncomment the `pid: host` line in `deploy/docker-compose.yml`:

```yaml
  diagnostics:
    # ...
    pid: host   # ← uncomment; grants host-PID namespace visibility
```

> **Warning**: `pid: host` elevates the container's blast radius. Use only on a
> developer machine, never in production.

For Kubernetes, see the canonical attach recipe in the diagnostics repo:
`deploy/k8s/sample-sidecar.yaml` (uses `shareProcessNamespace` + a shared
`emptyDir` volume over `/tmp`).

## Building locally

To build the `dotnet-native-mcp` image from source (instead of pulling from
GHCR):

```bash
docker build -t dotnet-native-mcp:local -f deploy/Dockerfile .
```

Then edit `deploy/docker-compose.yml` to replace the `native` service image:

```yaml
  native:
    image: dotnet-native-mcp:local   # instead of ghcr.io/pedrosakuma/dotnet-native-mcp:latest
```

## Keeping the triad in sync

The same `deploy/docker-compose.yml` lives in each of the three sibling repos.
When you edit it, update all three copies and keep them byte-identical.
Drift between copies is the single biggest maintenance cost of the joint
topology.

- `pedrosakuma/dotnet-native-mcp:deploy/docker-compose.yml` ← this file
- `pedrosakuma/dotnet-assembly-mcp:deploy/docker-compose.yml`
- `pedrosakuma/dotnet-diagnostics-mcp:deploy/docker-compose.yml`

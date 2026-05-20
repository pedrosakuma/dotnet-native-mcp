# Multi-stage Dockerfile for dotnet-native-mcp server (HTTP transport).
#
# Build:  docker build -t dotnet-native-mcp:dev -f deploy/Dockerfile .
# Run:    docker run --rm -p 8789:8080 \
#                    -v /path/to/binaries:/binaries:ro \
#                    dotnet-native-mcp:dev
#
# The server inspects native .NET binaries (NativeAOT, R2R) on disk; mount the
# target binaries read-only and point the MCP client at http://<container>:8080/mcp.
# For stdio (local tool) installs use `dotnet tool install -g dotnet-native-mcp`
# instead — this image is for HTTP / sidecar deployments.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Bring in the files needed to restore first so the layer caches well.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/DotnetNativeMcp.Core/DotnetNativeMcp.Core.csproj src/DotnetNativeMcp.Core/
COPY src/DotnetNativeMcp.Server/DotnetNativeMcp.Server.csproj src/DotnetNativeMcp.Server/

RUN dotnet restore src/DotnetNativeMcp.Server/DotnetNativeMcp.Server.csproj

COPY src/ src/
COPY docs/ docs/

RUN dotnet publish src/DotnetNativeMcp.Server/DotnetNativeMcp.Server.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_NOLOGO=1

RUN groupadd --system --gid 10001 nativemcp \
 && useradd --system --uid 10001 --gid nativemcp --create-home --shell /usr/sbin/nologin nativemcp

COPY --from=build /app/publish ./
RUN chown -R nativemcp:nativemcp /app
USER nativemcp

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "DotnetNativeMcp.Server.dll"]

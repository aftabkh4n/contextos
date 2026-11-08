# ContextOS MCP server over stdio.
#   Build: docker build -t contextos .
#   Run:   docker run -i --rm -v contextos-data:/data -v "$PWD":/workspace contextos
#
# The build stage downloads the all-MiniLM-L6-v2 ONNX model and publishes the
# server for the image's CPU (amd64 or arm64). The final image holds the .NET
# runtime, the server and the model, and needs no network at runtime.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN bash scripts/fetch-model.sh
RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac \
 && dotnet publish src/ContextOS.Mcp -c Release -r "$rid" --self-contained false -o /app \
 && mkdir -p /dirs/data /dirs/workspace

FROM mcr.microsoft.com/dotnet/runtime:10.0
COPY --from=build /app /app
# Created in the build stage so this stage runs no commands (keeps cross-arch
# builds working without emulation).
COPY --from=build --chown=app:app /dirs/ /
# Memories are stored under /data; mount a volume there to keep them between
# runs. The server treats its working directory as the workspace, so mount the
# project you want memories for at /workspace.
ENV CONTEXTOS_HOME=/data
USER app
WORKDIR /workspace
ENTRYPOINT ["dotnet", "/app/contextos.dll"]

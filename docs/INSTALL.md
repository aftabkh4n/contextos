# ContextOS installation

Detailed per-platform instructions. If you just want the quick version,
see the Install section in [README.md](../README.md).

---

## macOS (Apple Silicon)

### 1. Download and install

```sh
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-arm64.tar.gz | tar xz
sudo mv osx-arm64/contextos /usr/local/bin/contextos
```

### 2. Verify

```sh
contextos --version
contextos --selftest
```

`--selftest` loads the ONNX model and embeds a short string. If it prints
a dimension count (384), the embeddings provider is working.

### 3. Register with Claude Code

```sh
claude mcp add --scope user contextos -- /usr/local/bin/contextos
claude mcp list
```

You should see `contextos` in the list. Start a Claude Code session and
run `/mcp` to confirm `remember`, `recall`, and `context` appear.

### Cursor note

Cursor MCP support is compatible but auto-hydration has not been manually
verified for this platform. See [examples/cursor-config/README.md](../examples/cursor-config/README.md).

### Troubleshooting

**"contextos: command not found" after install**
The binary is not on PATH. Move it to `/usr/local/bin` or add its directory
to your shell's PATH in `~/.zshrc`.

**"permission denied"**
Run `chmod +x /usr/local/bin/contextos` once.

**selftest fails with "no functional embeddings provider"**
This should not happen with the release binary, which bundles the ONNX model.
If it does, file an issue with the output of `contextos --selftest`.

---

## macOS (Intel)

### 1. Download and install

```sh
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-x64.tar.gz | tar xz
sudo mv osx-x64/contextos /usr/local/bin/contextos
```

### 2. Verify

```sh
contextos --version
contextos --selftest
```

### 3. Register with Claude Code

Same as Apple Silicon above; only the binary differs.

---

## Linux x64

### 1. Download and install

```sh
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-linux-x64.tar.gz | tar xz
sudo mv linux-x64/contextos /usr/local/bin/contextos
```

### 2. Verify

```sh
contextos --version
contextos --selftest
```

### 3. Register with Claude Code

```sh
claude mcp add --scope user contextos -- /usr/local/bin/contextos
```

### Troubleshooting

**libstdc++ errors**
The binary requires glibc 2.17+ (present on any distro from 2013 onward,
including Ubuntu 18.04+, Debian 9+, CentOS 7+).

**selftest reports dimension 0**
Unlikely with the release binary. File an issue if it occurs.

---

## Windows (x64)

### 1. Download and extract

```powershell
Invoke-WebRequest -Uri "https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-win-x64.zip" -OutFile contextos.zip
Expand-Archive contextos.zip -DestinationPath .
```

The zip extracts to a `win-x64\` folder. Move the binary somewhere
permanent before registering it, so the path does not change later.

```powershell
# Example: place it in C:\tools\ (create the directory first if needed)
New-Item -ItemType Directory -Force C:\tools | Out-Null
Move-Item win-x64\contextos.exe C:\tools\contextos.exe
```

### 2. Verify

```powershell
C:\tools\contextos.exe --version
C:\tools\contextos.exe --selftest
```

### 3. Register with Claude Code

```powershell
claude mcp add --scope user contextos -- C:\tools\contextos.exe
claude mcp list
```

### Troubleshooting

**"Windows protected your PC" (SmartScreen)**
Right-click the `.exe`, choose Properties, check "Unblock" at the bottom,
click OK. Or run from an admin PowerShell:
```powershell
Unblock-File C:\tools\contextos.exe
```

**"The system cannot find the file specified"**
Confirm the full path in the `claude mcp add` command matches where you
placed the binary. Use the full path, not a relative one.

**selftest fails on Windows**
The ONNX Runtime native DLL is bundled inside the single-file executable
and is extracted to a temp directory on first run. Antivirus software
sometimes blocks this. Add an exclusion for `%TEMP%\.net\contextos\`.

---

## .NET tool (dotnet tool install)

The .NET tool package (`ContextOS` on NuGet) is framework-dependent and
does not bundle the ONNX model or native runtime libs.

```sh
dotnet tool install -g ContextOS
```

After installing, you must configure a provider that does not need the
local ONNX model. The two options are Ollama and OpenAI.

### Configure Ollama

```sh
ollama serve         # start the Ollama daemon if it is not running
ollama pull nomic-embed-text
```

Create `~/.contextos/config.json`:

```json
{
  "embeddings": {
    "provider": "ollama",
    "ollamaUrl": "http://localhost:11434",
    "ollamaModel": "nomic-embed-text"
  }
}
```

### Configure OpenAI

```sh
export OPENAI_API_KEY=sk-...
```

Create `~/.contextos/config.json`:

```json
{
  "embeddings": {
    "provider": "openai",
    "openAiModel": "text-embedding-3-small"
  }
}
```

### Verify and register

```sh
contextos --selftest
claude mcp add --scope user contextos -- contextos
```

The `contextos` argument at the end is the tool command, not a path. The
.NET tool installer puts it on PATH automatically.

---

## First-time setup after registration

1. Open Claude Code in any git repo with at least a few commits.
2. Run `/mcp` and confirm `remember`, `recall`, and `context` appear.
3. Ask the agent: "What was I working on?" The agent should describe recent
   commits from the auto-hydrated context, without calling any tool.
4. Store a memory: "Use contextos remember to store: [a decision you just
   made]. Type: decision."
5. Close and reopen Claude Code. Ask again. The decision should appear in
   the response.

If any of those steps fail, see [docs/TROUBLESHOOTING.md](TROUBLESHOOTING.md).

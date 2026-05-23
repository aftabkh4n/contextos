# ContextOS installation

Detailed per-platform instructions. If you just want the quick version,
see the Install section in [README.md](../README.md).

> **v0.1.0 is the first release.** If downloading from "latest" returns a 404,
> the release has not been published yet. Check the
> [Releases tab](https://github.com/aftabkh4n/contextos/releases).

---

## macOS (Apple Silicon)

### 1. Download and extract

The archive contains the binary, a `.pdb` debug file, and a `Models/`
directory with the ONNX embedding model. Extract everything to a permanent
directory; the binary expects `Models/` to be next to it.

```sh
mkdir -p "$HOME/.local/share/contextos"
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-arm64.tar.gz \
  | tar xz --strip-components=1 -C "$HOME/.local/share/contextos"
```

`--strip-components=1` strips the `osx-arm64/` prefix from paths inside the
archive, so the binary lands at `$HOME/.local/share/contextos/contextos` and
`Models/` lands at `$HOME/.local/share/contextos/Models/`.

### 2. Verify

```sh
"$HOME/.local/share/contextos/contextos" --version
"$HOME/.local/share/contextos/contextos" --selftest
```

`--selftest` loads the ONNX model and embeds a short string. If it prints
a dimension count (384), the embeddings provider is working.

### 3. Register with Claude Code

```sh
claude mcp add --scope user contextos -- "$HOME/.local/share/contextos/contextos"
claude mcp list
```

The full path is passed directly, so this works whether or not
`~/.local/share/contextos` is on PATH.

You should see `contextos` in the list. Open a Claude Code session and
run `/mcp` to confirm `remember`, `recall`, and `context` appear.

### Optional: add to PATH

If you also want to type `contextos` at the command line:

```sh
mkdir -p "$HOME/.local/bin"
ln -s "$HOME/.local/share/contextos/contextos" "$HOME/.local/bin/contextos"
# Add to PATH if ~/.local/bin isn't already there:
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zshrc   # or ~/.bash_profile
```

Restart your shell after editing `.zshrc`.

### Cursor note

Cursor MCP support is compatible but auto-hydration has not been manually
verified. See [examples/cursor-config/README.md](../examples/cursor-config/README.md).

### Troubleshooting

**"permission denied" when running the binary**
```sh
chmod +x "$HOME/.local/share/contextos/contextos"
```

**selftest fails with "no functional embeddings provider"**
This should not happen with the release binary, which bundles the ONNX model
in a `Models/` folder next to the executable. If it does, confirm the
`Models/` directory is present:
```sh
ls "$HOME/.local/share/contextos/Models/"
```
If missing, re-extract from the archive.

---

## macOS (Intel)

Same steps as Apple Silicon; only the download URL differs.

### 1. Download and extract

```sh
mkdir -p "$HOME/.local/share/contextos"
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-osx-x64.tar.gz \
  | tar xz --strip-components=1 -C "$HOME/.local/share/contextos"
```

### 2. Verify

```sh
"$HOME/.local/share/contextos/contextos" --version
"$HOME/.local/share/contextos/contextos" --selftest
```

### 3. Register with Claude Code

```sh
claude mcp add --scope user contextos -- "$HOME/.local/share/contextos/contextos"
```

---

## Linux x64

### 1. Download and extract

```sh
mkdir -p "$HOME/.local/share/contextos"
curl -L https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-linux-x64.tar.gz \
  | tar xz --strip-components=1 -C "$HOME/.local/share/contextos"
```

### 2. Verify

```sh
"$HOME/.local/share/contextos/contextos" --version
"$HOME/.local/share/contextos/contextos" --selftest
```

### 3. Register with Claude Code

```sh
claude mcp add --scope user contextos -- "$HOME/.local/share/contextos/contextos"
claude mcp list
```

### Optional: add to PATH

```sh
mkdir -p "$HOME/.local/bin"
ln -s "$HOME/.local/share/contextos/contextos" "$HOME/.local/bin/contextos"
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
```

Restart your shell after editing `.bashrc`. On Ubuntu 20.04+,
`~/.local/bin` is already on PATH if the directory exists.

### Troubleshooting

**libstdc++ errors**
The binary requires glibc 2.17+ (any distro from 2013 onward: Ubuntu 18.04+,
Debian 9+, CentOS 7+).

**selftest reports dimension 0**
Unlikely with the release binary. File an issue if it occurs.

---

## Windows (x64)

### 1. Download and extract

```powershell
# Download
Invoke-WebRequest -Uri "https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-win-x64.zip" -OutFile contextos.zip

# Extract to a permanent location under your user profile (no admin required)
Expand-Archive contextos.zip -DestinationPath "$env:LOCALAPPDATA\Programs\contextos" -Force
```

The zip extracts a `win-x64\` folder, so the binary ends up at:

```
%LOCALAPPDATA%\Programs\contextos\win-x64\contextos.exe
```

The `Models\` directory containing the ONNX model lands next to it. Do not
move the `.exe` out of that folder or the model will not be found.

### 2. Verify

```powershell
& "$env:LOCALAPPDATA\Programs\contextos\win-x64\contextos.exe" --version
& "$env:LOCALAPPDATA\Programs\contextos\win-x64\contextos.exe" --selftest
```

`--selftest` should print dimension 384. If it fails, see Troubleshooting below.

### 3. Register with Claude Code

```powershell
claude mcp add --scope user contextos -- "$env:LOCALAPPDATA\Programs\contextos\win-x64\contextos.exe"
claude mcp list
```

The full path is used directly, so this works whether or not you set up PATH.

### Optional: add to PATH

If you want to type `contextos` anywhere in PowerShell:

```powershell
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$env:LOCALAPPDATA\Programs\contextos\win-x64", "User")
# Restart your PowerShell session for the change to take effect.
```

### Troubleshooting

**"Windows protected your PC" (SmartScreen)**
Right-click `contextos.exe` in Explorer, choose Properties, check "Unblock",
click OK. Or from PowerShell:
```powershell
Unblock-File "$env:LOCALAPPDATA\Programs\contextos\win-x64\contextos.exe"
```

**selftest fails on Windows**
The ONNX Runtime native DLL is bundled inside the single-file executable and
is extracted to a temp directory on first run. Antivirus software sometimes
blocks this extraction. Add an exclusion for `%TEMP%\.net\contextos\` and retry.

**"The system cannot find the path specified"**
The `$env:LOCALAPPDATA` expansion must happen in the same PowerShell session
where you run the commands. If you copy a path with a literal `%LOCALAPPDATA%`
into `claude mcp add`, use the expanded form (e.g.
`C:\Users\yourname\AppData\Local\Programs\contextos\win-x64\contextos.exe`).

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

The `contextos` argument at the end is the tool command name. The .NET tool
installer puts it on PATH automatically.

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

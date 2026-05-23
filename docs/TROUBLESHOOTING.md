# ContextOS troubleshooting

---

## Server shows as "failed" in `claude mcp list`

The server process crashed or exited during startup. Check the log file:

```
~/.contextos/logs/contextos-<date>.log
```

Common causes:

**ONNX model missing (dotnet tool installs only)**
The .NET tool package does not bundle the model. Configure Ollama or OpenAI
as the provider. See [INSTALL.md](INSTALL.md) for provider setup.

**Binary not executable (macOS / Linux)**
```sh
chmod +x /usr/local/bin/contextos
```

**Wrong path in the MCP registration**
Run `claude mcp list` and check the command shown. The path must be absolute
and must point to the actual binary. Re-register with the correct path:
```sh
claude mcp remove contextos
claude mcp add --scope user contextos -- /correct/path/to/contextos
```

---

## Tools do not appear in `/mcp`

The server is listed in `claude mcp list` but `remember`, `recall`, and
`context` do not show up inside a Claude Code session.

Claude Code re-handshakes with MCP servers at session start. Exit Claude Code
completely and reopen it. If the tools still do not appear, check the log file
for errors during the `initialize` exchange.

---

## selftest fails: "no functional embeddings provider"

Full error from `contextos --selftest`:

```
ContextOS cannot start: no functional embeddings provider.
The configured provider is: onnx
...
```

For release binaries this means the bundled ONNX model could not be loaded.
On Windows, antivirus software sometimes blocks the native DLL that the
single-file executable extracts to `%TEMP%\.net\contextos\`. Add an exclusion
for that path and retry.

For .NET tool installs, this is expected when no provider is configured.
Follow the [INSTALL.md .NET tool section](INSTALL.md#net-tool-dotnet-tool-install)
to configure Ollama or OpenAI.

---

## recall returns no results after storing memories

You stored a memory with `remember` and `recall` returns an empty list.

**Embeddings were not written**
If the embeddings provider failed during the `remember` call, the memory is
stored as text but has no embedding vector. FTS5 search will still find it;
vector search will not. Check the log for embedding errors at the time of the
`remember` call.

**FTS5 index out of sync**
Rare. Delete the workspace `.db` file and re-store your memories:
```sh
rm ~/.contextos/<workspace_id>.db
```
The workspace ID appears in the log file.

**Query has no overlap with stored content**
Try a more literal query that uses words from the memory text.

---

## Dimension mismatch: server refuses to start

Full error:

```
Workspace was indexed with embeddings of dimension 384 but
the current provider produces dimension 1536.
Either revert config to a matching provider, or delete the workspace DB
at /home/user/.contextos/abc123def456789a.db to reindex from scratch.
```

You switched the embeddings provider after memories were already stored.
Options:

1. Revert the provider in `~/.contextos/config.json` to match the original.
2. Delete the named `.db` file and re-store memories from scratch. The file
   name is shown in the error message.

Switching between providers with identical output dimensions (for example,
`text-embedding-3-small` and `text-embedding-ada-002`, both 1536) does not
trigger this error, but the stored embeddings will be inconsistent. A full
reindex is still recommended.

---

## File lock errors during `dotnet build` (contributors)

The build fails with `MSB3027` errors because Claude Code keeps the server
process running and the DLLs are locked.

Options:

1. Exit the Claude Code session that has contextos registered, then build.
2. Temporarily remove the server, build, then re-add:
   ```sh
   claude mcp remove contextos
   dotnet build
   claude mcp add --scope user contextos -- dotnet run \
     --project /path/to/contextos/src/ContextOS.Mcp --no-build
   ```

This only affects contributors building from source. Users of the release
binary are not affected.

---

## Auto-hydration is empty or says "no memory yet"

The agent says "I have no context for this workspace" or shows only the empty
workspace prompt, even though you have stored memories.

**Wrong workspace**
ContextOS derives the workspace ID from the absolute path of the nearest `.git`
directory. If the DB was created in one directory but you opened Claude Code
in a subdirectory that has its own `.git`, they will have different IDs.

Check which DB file is active by looking at the log:
```
~/.contextos/logs/contextos-<date>.log
```
Look for "Opened database" near the start of the session.

**Memories are all archived**
If memories were stored with `archived_at` set, they do not appear in
auto-hydration. Use `recall` to confirm memories are visible.

**Client is not using `serverInfo.instructions`**
Some MCP clients do not pass `serverInfo.instructions` to the model. This
is a client-side behavior that ContextOS cannot control. Auto-hydration
will not work in those clients; use the `context` tool explicitly instead.

---

## Logs

All log files are in `~/.contextos/logs/`. Override the base directory with
the `CONTEXTOS_HOME` environment variable:

```sh
CONTEXTOS_HOME=/tmp/contextos-test contextos --version
```

Log level defaults to `Information`. To see more detail, add to
`~/.contextos/config.json`:

```json
{
  "logging": {
    "level": "Debug"
  }
}
```

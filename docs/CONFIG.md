# ContextOS configuration reference

ContextOS reads `~/.contextos/config.json` at startup. The file is optional;
if it is absent, all defaults apply and the ONNX provider is used.

The base directory can be overridden with the `CONTEXTOS_HOME` environment
variable. This changes where both `config.json` and workspace databases are
stored.

---

## Full schema

```json
{
  "embeddings": {
    "provider": "onnx",
    "ollamaUrl": "http://localhost:11434",
    "ollamaModel": "nomic-embed-text",
    "openAiModel": "text-embedding-3-small",
    "openAiApiKey": null
  },
  "hydration": {
    "enabled": true,
    "maxBytes": 2048
  }
}
```

---

## Embeddings providers

### onnx (default)

No setup required. Runs `all-MiniLM-L6-v2` locally via ONNX Runtime.
Download the model with:

```sh
bash scripts/fetch-model.sh
```

Output dimension: 384.

### ollama

Calls a locally running Ollama instance.

```json
{
  "embeddings": {
    "provider": "ollama",
    "ollamaUrl": "http://localhost:11434",
    "ollamaModel": "nomic-embed-text"
  }
}
```

Start Ollama with `ollama serve` and pull the model with
`ollama pull nomic-embed-text` before starting ContextOS.

Output dimension: 768 for nomic-embed-text. If you use a different model,
update the dimension field manually and reindex (see below).

### openai

Calls the OpenAI embeddings API.

```json
{
  "embeddings": {
    "provider": "openai",
    "openAiModel": "text-embedding-3-small",
    "openAiApiKey": "sk-..."
  }
}
```

Setting `openAiApiKey` in the config file is supported but storing
secrets on disk is not ideal. The `OPENAI_API_KEY` environment variable
takes precedence:

```sh
export OPENAI_API_KEY=sk-...
```

Supported models and their output dimensions:

| Model | Dimension |
|-------|-----------|
| text-embedding-3-small | 1536 |
| text-embedding-3-large | 3072 |
| text-embedding-ada-002 | 1536 |

Requests are batched in groups of 100 inputs.

---

## Switching providers

Each workspace database stores embeddings at the dimension produced by the
provider that was active when memories were first stored.

If you switch to a provider with a different output dimension, ContextOS
refuses to start and prints:

```
Workspace was indexed with embeddings of dimension 384 but
the current provider produces dimension 1536.
Either revert config to a matching provider, or delete the workspace DB
at /home/user/.contextos/abc123def456789a.db to reindex from scratch.
(Reindex tool coming in v2.)
```

To reindex: delete the `.db` file and re-store your memories. The
workspace starts fresh on the next launch.

Switching between providers with the same output dimension (for example,
`text-embedding-3-small` and `text-embedding-ada-002`, both 1536) does not
trigger this check, but the semantic space will be inconsistent. A full
reindex is still recommended.

---

## Hydration

Controls how much context is injected into the MCP `initialize` response.

| Key | Default | Description |
|-----|---------|-------------|
| `enabled` | `true` | Set to `false` to disable auto-hydration entirely. |
| `maxBytes` | `2048` | Maximum byte size of the injected context blob. |

Hydration is currently always active; the `enabled` flag is reserved for a
future release.

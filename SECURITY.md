# Security Policy

## Supported versions

Security fixes go into the latest release only. Upgrade to the newest
version from the [releases page](https://github.com/aftabkh4n/contextos/releases)
or with `dotnet tool update -g ContextOS`.

| Version | Supported |
|---|---|
| Latest release | Yes |
| Older releases | No |

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub:
**Security** tab, then **Report a vulnerability**
(https://github.com/aftabkh4n/contextos/security/advisories/new).

Do not open a public issue for security problems.

You can expect a first reply within 7 days. If the report is confirmed,
a fix is released and the advisory is published once users can upgrade.

## Scope

ContextOS runs locally. It stores memories in SQLite files under
`~/.contextos` and sends no telemetry. The most relevant areas for reports are:

- the MCP server and its tools (`remember`, `remember_skill`, `recall`, `context`)
- how the release binaries are built and downloaded, including the
  [Claude Code plugin](https://github.com/aftabkh4n/contextos-plugin)
- the optional OpenAI and Ollama embedding providers, which send memory text
  to the configured endpoint

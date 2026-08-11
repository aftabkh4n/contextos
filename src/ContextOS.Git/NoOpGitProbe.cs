using ContextOS.Core;

namespace ContextOS.Git;

/// <summary>
/// Fallback used when LibGit2Sharp's native library is unavailable (e.g. dotnet tool installs).
/// Returns null for all probes so the server starts normally without git context.
/// </summary>
public sealed class NoOpGitProbe : IGitProbe
{
    /// <inheritdoc/>
    public GitInfo? Probe(string repoRoot, CancellationToken ct = default) => null;
}

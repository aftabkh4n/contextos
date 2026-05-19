namespace ContextOS.Core;

/// <summary>
/// Abstracts git repository inspection so the MCP layer can be tested without LibGit2Sharp.
/// </summary>
public interface IGitProbe
{
    /// <summary>
    /// Probes the git repository at <paramref name="repoRoot"/>.
    /// Returns null if <paramref name="repoRoot"/> is not a git repository.
    /// Never throws for a missing or invalid repo; only throws on real library errors.
    /// </summary>
    GitInfo? Probe(string repoRoot, CancellationToken ct = default);
}

/// <summary>Git state snapshot for a workspace, captured at startup.</summary>
public record GitInfo(
    string? Branch,
    IReadOnlyList<GitCommit> RecentCommits,
    int UncommittedFileCount);

/// <summary>A single commit entry in the recent-commit list.</summary>
public record GitCommit(
    string Sha,
    string ShortSha,
    string Message,
    string Author,
    long CommittedAtUnix);

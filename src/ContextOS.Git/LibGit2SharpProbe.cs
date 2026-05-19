using ContextOS.Core;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace ContextOS.Git;

/// <summary>
/// Reads branch, recent commits, and dirty-file count from a local git repository
/// via LibGit2Sharp.
/// </summary>
public sealed class LibGit2SharpProbe : IGitProbe
{
    private readonly ILogger<LibGit2SharpProbe> _logger;

    public LibGit2SharpProbe(ILogger<LibGit2SharpProbe> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public GitInfo? Probe(string repoRoot, CancellationToken ct = default)
    {
        try
        {
            if (!Repository.IsValid(repoRoot))
                return null;

            using var repo = new Repository(repoRoot);

            string? branch = repo.Info.IsHeadDetached ? null : repo.Head.FriendlyName;

            List<GitCommit> commits = repo.Commits
                .Take(10)
                .Select(c => new GitCommit(
                    c.Sha,
                    c.Sha[..7],
                    TrimMessage(c.MessageShort),
                    c.Author.Name,
                    c.Committer.When.ToUnixTimeSeconds()))
                .ToList();

            var statusOpts = new StatusOptions { IncludeIgnored = false, ExcludeSubmodules = true };
            int dirty = repo.RetrieveStatus(statusOpts).Count();

            return new GitInfo(branch, commits, dirty);
        }
        catch (LibGit2SharpException ex)
        {
            _logger.LogWarning(ex, "Git probe failed for {RepoRoot}; continuing without git data.", repoRoot);
            return null;
        }
    }

    /// <summary>
    /// Walks up from <paramref name="startPath"/> to find a git working directory root.
    /// Returns the working directory path, or null if no repository was found.
    /// </summary>
    public static string? DiscoverRoot(string startPath)
    {
        try
        {
            string? gitDir = Repository.Discover(startPath);
            if (gitDir is null) return null;
            using var repo = new Repository(gitDir);
            return repo.Info.WorkingDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
    }

    private static string TrimMessage(string message)
    {
        string line = message.Split('\n', 2)[0].Trim();
        return line.Length > 80 ? line[..80] : line;
    }
}

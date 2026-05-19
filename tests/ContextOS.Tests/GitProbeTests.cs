using ContextOS.Core;
using ContextOS.Git;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextOS.Tests;

/// <summary>
/// Tests for <see cref="LibGit2SharpProbe"/> using temporary git repositories
/// created and destroyed in-process via LibGit2Sharp.
/// </summary>
public sealed class GitProbeTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // -------------------------------------------------------------------------
    // Probe returns null for non-repo directories
    // -------------------------------------------------------------------------

    [Fact]
    public void Probe_DirectoryWithNoGit_ReturnsNull()
    {
        string dir = CreateTempDir();
        var probe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);

        GitInfo? info = probe.Probe(dir);

        Assert.Null(info);
    }

    // -------------------------------------------------------------------------
    // Probe returns correct commit list
    // -------------------------------------------------------------------------

    [Fact]
    public void Probe_RepoWithThreeCommits_ReturnsThreeCommits()
    {
        string dir = CreateTempDir();
        Repository.Init(dir);
        using (var repo = new Repository(dir))
        {
            MakeCommit(repo, dir, "file1.txt", "Commit 1");
            MakeCommit(repo, dir, "file2.txt", "Commit 2");
            MakeCommit(repo, dir, "file3.txt", "Commit 3");
        }

        var probe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);
        GitInfo? info = probe.Probe(dir);

        Assert.NotNull(info);
        Assert.Equal(3, info.RecentCommits.Count);
        // Commits are returned newest first.
        Assert.Equal("Commit 3", info.RecentCommits[0].Message);
        Assert.Equal(7, info.RecentCommits[0].ShortSha.Length);
        Assert.Equal(info.RecentCommits[0].Sha[..7], info.RecentCommits[0].ShortSha);
    }

    [Fact]
    public void Probe_RepoWithNoCommits_ReturnsEmptyCommitList()
    {
        string dir = CreateTempDir();
        Repository.Init(dir);

        var probe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);
        GitInfo? info = probe.Probe(dir);

        Assert.NotNull(info);
        Assert.Empty(info.RecentCommits);
    }

    // -------------------------------------------------------------------------
    // Uncommitted file count
    // -------------------------------------------------------------------------

    [Fact]
    public void Probe_OneUntrackedFile_CountsAsUncommitted()
    {
        string dir = CreateTempDir();
        Repository.Init(dir);
        using (var repo = new Repository(dir))
        {
            // One commit so status baseline is stable.
            MakeCommit(repo, dir, "initial.txt", "Initial commit");
        }

        // Create a new file without staging or committing it.
        File.WriteAllText(Path.Combine(dir, "untracked.txt"), "hello");

        var probe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);
        GitInfo? info = probe.Probe(dir);

        Assert.NotNull(info);
        Assert.Equal(1, info.UncommittedFileCount);
    }

    [Fact]
    public void Probe_CleanRepo_ReturnsZeroUncommitted()
    {
        string dir = CreateTempDir();
        Repository.Init(dir);
        using (var repo = new Repository(dir))
        {
            MakeCommit(repo, dir, "file.txt", "Commit");
        }

        var probe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);
        GitInfo? info = probe.Probe(dir);

        Assert.NotNull(info);
        Assert.Equal(0, info.UncommittedFileCount);
    }

    // -------------------------------------------------------------------------
    // Branch name
    // -------------------------------------------------------------------------

    [Fact]
    public void Probe_RepoWithCommit_ReturnsBranchName()
    {
        string dir = CreateTempDir();
        Repository.Init(dir);
        using (var repo = new Repository(dir))
        {
            MakeCommit(repo, dir, "f.txt", "Init");
        }

        var probe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);
        GitInfo? info = probe.Probe(dir);

        Assert.NotNull(info);
        Assert.NotNull(info.Branch);
        Assert.False(string.IsNullOrWhiteSpace(info.Branch));
    }

    // -------------------------------------------------------------------------
    // DiscoverRoot
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverRoot_FromRepoSubdirectory_ReturnsRepoRoot()
    {
        string dir = CreateTempDir();
        Repository.Init(dir);
        using (var repo = new Repository(dir))
        {
            MakeCommit(repo, dir, "f.txt", "Init");
        }

        string subDir = Path.Combine(dir, "sub");
        Directory.CreateDirectory(subDir);

        string? root = LibGit2SharpProbe.DiscoverRoot(subDir);

        Assert.NotNull(root);
        // Normalize both paths for comparison (trailing separators, case on Windows).
        Assert.Equal(
            dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .ToLowerInvariant(),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant());
    }

    [Fact]
    public void DiscoverRoot_NongitDirectory_ReturnsNull()
    {
        string dir = CreateTempDir();

        string? root = LibGit2SharpProbe.DiscoverRoot(dir);

        Assert.Null(root);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"contextos-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static void MakeCommit(Repository repo, string repoDir, string fileName, string message)
    {
        File.WriteAllText(Path.Combine(repoDir, fileName), message);
        Commands.Stage(repo, "*");
        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.UtcNow);
        repo.Commit(message, sig, sig);
    }

    public void Dispose()
    {
        foreach (string dir in _tempDirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                // LibGit2Sharp marks some .git files readonly on Windows; clear before delete.
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
            catch
            {
                // Best-effort cleanup; don't fail the test run on cleanup errors.
            }
        }
    }
}

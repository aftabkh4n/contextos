using System.Text;
using ContextOS.Core;
using ContextOS.Storage;

namespace ContextOS.Tests;

/// <summary>
/// Unit tests for <see cref="HydrationBuilder"/>. Each test gets a fresh in-memory SQLite store.
/// No ONNX model needed — the store is opened without an embeddings provider.
/// </summary>
public sealed class HydrationBuilderTests : IAsyncLifetime
{
    private SqliteStore _store = null!;

    private const string WorkspaceId   = "hydration-test-ws";
    private const string WorkspaceName = "test-workspace";

    public async Task InitializeAsync()
    {
        _store = await SqliteStore.OpenAsync(":memory:");
        await _store.UpsertWorkspaceAsync(
            new Workspace(WorkspaceId, "/tmp/test-workspace", WorkspaceName, null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();

    private Task<Memory> Add(string content, string type = "note", double importance = 0.5, string? tags = null) =>
        _store.AddMemoryAsync(WorkspaceId, type, content, tags: tags, importance: importance);

    // -------------------------------------------------------------------------
    // Empty workspace
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReturnsEmptyWorkspaceMessage_WhenNoMemoriesAndNoGit()
    {
        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo: null);

        Assert.Equal(HydrationBuilder.EmptyWorkspaceMessage, blob);
    }

    [Fact]
    public void EmptyWorkspaceMessage_IsSingleLine()
    {
        Assert.DoesNotContain('\n', HydrationBuilder.EmptyWorkspaceMessage);
    }

    [Fact]
    public async Task DoesNotReturnEmptyMessage_WhenGitInfoPresent()
    {
        var gitInfo = new GitInfo("main", [], 0);

        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo);

        Assert.NotEqual(HydrationBuilder.EmptyWorkspaceMessage, blob);
    }

    [Fact]
    public async Task DoesNotReturnEmptyMessage_WhenMemoriesExist()
    {
        await Add("a stored decision", "decision");

        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo: null);

        Assert.NotEqual(HydrationBuilder.EmptyWorkspaceMessage, blob);
    }

    // -------------------------------------------------------------------------
    // Full blob structure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReturnsBlobWithPopulatedSections_WhenDataExists()
    {
        await Add("Fix the login bug", "todo");
        await Add("Use SQLite over Postgres for zero-dependency installs", "decision", importance: 0.9);

        var commits = new List<GitCommit>
        {
            new("aabbccdd11223344", "aabbccd", "Add outbox migration", "Dev",
                DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds()),
        };
        var gitInfo = new GitInfo("feature/outbox", commits, UncommittedFileCount: 2);

        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo);

        Assert.Contains("Branch: feature/outbox", blob);
        Assert.Contains("Active tasks:", blob);
        Assert.Contains("Recent decisions:", blob);
        Assert.Contains("Fix the login bug", blob);
        Assert.Contains("Use SQLite over Postgres", blob);
        Assert.Contains("aabbccd", blob);
    }

    [Fact]
    public async Task OmitsEmptySections()
    {
        var gitInfo = new GitInfo("main", [], 0);
        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo);

        Assert.DoesNotContain("Active tasks:", blob);
        Assert.DoesNotContain("Skills:", blob);
        Assert.DoesNotContain("Recent decisions:", blob);
        Assert.DoesNotContain("(none)", blob);
    }

    [Fact]
    public async Task CommitMessagesTruncatedToFiftyChars()
    {
        string longMsg = new('m', 120);
        var commits = new List<GitCommit>
        {
            new("abcdef1234567890", "abcdef1", longMsg, "Dev",
                DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds()),
        };
        var gitInfo = new GitInfo("main", commits, 0);

        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo);

        Assert.Contains("abcdef1", blob);
        Assert.Contains(new string('m', 50) + "...", blob);
        Assert.DoesNotContain(new string('m', 60), blob);
    }

    [Fact]
    public async Task BlobStartsWithFramingPrefix()
    {
        await Add("some decision", "decision");

        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo: null);

        Assert.StartsWith("ContextOS workspace context", blob);
    }

    [Fact]
    public async Task BlobWithData_ContainsAnswerFromContextRule()
    {
        await Add("some decision", "decision");

        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo: null);

        Assert.Contains(HydrationBuilder.AnswerFromContextRule, blob);
    }

    // -------------------------------------------------------------------------
    // Size budget
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BlobIsUnder1000Chars_WithTwentyDecisionsTwentyTodosTenCommits()
    {
        string longLine = new('x', 120);
        for (int i = 0; i < 20; i++)
            await Add($"Todo {i:D2}: {longLine}", "todo");

        for (int i = 0; i < 20; i++)
            await Add($"Decision {i:D2}: {longLine}", "decision", importance: 0.8);

        var commits = new List<GitCommit>();
        for (int i = 0; i < 10; i++)
            commits.Add(new($"sha{i:D13}", $"sha{i:D4}", new string('m', 200), "Dev",
                DateTimeOffset.UtcNow.AddHours(-i - 1).ToUnixTimeSeconds()));
        var gitInfo = new GitInfo("feature/very-long-branch-name-that-keeps-going", commits, 99);

        string blob = await HydrationBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, gitInfo);

        Assert.Contains(HydrationBuilder.AnswerFromContextRule, blob);
        Assert.True(blob.Length <= 1000, $"Blob must be at most 1000 chars. Got {blob.Length}.\n---\n{blob}");
        Assert.True(Encoding.UTF8.GetByteCount(blob) <= 1000,
            $"Blob must be at most 1000 bytes. Got {Encoding.UTF8.GetByteCount(blob)} bytes.");
    }

    // -------------------------------------------------------------------------
    // hydration_log
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LogHydration_RowCountIncrementsPerCall()
    {
        await _store.LogHydrationAsync(WorkspaceId, "session-1", "hash-a");
        await _store.LogHydrationAsync(WorkspaceId, "session-2", "hash-b");

        long count = await HydrationLogCountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task LogHydration_OverwritesSameSession()
    {
        await _store.LogHydrationAsync(WorkspaceId, "session-x", "hash-1");
        await _store.LogHydrationAsync(WorkspaceId, "session-x", "hash-2");

        long count = await HydrationLogCountAsync();
        Assert.Equal(1, count);
    }

    private async Task<long> HydrationLogCountAsync()
    {
        using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM hydration_log WHERE workspace_id = @w";
        cmd.Parameters.AddWithValue("@w", WorkspaceId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}

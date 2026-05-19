using System.Text;
using ContextOS.Core;
using ContextOS.Storage;

namespace ContextOS.Tests;

/// <summary>
/// Unit tests for <see cref="ContextBuilder"/>. Each test gets a fresh in-memory
/// SQLite store. No ONNX model needed — the store is opened without an embeddings
/// provider so memory inserts don't compute vectors.
/// </summary>
public sealed class ContextToolTests : IAsyncLifetime
{
    private SqliteStore _store = null!;

    private const string WorkspaceId   = "ctx-tool-test-ws";
    private const string WorkspaceName = "test-workspace";

    public async Task InitializeAsync()
    {
        _store = await SqliteStore.OpenAsync(":memory:");
        await _store.UpsertWorkspaceAsync(
            new Workspace(WorkspaceId, "/tmp/test-workspace", WorkspaceName, null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();

    // Shorthand helpers.
    private Task<Memory> Add(string content, string type = "note",
        double importance = 0.5, string? tags = null)
        => _store.AddMemoryAsync(WorkspaceId, type, content,
            tags: tags, importance: importance);

    private Task<string> Context(string scope = "current") =>
        ContextBuilder.BuildAsync(_store, WorkspaceId, WorkspaceName, scope);

    // -------------------------------------------------------------------------
    // scope=current — structure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Current_EmptyWorkspace_AllSectionHeadersPresent()
    {
        string md = await Context("current");

        Assert.Contains($"# ContextOS workspace: {WorkspaceName}", md);
        Assert.Contains("## Branch", md);
        Assert.Contains("## Active tasks", md);
        Assert.Contains("## Recent decisions", md);
        Assert.True(Encoding.UTF8.GetByteCount(md) < 2048,
            $"Empty context must be well under 2 KB. Got {Encoding.UTF8.GetByteCount(md)} bytes.");
    }

    [Fact]
    public async Task Current_EmptyWorkspace_SectionsShowNonePlaceholder()
    {
        string md = await Context("current");

        // Should contain "(none)" for both empty sections.
        int noneCount = CountOccurrences(md, "(none)");
        Assert.True(noneCount >= 2, $"Expected at least 2 '(none)' placeholders, got {noneCount}. Output:\n{md}");
    }

    // -------------------------------------------------------------------------
    // scope=current — content selection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Current_ShowsAllTodosAndTopDecisionsButNotNotes()
    {
        for (int i = 1; i <= 5; i++)
            await Add($"Todo item {i}", "todo");

        // Three decisions with distinct importance so ordering is deterministic
        // (all share the same creation timestamp, so recency is equal).
        await Add("Decision alpha", "decision", importance: 0.9);
        await Add("Decision beta",  "decision", importance: 0.7);
        await Add("Decision gamma", "decision", importance: 0.5);

        await Add("Plain note one",  "note");
        await Add("Plain note two", "note");

        string md = await Context("current");

        // All 5 todos appear.
        for (int i = 1; i <= 5; i++)
            Assert.Contains($"Todo item {i}", md);

        // All 3 decisions appear, ordered highest importance first.
        int idxAlpha = md.IndexOf("Decision alpha", StringComparison.Ordinal);
        int idxBeta  = md.IndexOf("Decision beta",  StringComparison.Ordinal);
        int idxGamma = md.IndexOf("Decision gamma", StringComparison.Ordinal);
        Assert.True(idxAlpha >= 0 && idxBeta >= 0 && idxGamma >= 0, "All three decisions must appear.");
        Assert.True(idxAlpha < idxBeta  && idxBeta < idxGamma,
            "Decisions must appear in descending importance order.");

        // Plain notes must be absent.
        Assert.DoesNotContain("Plain note one",  md);
        Assert.DoesNotContain("Plain note two", md);
    }

    [Fact]
    public async Task Current_ActiveTaggedNonTodo_AppearsInActiveTasks()
    {
        await Add("ongoing architecture review", "note", tags: "active");
        await Add("unrelated gotcha", "gotcha");

        string md = await Context("current");

        Assert.Contains("ongoing architecture review", md);
        Assert.DoesNotContain("unrelated gotcha", md);
    }

    [Fact]
    public async Task Current_CapsActiveTasks_AtTen()
    {
        for (int i = 1; i <= 12; i++)
            await Add($"Todo {i}", "todo");

        string md = await Context("current");

        int itemCount = md.Split('\n')
            .Count(l => l.TrimStart().StartsWith("- [todo]", StringComparison.Ordinal));
        Assert.Equal(10, itemCount);
    }

    // -------------------------------------------------------------------------
    // scope=week
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Week_ReturnsAtMost20Memories_WhenMoreThan20Exist()
    {
        // Insert 30 memories right now — all fall within the last 7 days.
        for (int i = 1; i <= 30; i++)
            await Add($"Week memory {i}");

        string md = await Context("week");

        int count = md.Split('\n')
            .Count(l => l.TrimStart().StartsWith("- [", StringComparison.Ordinal));
        Assert.Equal(20, count);
    }

    [Fact]
    public async Task Week_EmptyWorkspace_ReturnsValidMarkdown()
    {
        string md = await Context("week");

        Assert.Contains($"# ContextOS workspace: {WorkspaceName}", md);
        Assert.Contains("(none)", md);
        Assert.True(Encoding.UTF8.GetByteCount(md) < 2048);
    }

    // -------------------------------------------------------------------------
    // scope=all
    // -------------------------------------------------------------------------

    [Fact]
    public async Task All_TruncatesGracefullyWhenContentExceeds2KB()
    {
        string longContent = new('x', 80);
        for (int i = 0; i < 100; i++)
            await Add($"Memory {i:D3}: {longContent}");

        string md = await Context("all");

        Assert.True(Encoding.UTF8.GetByteCount(md) <= 2048,
            $"Output must fit in 2 KB. Got {Encoding.UTF8.GetByteCount(md)} bytes.");
        Assert.Contains("truncated to fit 2 KB budget", md);
    }

    [Fact]
    public async Task All_EmptyWorkspace_ReturnsValidMarkdown()
    {
        string md = await Context("all");

        Assert.Contains($"# ContextOS workspace: {WorkspaceName}", md);
        Assert.Contains("(none)", md);
        Assert.True(Encoding.UTF8.GetByteCount(md) < 2048);
    }

    // -------------------------------------------------------------------------
    // Invalid scope
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidScope_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Context("bogus"));
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }
}

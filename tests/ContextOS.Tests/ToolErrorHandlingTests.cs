using ContextOS.Core;
using ContextOS.Mcp;
using ContextOS.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextOS.Tests;

/// <summary>
/// Unit tests for tool-level error handling. Uses fake stores/search that throw
/// to verify the tools wrap runtime exceptions in clean messages and pass
/// validation errors through unchanged.
/// </summary>
public sealed class ToolErrorHandlingTests
{
    private static readonly WorkspaceContext FakeWs = new("ws1", "/fake", null);

    // -------------------------------------------------------------------------
    // RememberTool
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RememberTool_StoreThrows_WrapsInCleanException()
    {
        var tool = new RememberTool(
            new ThrowingMemoryStore(),
            FakeWs,
            NullLogger<RememberTool>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.RememberAsync("some valid content", "note"));

        Assert.DoesNotContain("Simulated", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ThrowingMemoryStore", ex.Message);
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public async Task RememberTool_EmptyContent_ThrowsArgumentException()
    {
        var tool = new RememberTool(
            new ThrowingMemoryStore(),
            FakeWs,
            NullLogger<RememberTool>.Instance);

        // Validation errors must pass through as ArgumentException so the
        // MCP SDK converts them to isError responses with the original message.
        await Assert.ThrowsAsync<ArgumentException>(() => tool.RememberAsync("", "note"));
    }

    [Fact]
    public async Task RememberTool_InvalidType_ThrowsArgumentException()
    {
        var tool = new RememberTool(
            new ThrowingMemoryStore(),
            FakeWs,
            NullLogger<RememberTool>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(
            () => tool.RememberAsync("some content", "bad_type"));
    }

    // -------------------------------------------------------------------------
    // RecallTool
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecallTool_SearchThrows_WrapsInCleanException()
    {
        var tool = new RecallTool(
            new ThrowingSearch(),
            FakeWs,
            NullLogger<RecallTool>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.RecallAsync("some query"));

        Assert.DoesNotContain("Simulated", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public async Task RecallTool_EmptyQuery_ThrowsArgumentException()
    {
        var tool = new RecallTool(
            new ThrowingSearch(),
            FakeWs,
            NullLogger<RecallTool>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => tool.RecallAsync(""));
    }

    // -------------------------------------------------------------------------
    // ContextTool
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ContextTool_StoreThrows_WrapsInCleanException()
    {
        var tool = new ContextTool(
            new ThrowingMemoryStore(),
            FakeWs,
            NullLogger<ContextTool>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.ContextAsync("current"));

        Assert.DoesNotContain("Simulated", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(ex.Message);
    }

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class ThrowingMemoryStore : IMemoryStore
    {
        public Task<Memory> AddMemoryAsync(string workspaceId, string type, string content,
            string? source = null, string? tags = null, double importance = 0.5,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated DB failure.");

        public Task<Memory?> GetMemoryByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult<Memory?>(null);

        public Task<IReadOnlyList<Memory>> ListMemoriesAsync(string workspaceId,
            MemoryFilter? filter = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated DB failure.");

        public Task<bool> DeleteMemoryAsync(string id, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task UpsertWorkspaceAsync(Workspace workspace, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Workspace?> GetWorkspaceAsync(string id, CancellationToken ct = default)
            => Task.FromResult<Workspace?>(null);

        public Task LogHydrationAsync(string workspaceId, string sessionId,
            string contextHash, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingSearch : ISearch
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(string workspaceId, string query,
            int k = 5, IReadOnlyCollection<string>? types = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated search failure.");
    }
}

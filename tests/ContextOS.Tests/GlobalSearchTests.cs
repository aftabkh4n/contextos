using System.Text.Json;
using ContextOS.Core;
using ContextOS.Mcp;
using ContextOS.Mcp.Tools;
using ContextOS.Retrieval;
using ContextOS.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextOS.Tests;

/// <summary>
/// Tests for cross-workspace global search.
/// Uses file-based SQLite stores (not :memory:) so HybridSearch can discover
/// workspace DBs by scanning a temp directory.
/// Does not require the ONNX model — stores have no embeddings, so only FTS5
/// keyword matching is exercised.
/// </summary>
public sealed class GlobalSearchTests : IAsyncLifetime
{
    private string _tempHome = null!;
    private SqliteStore _storeA = null!;
    private SqliteStore _storeB = null!;
    private string _dbPathA = null!;
    private string _dbPathB = null!;

    private const string WsIdA   = "ws-global-a";
    private const string WsIdB   = "ws-global-b";
    private const string WsNameA = "WorkspaceAlpha";
    private const string WsNameB = "WorkspaceBeta";

    // IEmbeddingsProvider that always returns a 1-dim zero vector so we can
    // instantiate HybridSearch without the ONNX model.
    private sealed class ZeroProvider : IEmbeddingsProvider
    {
        public string Name => "zero";
        public int Dimension => 1;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new float[] { 0f });
        public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new float[] { 0f }).ToArray());
    }

    private static readonly ZeroProvider Provider = new();

    public async Task InitializeAsync()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"contextos-global-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempHome);

        _dbPathA = Path.Combine(_tempHome, $"{WsIdA}.db");
        _dbPathB = Path.Combine(_tempHome, $"{WsIdB}.db");

        _storeA = await SqliteStore.OpenAsync(_dbPathA);
        _storeB = await SqliteStore.OpenAsync(_dbPathB);

        await _storeA.UpsertWorkspaceAsync(
            new Workspace(WsIdA, "/repos/alpha", WsNameA, null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await _storeB.UpsertWorkspaceAsync(
            new Workspace(WsIdB, "/repos/beta", WsNameB, null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public async Task DisposeAsync()
    {
        await _storeA.DisposeAsync();
        await _storeB.DisposeAsync();
        try { Directory.Delete(_tempHome, recursive: true); } catch { /* best-effort */ }
    }

    // HybridSearch that treats workspace B as "current" and scans tempHome for others.
    private HybridSearch SearchFromB() =>
        new(_storeB.Connection, Provider, contextosHomeOverride: _tempHome);

    // HybridSearch that treats workspace A as "current".
    private HybridSearch SearchFromA() =>
        new(_storeA.Connection, Provider, contextosHomeOverride: _tempHome);

    // -------------------------------------------------------------------------
    // Cross-workspace discovery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GlobalSearch_FindsMemory_FromOtherWorkspace()
    {
        Memory m = await _storeA.AddMemoryAsync(WsIdA, MemoryTypes.Decision,
            "use postgres for the user service");

        SearchResult[] results = await SearchFromB().SearchGlobalAsync("postgres user service", k: 5);

        Assert.Contains(results, r => r.Id == m.Id);
    }

    [Fact]
    public async Task GlobalSearch_WorksFromEitherWorkspaceContext()
    {
        Memory mA = await _storeA.AddMemoryAsync(WsIdA, MemoryTypes.Note, "kafka outbox pattern alpha");
        Memory mB = await _storeB.AddMemoryAsync(WsIdB, MemoryTypes.Note, "kafka outbox pattern beta");

        // Searching from B should find A's memory (not B's own).
        SearchResult[] fromB = await SearchFromB().SearchGlobalAsync("kafka outbox", k: 10);
        Assert.Contains(fromB, r => r.Id == mA.Id);
        Assert.DoesNotContain(fromB, r => r.Id == mB.Id);

        // Searching from A should find B's memory (not A's own).
        SearchResult[] fromA = await SearchFromA().SearchGlobalAsync("kafka outbox", k: 10);
        Assert.Contains(fromA, r => r.Id == mB.Id);
        Assert.DoesNotContain(fromA, r => r.Id == mA.Id);
    }

    [Fact]
    public async Task GlobalSearch_Result_HasWorkspaceNameField()
    {
        await _storeA.AddMemoryAsync(WsIdA, MemoryTypes.Note, "deployment process alpha workspace");

        SearchResult[] results = await SearchFromB().SearchGlobalAsync("deployment process alpha", k: 5);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(WsNameA, r.WorkspaceName));
    }

    [Fact]
    public async Task CurrentScope_DoesNotReturn_MemoriesFromOtherWorkspaces()
    {
        await _storeA.AddMemoryAsync(WsIdA, MemoryTypes.Note, "secret from workspace alpha only");

        IReadOnlyList<SearchResult> results = await SearchFromB()
            .SearchAsync(WsIdB, "secret from workspace alpha", k: 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GlobalSearch_NoOtherWorkspaces_ReturnsEmptyGracefully()
    {
        // Create a fresh temp home with only one DB inside and search from it.
        string isolatedHome = Path.Combine(Path.GetTempPath(), $"contextos-iso-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedHome);
        string singleDbPath = Path.Combine(isolatedHome, "only.db");

        try
        {
            await using SqliteStore singleStore = await SqliteStore.OpenAsync(singleDbPath);
            await singleStore.UpsertWorkspaceAsync(
                new Workspace("ws-only", "/repos/only", "OnlyWorkspace", null,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

            var search = new HybridSearch(singleStore.Connection, Provider,
                contextosHomeOverride: isolatedHome);

            SearchResult[] results = await search.SearchGlobalAsync("anything at all", k: 5);
            Assert.Empty(results);
        }
        finally
        {
            try { Directory.Delete(isolatedHome, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task GlobalSearch_SkipsCorruptDb_WithoutCrashing()
    {
        // Plant a corrupt .db file alongside real workspace DBs.
        string corruptPath = Path.Combine(_tempHome, "corrupt.db");
        await File.WriteAllBytesAsync(corruptPath,
            System.Text.Encoding.UTF8.GetBytes("this is not a valid sqlite database file"));

        await _storeA.AddMemoryAsync(WsIdA, MemoryTypes.Note, "resilience test memory");

        // Must not throw even though corrupt.db is present.
        SearchResult[] results = await SearchFromB()
            .SearchGlobalAsync("resilience test memory", k: 5);

        // The valid workspace A result should still be found.
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task GlobalSearch_ReturnsResultsFromMultipleWorkspaces()
    {
        await _storeA.AddMemoryAsync(WsIdA, MemoryTypes.Note, "circuit breaker pattern from alpha");
        await _storeB.AddMemoryAsync(WsIdB, MemoryTypes.Note, "circuit breaker pattern from beta");

        // Third workspace
        string dbPathC = Path.Combine(_tempHome, "ws-global-c.db");
        await using SqliteStore storeC = await SqliteStore.OpenAsync(dbPathC);
        await storeC.UpsertWorkspaceAsync(
            new Workspace("ws-global-c", "/repos/gamma", "WorkspaceGamma", null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await storeC.AddMemoryAsync("ws-global-c", MemoryTypes.Note, "circuit breaker pattern from gamma");

        // Search from a fourth (in-memory) context that is not any of the three.
        await using SqliteStore current = await SqliteStore.OpenAsync(":memory:");
        var search = new HybridSearch(current.Connection, Provider, contextosHomeOverride: _tempHome);

        SearchResult[] results = await search.SearchGlobalAsync("circuit breaker pattern", k: 10);

        var workspaceNames = results.Select(r => r.WorkspaceName).Distinct().ToHashSet();
        Assert.True(workspaceNames.Count >= 2,
            $"Expected results from at least 2 workspaces, got: {string.Join(", ", workspaceNames)}");
    }

    // -------------------------------------------------------------------------
    // Recall tool routing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecallTool_WithGlobalScope_CallsSearchGlobalAsync()
    {
        var spy = new SpySearch();
        var tool = new RecallTool(spy, new WorkspaceContext(WsIdB, "/repos/beta"),
            NullLogger<RecallTool>.Instance);

        await tool.RecallAsync("test query", scope: "global");

        Assert.True(spy.GlobalSearchCalled, "scope=global should call SearchGlobalAsync");
        Assert.False(spy.CurrentSearchCalled, "scope=global should not call SearchAsync");
    }

    [Fact]
    public async Task RecallTool_WithCurrentScope_CallsSearchAsync()
    {
        var spy = new SpySearch();
        var tool = new RecallTool(spy, new WorkspaceContext(WsIdB, "/repos/beta"),
            NullLogger<RecallTool>.Instance);

        await tool.RecallAsync("test query", scope: "current");

        Assert.True(spy.CurrentSearchCalled, "scope=current should call SearchAsync");
        Assert.False(spy.GlobalSearchCalled, "scope=current should not call SearchGlobalAsync");
    }

    // -------------------------------------------------------------------------
    // Spy
    // -------------------------------------------------------------------------

    private sealed class SpySearch : ISearch
    {
        public bool GlobalSearchCalled { get; private set; }
        public bool CurrentSearchCalled { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string workspaceId, string query, int k = 5,
            IReadOnlyCollection<string>? types = null, CancellationToken ct = default)
        {
            CurrentSearchCalled = true;
            return Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());
        }

        public Task<SearchResult[]> SearchGlobalAsync(
            string query, int k = 10, CancellationToken ct = default)
        {
            GlobalSearchCalled = true;
            return Task.FromResult(Array.Empty<SearchResult>());
        }
    }
}

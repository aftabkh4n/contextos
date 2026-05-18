using ContextOS.Core;
using ContextOS.Embeddings;
using ContextOS.Retrieval;
using ContextOS.Storage;
using Microsoft.Data.Sqlite;

namespace ContextOS.Tests;

/// <summary>
/// Integration tests for HybridSearch.
/// Requires the ONNX model to be present: run bash scripts/fetch-model.sh once.
/// </summary>
public sealed class RetrievalTests : IAsyncLifetime
{
    private OnnxMiniLmProvider _provider = null!;
    private SqliteStore _store = null!;
    private HybridSearch _search = null!;

    private const string Ws = "ws-retrieval-tests";

    public async Task InitializeAsync()
    {
        _provider = OnnxMiniLmProvider.Create();
        _store = await SqliteStore.OpenAsync(":memory:", _provider);
        _search = new HybridSearch(_store.Connection, _provider);

        await _store.UpsertWorkspaceAsync(
            new Workspace(Ws, "/test/retrieval", "RetrievalTests", null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        _provider.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private Task<Memory> Add(string content, string type = "note", double importance = 0.5, string? tags = null)
        => _store.AddMemoryAsync(Ws, type, content, tags: tags, importance: importance);

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Query "webpack build optimization":
    ///   A — semantic match (esbuild / compilation times) but no keyword overlap.
    ///   B — exact keyword match ("webpack build optimization" in content).
    /// Both should appear in top-5, proving RRF fused the two ranked lists.
    /// </summary>
    [Fact]
    public async Task Search_HybridFusion_ReturnsBothSemanticAndLexicalMatches()
    {
        Memory a = await Add("Replaced babel with esbuild which cut CI compilation times from 45 seconds to 3 seconds");
        Memory b = await Add("webpack build optimization: enable production mode and tree shaking");
        // Noise memories — clearly off-topic.
        await Add("Daily standup is at 10am on Zoom");
        await Add("Pizza order for the team lunch: 3 margheritas");
        await Add("The kitchen tap on the third floor is leaking again");
        await Add("Reminder to renew the parking permit before Friday");
        await Add("Book recommendation: Clean Code by Robert Martin");
        await Add("The office printer runs out of toner every two weeks");
        await Add("Company all-hands is scheduled for next Thursday");
        await Add("Birthday cake for Sarah is chocolate flavour");

        IReadOnlyList<SearchResult> results = await _search.SearchAsync(Ws, "webpack build optimization", k: 5);

        Assert.True(results.Count >= 2, $"Expected at least 2 results, got {results.Count}");
        var ids = results.Select(r => r.Id).ToHashSet();
        Assert.Contains(b.Id, ids); // lexical match via FTS5
        Assert.Contains(a.Id, ids); // semantic match via vec
    }

    /// <summary>
    /// Two memories with identical content inserted at different times.
    /// The newer one should rank higher because exp(-age/30) weights recency.
    /// </summary>
    [Fact]
    public async Task Search_NewerMemoryRanksHigherThanOlderWithSameContent()
    {
        await _store.UpsertWorkspaceAsync(
            new Workspace("ws-age", "/test/age", "AgeTest", null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        // Insert the "old" memory then back-date it with raw SQL.
        Memory old = await _store.AddMemoryAsync("ws-age", "note", "connection pool exhausted under high load");
        long tenDaysAgo = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        using (SqliteCommand cmd = _store.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE memories SET created_at = @t WHERE id = @id";
            cmd.Parameters.AddWithValue("@t", tenDaysAgo);
            cmd.Parameters.AddWithValue("@id", old.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        Memory recent = await _store.AddMemoryAsync("ws-age", "note", "connection pool exhausted under high load");

        var search = new HybridSearch(_store.Connection, _provider);
        IReadOnlyList<SearchResult> results = await search.SearchAsync("ws-age", "connection pool exhausted");

        SearchResult? oldResult    = results.FirstOrDefault(r => r.Id == old.Id);
        SearchResult? recentResult = results.FirstOrDefault(r => r.Id == recent.Id);

        Assert.NotNull(recentResult);
        Assert.NotNull(oldResult);
        Assert.True(recentResult.Score > oldResult.Score,
            $"recent score {recentResult.Score:F6} should exceed old score {oldResult.Score:F6}");
    }

    /// <summary>
    /// Two memories with identical content and the same age.
    /// The one with higher importance (0.9 vs 0.2) should rank higher.
    /// </summary>
    [Fact]
    public async Task Search_HigherImportanceRanksHigherWhenAgeIsEqual()
    {
        await _store.UpsertWorkspaceAsync(
            new Workspace("ws-imp", "/test/imp", "ImportanceTest", null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        Memory low  = await _store.AddMemoryAsync("ws-imp", "note", "SQL query uses a full table scan on orders", importance: 0.2);
        Memory high = await _store.AddMemoryAsync("ws-imp", "note", "SQL query uses a full table scan on orders", importance: 0.9);

        var search = new HybridSearch(_store.Connection, _provider);
        IReadOnlyList<SearchResult> results = await search.SearchAsync("ws-imp", "SQL table scan");

        SearchResult? lowResult  = results.FirstOrDefault(r => r.Id == low.Id);
        SearchResult? highResult = results.FirstOrDefault(r => r.Id == high.Id);

        Assert.NotNull(lowResult);
        Assert.NotNull(highResult);
        Assert.True(highResult.Score > lowResult.Score,
            $"high-importance score {highResult.Score:F6} should exceed low-importance score {lowResult.Score:F6}");
    }

    /// <summary>
    /// Filtering by type should exclude memories of other types.
    /// </summary>
    [Fact]
    public async Task Search_TypeFilter_ReturnsOnlyMatchingTypes()
    {
        await _store.UpsertWorkspaceAsync(
            new Workspace("ws-type", "/test/type", "TypeTest", null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        await _store.AddMemoryAsync("ws-type", MemoryTypes.Note,     "authentication uses JWT RS256 tokens");
        await _store.AddMemoryAsync("ws-type", MemoryTypes.Decision, "authentication uses JWT RS256 tokens");
        await _store.AddMemoryAsync("ws-type", MemoryTypes.Gotcha,   "authentication uses JWT RS256 tokens");

        var search = new HybridSearch(_store.Connection, _provider);
        IReadOnlyList<SearchResult> results = await search.SearchAsync(
            "ws-type", "JWT authentication", types: [MemoryTypes.Decision]);

        Assert.All(results, r => Assert.Equal(MemoryTypes.Decision, r.Type));
        Assert.True(results.Count >= 1);
    }

    /// <summary>
    /// Searching an empty workspace should return an empty list, not throw.
    /// </summary>
    [Fact]
    public async Task Search_EmptyWorkspace_ReturnsEmpty()
    {
        await _store.UpsertWorkspaceAsync(
            new Workspace("ws-empty", "/test/empty", "EmptyTest", null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        var search = new HybridSearch(_store.Connection, _provider);
        IReadOnlyList<SearchResult> results = await search.SearchAsync("ws-empty", "anything at all");

        Assert.Empty(results);
    }
}

using ContextOS.Core;
using ContextOS.Storage;

namespace ContextOS.Tests;

/// <summary>Minimal embeddings provider that always returns a fixed-dimension zero vector.</summary>
file sealed class FixedDimProvider(int dimension) : IEmbeddingsProvider
{
    public string Name => "fixed";
    public int Dimension => dimension;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(new float[dimension]);

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult(texts.Select(_ => new float[dimension]).ToArray());
}

public sealed class StorageTests : IAsyncLifetime
{
    private SqliteStore? _store;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_store is not null)
            await _store.DisposeAsync();
    }

    private async Task<SqliteStore> Store()
    {
        _store ??= await SqliteStore.OpenAsync(":memory:");
        return _store;
    }

    private static Workspace TestWorkspace(string id, string name) =>
        new(id, $"/repos/{name}", name, null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    [Fact]
    public async Task Upsert_and_get_workspace_roundtrips_all_fields()
    {
        SqliteStore store = await Store();
        var ws = new Workspace("ws-01", "/repos/alpha", "alpha", "https://github.com/x/y",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await store.UpsertWorkspaceAsync(ws);

        Workspace? fetched = await store.GetWorkspaceAsync("ws-01");
        Assert.NotNull(fetched);
        Assert.Equal("alpha", fetched.Name);
        Assert.Equal("/repos/alpha", fetched.RootPath);
        Assert.Equal("https://github.com/x/y", fetched.RepoUrl);
    }

    [Fact]
    public async Task Get_workspace_returns_null_for_missing_id()
    {
        SqliteStore store = await Store();
        Assert.Null(await store.GetWorkspaceAsync("does-not-exist"));
    }

    [Fact]
    public async Task Add_five_memories_and_list_all()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws-list", "list"));

        for (int i = 0; i < 5; i++)
            await store.AddMemoryAsync("ws-list", MemoryTypes.Note, $"Memory {i}");

        IReadOnlyList<Memory> memories = await store.ListMemoriesAsync("ws-list");
        Assert.Equal(5, memories.Count);
    }

    [Fact]
    public async Task List_memories_filtered_by_type_returns_only_matching_rows()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws-filter", "filter"));

        await store.AddMemoryAsync("ws-filter", MemoryTypes.Note, "note one");
        await store.AddMemoryAsync("ws-filter", MemoryTypes.Decision, "a decision");
        await store.AddMemoryAsync("ws-filter", MemoryTypes.Todo, "a todo");
        await store.AddMemoryAsync("ws-filter", MemoryTypes.Note, "note two");
        await store.AddMemoryAsync("ws-filter", MemoryTypes.Gotcha, "a gotcha");

        IReadOnlyList<Memory> notes =
            await store.ListMemoriesAsync("ws-filter", new MemoryFilter(Type: MemoryTypes.Note));

        Assert.Equal(2, notes.Count);
        Assert.All(notes, m => Assert.Equal(MemoryTypes.Note, m.Type));
    }

    [Fact]
    public async Task Delete_memory_reduces_count_and_removes_the_right_row()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws-del", "del"));

        var added = new List<Memory>();
        for (int i = 0; i < 5; i++)
            added.Add(await store.AddMemoryAsync("ws-del", MemoryTypes.Note, $"Memory {i}"));

        bool deleted = await store.DeleteMemoryAsync(added[2].Id);

        Assert.True(deleted);
        IReadOnlyList<Memory> remaining = await store.ListMemoriesAsync("ws-del");
        Assert.Equal(4, remaining.Count);
        Assert.DoesNotContain(remaining, m => m.Id == added[2].Id);
    }

    [Fact]
    public async Task Added_memory_is_retrievable_by_id()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws-byid", "byid"));

        Memory created = await store.AddMemoryAsync("ws-byid", MemoryTypes.Decision,
            "use sqlite", tags: "infra,db", importance: 0.9);

        Memory? fetched = await store.GetMemoryByIdAsync(created.Id);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(MemoryTypes.Decision, fetched.Type);
        Assert.Equal("use sqlite", fetched.Content);
        Assert.Equal("infra,db", fetched.Tags);
        Assert.Equal(0.9, fetched.Importance);
    }

    [Fact]
    public async Task DimensionMismatch_ThrowsOnOpen()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"ctos-dim-test-{Guid.NewGuid():N}.db");
        try
        {
            // Create DB and insert a memory with dimension 4.
            var provider4 = new FixedDimProvider(4);
            await using SqliteStore store4 = await SqliteStore.OpenAsync(dbPath, provider4);
            await store4.UpsertWorkspaceAsync(TestWorkspace("ws-dim", "dim"));
            await store4.AddMemoryAsync("ws-dim", MemoryTypes.Note, "a note");

            // Re-open with dimension 8 — must fail.
            var provider8 = new FixedDimProvider(8);
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqliteStore.OpenAsync(dbPath, provider8));

            Assert.Contains("dimension 4", ex.Message);
            Assert.Contains("dimension 8", ex.Message);
            Assert.Contains("reindex from scratch", ex.Message);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Ulid_ids_are_unique_and_26_chars()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws-ulid", "ulid"));

        var ids = new HashSet<string>();
        for (int i = 0; i < 20; i++)
        {
            Memory m = await store.AddMemoryAsync("ws-ulid", MemoryTypes.Note, $"m{i}");
            Assert.Equal(26, m.Id.Length);
            Assert.True(ids.Add(m.Id), $"Duplicate ULID generated: {m.Id}");
        }
    }
}

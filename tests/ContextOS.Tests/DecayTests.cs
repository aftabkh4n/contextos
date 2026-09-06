using ContextOS.Core;
using ContextOS.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextOS.Tests;

public sealed class DecayTests : IAsyncDisposable
{
    private SqliteStore? _store;

    public async ValueTask DisposeAsync()
    {
        if (_store is not null)
            await _store.DisposeAsync();
    }

    private async Task<SqliteStore> Store()
    {
        _store ??= await SqliteStore.OpenAsync(":memory:");
        return _store;
    }

    private static Workspace TestWorkspace(string id) =>
        new(id, $"/repos/{id}", id, null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static DecayService MakeDecayService(int globalDecayDays = 90) =>
        new(new MemoryConfig(globalDecayDays));

    private static ILogger Logger => NullLogger.Instance;

    // Helper: set created_at to a relative number of days in the past.
    private static async Task SetCreatedAt(SqliteConnection conn, string id, int daysAgo)
    {
        long ts = DateTimeOffset.UtcNow.AddDays(-daysAgo).ToUnixTimeMilliseconds();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE memories SET created_at = @v WHERE id = @id";
        cmd.Parameters.AddWithValue("@v", ts);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // Helper: set last_recalled_at to a relative number of days in the past.
    private static async Task SetLastRecalledAt(SqliteConnection conn, string id, int daysAgo)
    {
        string ts = DateTimeOffset.UtcNow.AddDays(-daysAgo).ToString("O");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE memories SET last_recalled_at = @v WHERE id = @id";
        cmd.Parameters.AddWithValue("@v", ts);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // Helper: set per-row decay_days.
    private static async Task SetDecayDays(SqliteConnection conn, string id, int decayDays)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE memories SET decay_days = @v WHERE id = @id";
        cmd.Parameters.AddWithValue("@v", decayDays);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Memory_recalled_yesterday_is_not_archived()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws1"));
        Memory m = await store.AddMemoryAsync("ws1", MemoryTypes.Note, "recent recall");
        await SetCreatedAt(store.Connection, m.Id, 100);
        await SetLastRecalledAt(store.Connection, m.Id, 1);

        await MakeDecayService().RunDecayPassAsync(store, Logger);

        Memory? after = await store.GetMemoryByIdAsync(m.Id);
        Assert.NotNull(after);
        Assert.Null(after.ArchivedAt);
    }

    [Fact]
    public async Task Memory_never_recalled_created_91_days_ago_with_decay_90_is_archived()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws2"));
        Memory m = await store.AddMemoryAsync("ws2", MemoryTypes.Note, "old never recalled");
        await SetCreatedAt(store.Connection, m.Id, 91); // 91 > 90 days, last_recalled_at remains null

        await MakeDecayService(globalDecayDays: 90).RunDecayPassAsync(store, Logger);

        Memory? after = await store.GetMemoryByIdAsync(m.Id);
        Assert.NotNull(after);
        Assert.NotNull(after.ArchivedAt);
    }

    [Fact]
    public async Task Memory_created_200_days_ago_recalled_5_days_ago_is_not_archived()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws3"));
        Memory m = await store.AddMemoryAsync("ws3", MemoryTypes.Note, "old but recalled recently");
        await SetCreatedAt(store.Connection, m.Id, 200);
        await SetLastRecalledAt(store.Connection, m.Id, 5);

        await MakeDecayService().RunDecayPassAsync(store, Logger);

        Memory? after = await store.GetMemoryByIdAsync(m.Id);
        Assert.NotNull(after);
        Assert.Null(after.ArchivedAt);
    }

    [Fact]
    public async Task Global_decay_disabled_skips_pass_and_archives_nothing()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws4"));
        Memory m = await store.AddMemoryAsync("ws4", MemoryTypes.Note, "very old memory");
        await SetCreatedAt(store.Connection, m.Id, 500);

        await MakeDecayService(globalDecayDays: 0).RunDecayPassAsync(store, Logger);

        Memory? after = await store.GetMemoryByIdAsync(m.Id);
        Assert.NotNull(after);
        Assert.Null(after.ArchivedAt);
    }

    [Fact]
    public async Task Memory_with_row_decay_days_zero_is_not_archived_even_when_global_decay_enabled()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws5"));
        Memory m = await store.AddMemoryAsync("ws5", MemoryTypes.Note, "pinned memory");
        await SetCreatedAt(store.Connection, m.Id, 500);
        await SetDecayDays(store.Connection, m.Id, 0);

        await MakeDecayService().RunDecayPassAsync(store, Logger);

        Memory? after = await store.GetMemoryByIdAsync(m.Id);
        Assert.NotNull(after);
        Assert.Null(after.ArchivedAt);
    }

    [Fact]
    public async Task Last_recalled_at_is_updated_after_list_returns_a_memory()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws6"));
        Memory m = await store.AddMemoryAsync("ws6", MemoryTypes.Note, "something");

        Memory? before = await store.GetMemoryByIdAsync(m.Id);
        Assert.NotNull(before);
        Assert.Null(before.LastRecalledAt);

        await store.ListMemoriesAsync("ws6");

        Memory? after = await store.GetMemoryByIdAsync(m.Id);
        Assert.NotNull(after);
        Assert.NotNull(after.LastRecalledAt);
    }

    [Fact]
    public async Task Batch_of_five_memories_recalled_all_have_last_recalled_at_set()
    {
        SqliteStore store = await Store();
        await store.UpsertWorkspaceAsync(TestWorkspace("ws7"));

        var ids = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            Memory m = await store.AddMemoryAsync("ws7", MemoryTypes.Note, $"memory {i}");
            ids.Add(m.Id);
        }

        await store.ListMemoriesAsync("ws7");

        foreach (string id in ids)
        {
            Memory? m = await store.GetMemoryByIdAsync(id);
            Assert.NotNull(m);
            Assert.NotNull(m.LastRecalledAt);
        }
    }
}

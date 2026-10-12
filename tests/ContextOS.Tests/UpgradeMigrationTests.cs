using ContextOS.Core;
using ContextOS.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextOS.Tests;

/// <summary>
/// Verifies the v0.2.0 → v0.2.1 upgrade does not archive legitimate memories
/// (the data-loss bug) and that already-archived rows from the broken decay
/// pass are restored.
/// </summary>
public sealed class UpgradeMigrationTests
{
    private const string Workspace = "upgrade-ws";

    /// <summary>
    /// Memory stored under the pre-v0.2.0 schema 150 days ago must not be
    /// archived on the first startup after upgrade. V3 now backfills
    /// last_recalled_at, so the decay clock restarts from upgrade time.
    /// </summary>
    [Fact]
    public async Task PreV020_old_memory_is_not_archived_after_upgrade()
    {
        string dbPath = TempDbPath();
        try
        {
            // Build a database at user_version=3 (the pre-v0.2.0 schema).
            await InstallPreV020SchemaAsync(dbPath);
            await InsertMemoryAtVersion3Async(dbPath, Workspace,
                content: "an old pre-v0.2.0 memory", daysAgo: 150);

            // Open with the current SqliteStore. V3 backfill + V4 repair run.
            await using SqliteStore store = await SqliteStore.OpenAsync(dbPath);

            // Run the decay pass. The memory is 150 days old but just got a
            // fresh last_recalled_at, so it must stay active.
            var decay = new DecayService(new MemoryConfig(DecayDays: 90));
            await decay.RunDecayPassAsync(store, NullLogger.Instance);

            IReadOnlyList<Memory> all = await store.ListMemoriesAsync(Workspace,
                new MemoryFilter(IncludeArchived: true, Limit: 10));
            Assert.Single(all);
            Assert.Null(all[0].ArchivedAt);
            Assert.NotNull(all[0].LastRecalledAt);
        }
        finally { TryDelete(dbPath); }
    }

    /// <summary>
    /// A database already at V3 containing a row the broken decay pass archived
    /// (archived_at set, last_recalled_at NULL) must be restored by V4 on open.
    /// </summary>
    [Fact]
    public async Task V3_archived_memory_with_null_last_recalled_at_is_restored()
    {
        string dbPath = TempDbPath();
        try
        {
            // Build at user_version=4 (post-broken-V3) with an archived row.
            await InstallBrokenV3SchemaAsync(dbPath);
            string id = await InsertArchivedMemoryAtVersion4Async(dbPath, Workspace,
                content: "wrongly archived by v0.2.0 decay");

            await using SqliteStore store = await SqliteStore.OpenAsync(dbPath);

            Memory? restored = await store.GetMemoryByIdAsync(id);
            Assert.NotNull(restored);
            Assert.Null(restored.ArchivedAt);
            Assert.NotNull(restored.LastRecalledAt);
        }
        finally { TryDelete(dbPath); }
    }

    // -------------------------------------------------------------------------
    // Helpers: construct historical schemas directly, bypassing the current
    // migration pipeline, so the "upgrade" path is actually exercised.
    // -------------------------------------------------------------------------

    private static async Task InstallPreV020SchemaAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        foreach (string sql in Migrations.V1) await ExecAsync(conn, sql);
        foreach (string sql in Migrations.V2) await ExecAsync(conn, sql);
        await ExecAsync(conn, "PRAGMA user_version = 3");
    }

    private static async Task InstallBrokenV3SchemaAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        foreach (string sql in Migrations.V1) await ExecAsync(conn, sql);
        foreach (string sql in Migrations.V2) await ExecAsync(conn, sql);
        // Replay the broken V3: only ADD COLUMN, no backfill.
        await ExecAsync(conn, "ALTER TABLE memories ADD COLUMN last_recalled_at TEXT");
        await ExecAsync(conn, "ALTER TABLE memories ADD COLUMN decay_days INTEGER NOT NULL DEFAULT 90");
        await ExecAsync(conn, "PRAGMA user_version = 4");
    }

    private static async Task InsertMemoryAtVersion3Async(
        string dbPath, string workspaceId, string content, int daysAgo)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        await ExecAsync(conn,
            "INSERT INTO workspaces (id, root_path, name, repo_url, created_at) " +
            "VALUES (@id, @root, @name, NULL, @created)",
            ("@id", workspaceId), ("@root", $"/tmp/{workspaceId}"), ("@name", workspaceId),
            ("@created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        long created = DateTimeOffset.UtcNow.AddDays(-daysAgo).ToUnixTimeMilliseconds();
        await ExecAsync(conn,
            "INSERT INTO memories (id, workspace_id, type, content, importance, created_at) " +
            "VALUES (@id, @w, 'note', @c, 0.5, @t)",
            ("@id", UlidHelper.NewUlid()), ("@w", workspaceId), ("@c", content), ("@t", created));
    }

    private static async Task<string> InsertArchivedMemoryAtVersion4Async(
        string dbPath, string workspaceId, string content)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        await ExecAsync(conn,
            "INSERT INTO workspaces (id, root_path, name, repo_url, created_at) " +
            "VALUES (@id, @root, @name, NULL, @created)",
            ("@id", workspaceId), ("@root", $"/tmp/{workspaceId}"), ("@name", workspaceId),
            ("@created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        string id = UlidHelper.NewUlid();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecAsync(conn,
            "INSERT INTO memories (id, workspace_id, type, content, importance, created_at, archived_at, last_recalled_at, decay_days) " +
            "VALUES (@id, @w, 'note', @c, 0.5, @t, @t, NULL, 90)",
            ("@id", id), ("@w", workspaceId), ("@c", content), ("@t", now));
        return id;
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql,
        params (string name, object value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach ((string n, object v) in parameters) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"contextos-upgrade-{Guid.NewGuid():N}.db");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

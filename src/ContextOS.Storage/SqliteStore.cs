using ContextOS.Core;
using Microsoft.Data.Sqlite;

namespace ContextOS.Storage;

/// <summary>
/// Persists and retrieves memories for a single workspace database.
/// One instance owns one SQLite connection; use <see cref="OpenAsync"/> to create.
/// </summary>
public sealed class SqliteStore : IMemoryStore, IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly IEmbeddingsProvider? _embeddings;

    /// <summary>The underlying connection, shared with HybridSearch so both use one SQLite handle.</summary>
    public SqliteConnection Connection => _conn;

    private SqliteStore(SqliteConnection conn, IEmbeddingsProvider? embeddings)
    {
        _conn = conn;
        _embeddings = embeddings;
    }

    /// <summary>Opens (or creates) the database at <paramref name="dbPath"/> and runs pending migrations.</summary>
    public static async Task<SqliteStore> OpenAsync(
        string dbPath,
        IEmbeddingsProvider? embeddings = null,
        CancellationToken ct = default)
    {
        if (!string.Equals(dbPath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (dir is not null)
                Directory.CreateDirectory(dir);
        }

        var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);

        var store = new SqliteStore(conn, embeddings);
        await store.InitAsync(ct);
        return store;
    }

    private async Task InitAsync(CancellationToken ct)
    {
        // WAL gives better read/write concurrency; foreign_keys enforces referential integrity.
        await RunAsync("PRAGMA journal_mode=WAL", ct);
        await RunAsync("PRAGMA foreign_keys=ON", ct);
        await MigrateAsync(ct);
    }

    private async Task MigrateAsync(CancellationToken ct)
    {
        int version = await UserVersionAsync(ct);
        if (version < 1)
            await ApplyAsync(Migrations.V1, 1, ct);
        // Version slot 2 was the sqlite-vec memories_vec table, dropped before v1.
        // Jump directly to 3 so databases that were at version 2 during development
        // still receive the embedding BLOB migration.
        if (version < 3)
            await ApplyAsync(Migrations.V2, 3, ct);
    }

    private async Task<int> UserVersionAsync(CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        object? result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private async Task ApplyAsync(string[] statements, int targetVersion, CancellationToken ct)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            foreach (string sql in statements)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // PRAGMA user_version participates in the transaction, so a rollback reverts it.
            using var verCmd = _conn.CreateCommand();
            verCmd.Transaction = tx;
            verCmd.CommandText = $"PRAGMA user_version = {targetVersion}";
            await verCmd.ExecuteNonQueryAsync(ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Memories
    // -------------------------------------------------------------------------

    /// <summary>Inserts a new memory and returns it with its generated ID and timestamp.</summary>
    public async Task<Memory> AddMemoryAsync(
        string workspaceId,
        string type,
        string content,
        string? source = null,
        string? tags = null,
        double importance = 0.5,
        CancellationToken ct = default)
    {
        string id = UlidHelper.NewUlid();
        long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Compute embedding before the INSERT so we can store it in one round-trip.
        float[]? embedding = _embeddings is not null
            ? await _embeddings.EmbedAsync(content, ct)
            : null;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memories (id, workspace_id, type, content, source, tags, importance, created_at, embedding)
            VALUES (@id, @workspaceId, @type, @content, @source, @tags, @importance, @createdAt, @embedding)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@source", (object?)source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", (object?)tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@importance", importance);
        cmd.Parameters.AddWithValue("@createdAt", createdAt);
        cmd.Parameters.AddWithValue("@embedding", embedding is not null ? (object)EmbeddingToBlob(embedding) : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        return new Memory(id, workspaceId, type, content, source, tags, importance, createdAt, null);
    }

    /// <summary>Returns the memory with <paramref name="id"/>, or null if not found.</summary>
    public async Task<Memory?> GetMemoryByIdAsync(string id, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, type, content, source, tags, importance, created_at, archived_at
            FROM memories WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMemory(reader) : null;
    }

    /// <summary>Lists memories for a workspace, newest first, with optional filters.</summary>
    public async Task<IReadOnlyList<Memory>> ListMemoriesAsync(
        string workspaceId,
        MemoryFilter? filter = null,
        CancellationToken ct = default)
    {
        filter ??= new MemoryFilter();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace_id, type, content, source, tags, importance, created_at, archived_at
            FROM memories
            WHERE workspace_id = @workspaceId
              AND (@type IS NULL OR type = @type)
              AND (@includeArchived = 1 OR archived_at IS NULL)
            ORDER BY created_at DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@type", (object?)filter.Type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@includeArchived", filter.IncludeArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("@limit", filter.Limit);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<Memory>();
        while (await reader.ReadAsync(ct))
            rows.Add(ReadMemory(reader));
        return rows;
    }

    /// <summary>Deletes the memory with <paramref name="id"/>. Returns true if a row was removed.</summary>
    public async Task<bool> DeleteMemoryAsync(string id, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM memories WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // -------------------------------------------------------------------------
    // Workspaces
    // -------------------------------------------------------------------------

    /// <summary>Inserts or updates the workspace record.</summary>
    public async Task UpsertWorkspaceAsync(Workspace workspace, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO workspaces (id, root_path, name, repo_url, created_at)
            VALUES (@id, @rootPath, @name, @repoUrl, @createdAt)
            ON CONFLICT(id) DO UPDATE SET
              root_path = excluded.root_path,
              name      = excluded.name,
              repo_url  = excluded.repo_url
            """;
        cmd.Parameters.AddWithValue("@id", workspace.Id);
        cmd.Parameters.AddWithValue("@rootPath", workspace.RootPath);
        cmd.Parameters.AddWithValue("@name", workspace.Name);
        cmd.Parameters.AddWithValue("@repoUrl", (object?)workspace.RepoUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", workspace.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Returns the workspace with <paramref name="id"/>, or null if not found.</summary>
    public async Task<Workspace?> GetWorkspaceAsync(string id, CancellationToken ct = default)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, root_path, name, repo_url, created_at
            FROM workspaces WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new Workspace(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt64(4));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static byte[] EmbeddingToBlob(float[] v)
    {
        byte[] b = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, b, 0, b.Length);
        return b;
    }

    private static Memory ReadMemory(SqliteDataReader r) =>
        new(r.GetString(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetDouble(6),
            r.GetInt64(7),
            r.IsDBNull(8) ? null : r.GetInt64(8));

    private async Task RunAsync(string sql, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        _conn.Dispose();
        return ValueTask.CompletedTask;
    }
}

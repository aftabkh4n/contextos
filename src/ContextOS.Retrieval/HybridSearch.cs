using ContextOS.Core;
using Microsoft.Data.Sqlite;

namespace ContextOS.Retrieval;

/// <summary>
/// Hybrid retrieval: cosine similarity scan over embedding BLOBs merged with
/// FTS5 BM25 via Reciprocal Rank Fusion, then reranked by recency and importance.
///
/// Vector search strategy: full scan of memories.embedding with managed cosine
/// similarity. This is the v1 strategy. When a workspace exceeds ~50k memories,
/// revisit with sqlite-vec KNN or an HNSW index.
/// </summary>
public sealed class HybridSearch : ISearch
{
    private readonly SqliteConnection _conn;
    private readonly IEmbeddingsProvider _embeddings;

    /// <param name="conn">Open connection — pass <c>SqliteStore.Connection</c>.</param>
    /// <param name="embeddings">Provider used to embed the query at search time.</param>
    public HybridSearch(SqliteConnection conn, IEmbeddingsProvider embeddings)
    {
        _conn = conn;
        _embeddings = embeddings;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string workspaceId,
        string query,
        int k = 5,
        IReadOnlyCollection<string>? types = null,
        CancellationToken ct = default)
    {
        float[] queryVec = await _embeddings.EmbedAsync(query, ct);

        List<long> vecRowids = await VectorSearchAsync(workspaceId, queryVec, 20, ct);
        List<long> ftsRowids = await FtsSearchAsync(workspaceId, query, 20, ct);

        Dictionary<long, double> rrfScores = Rrf(vecRowids, ftsRowids);
        if (rrfScores.Count == 0)
            return [];

        var rows = await FetchMemoriesAsync(workspaceId, rrfScores.Keys, types, ct);
        if (rows.Count == 0)
            return [];

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var results = new List<SearchResult>(rows.Count);

        foreach ((long rowid, Memory m) in rows)
        {
            if (!rrfScores.TryGetValue(rowid, out double rrf)) continue;
            double ageDays = (nowMs - m.CreatedAt) / 86_400_000.0;
            double score = rrf * Math.Exp(-ageDays / 30.0) * (0.5 + m.Importance);
            string[] tags = m.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            results.Add(new SearchResult(m.Id, m.Type, m.Content, tags, m.CreatedAt, score));
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results.Count <= k ? results : results.Take(k).ToList();
    }

    // -------------------------------------------------------------------------
    // Vector search (managed cosine scan over embedding BLOBs)
    // -------------------------------------------------------------------------

    private async Task<List<long>> VectorSearchAsync(
        string workspaceId, float[] queryVec, int limit, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT rowid, embedding FROM memories
            WHERE workspace_id = @workspaceId
              AND archived_at IS NULL
              AND embedding IS NOT NULL
            """;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);

        var candidates = new List<(long rowid, double sim)>();
        using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            long rowid = r.GetInt64(0);
            float[] vec = FromBlob((byte[])r.GetValue(1));
            candidates.Add((rowid, Dot(queryVec, vec))); // vectors are L2-normalised → dot = cosine
        }

        candidates.Sort((a, b) => b.sim.CompareTo(a.sim));
        return candidates.Take(limit).Select(c => c.rowid).ToList();
    }

    // -------------------------------------------------------------------------
    // FTS5 search
    // -------------------------------------------------------------------------

    private async Task<List<long>> FtsSearchAsync(
        string workspaceId, string query, int limit, CancellationToken ct)
    {
        string ftsQuery = BuildFtsQuery(query);
        if (ftsQuery.Length == 0)
            return [];

        using var cmd = _conn.CreateCommand();
        // Inner subquery limits the FTS scan to this workspace's non-archived rows.
        cmd.CommandText = $"""
            SELECT rowid FROM memories_fts
            WHERE memories_fts MATCH @query
              AND rowid IN (
                SELECT rowid FROM memories
                WHERE workspace_id = @workspaceId AND archived_at IS NULL
              )
            ORDER BY rank
            LIMIT {limit}
            """;
        cmd.Parameters.AddWithValue("@query", ftsQuery);
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);

        var rowids = new List<long>(limit);
        using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rowids.Add(r.GetInt64(0));
        return rowids;
    }

    /// <summary>
    /// Quotes each whitespace-separated token so FTS5 treats them as literals
    /// (avoids misinterpretation of AND/OR/NOT and special chars).
    /// </summary>
    private static string BuildFtsQuery(string query)
    {
        IEnumerable<string> tokens = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => $"\"{t.Replace("\"", "")}\"");
        return string.Join(" ", tokens);
    }

    // -------------------------------------------------------------------------
    // RRF merge
    // -------------------------------------------------------------------------

    private static Dictionary<long, double> Rrf(List<long> vec, List<long> fts, int k = 60)
    {
        var scores = new Dictionary<long, double>();
        for (int i = 0; i < vec.Count; i++)
            scores[vec[i]] = scores.GetValueOrDefault(vec[i]) + 1.0 / (k + i + 1);
        for (int i = 0; i < fts.Count; i++)
            scores[fts[i]] = scores.GetValueOrDefault(fts[i]) + 1.0 / (k + i + 1);
        return scores;
    }

    // -------------------------------------------------------------------------
    // Fetch memories for final reranking
    // -------------------------------------------------------------------------

    private async Task<List<(long rowid, Memory memory)>> FetchMemoriesAsync(
        string workspaceId,
        IEnumerable<long> rowids,
        IReadOnlyCollection<string>? types,
        CancellationToken ct)
    {
        List<long> ids = rowids.ToList();
        if (ids.Count == 0) return [];

        // Build inline parameter lists — SQLite has no array bind type.
        string rowPlaceholders = string.Join(",", ids.Select((_, i) => $"@r{i}"));
        string typesClause = types is { Count: > 0 }
            ? $"AND type IN ({string.Join(",", Enumerable.Range(0, types.Count).Select(i => $"@t{i}"))})"
            : string.Empty;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT rowid, id, workspace_id, type, content, source, tags, importance, created_at, archived_at
            FROM memories
            WHERE rowid IN ({rowPlaceholders})
              AND workspace_id = @workspaceId
              AND archived_at IS NULL
              {typesClause}
            """;

        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"@r{i}", ids[i]);
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        if (types is { Count: > 0 })
        {
            int i = 0;
            foreach (string t in types)
                cmd.Parameters.AddWithValue($"@t{i++}", t);
        }

        var results = new List<(long, Memory)>(ids.Count);
        using SqliteDataReader r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            long rowid = r.GetInt64(0);
            var m = new Memory(
                r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetDouble(7), r.GetInt64(8),
                r.IsDBNull(9) ? null : r.GetInt64(9));
            results.Add((rowid, m));
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static float[] FromBlob(byte[] b)
    {
        float[] v = new float[b.Length / sizeof(float)];
        Buffer.BlockCopy(b, 0, v, 0, b.Length);
        return v;
    }

    private static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++)
            s += a[i] * b[i];
        return s;
    }
}

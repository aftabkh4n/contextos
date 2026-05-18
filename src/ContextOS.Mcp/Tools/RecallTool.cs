using System.ComponentModel;
using System.Text.Json;
using ContextOS.Core;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class RecallTool(ISearch search, WorkspaceContext ws)
{
    [McpServerTool(Name = "recall")]
    [Description("Search stored memories by semantic and keyword similarity.")]
    public async Task<string> RecallAsync(
        [Description("Search query.")] string query,
        [Description("Number of results to return (1-20).")] int k = 5,
        [Description("Filter by memory type.")] string[]? types = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("query must not be empty.");
        if (k < 1 || k > 20)
            throw new ArgumentException($"k must be between 1 and 20. Got: {k}");

        IReadOnlyList<SearchResult> results = await search.SearchAsync(
            ws.WorkspaceId, query, k: k, types: types, ct: ct);

        var items = results.Select(r => new
        {
            id = r.Id,
            type = r.Type,
            content = r.Content,
            tags = r.Tags,
            created_at = r.CreatedAt,
            score = r.Score,
        });

        return JsonSerializer.Serialize(items);
    }
}

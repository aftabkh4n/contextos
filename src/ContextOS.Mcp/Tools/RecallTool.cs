using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextOS.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class RecallTool(ISearch search, WorkspaceContext ws, ILogger<RecallTool> logger)
{
    [McpServerTool(Name = "recall")]
    [Description("Search stored memories by semantic and keyword similarity. Use scope='global' to search across all workspaces.")]
    public async Task<string> RecallAsync(
        [Description("Search query.")] string query,
        [Description("Number of results to return (1-20).")] int k = 5,
        [Description("Filter by memory type.")] string[]? types = null,
        [Description("Search scope: 'current' (default) or 'global'.")] string scope = "current",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("query must not be empty.");
        if (k < 1 || k > 20)
            throw new ArgumentException($"k must be between 1 and 20. Got: {k}");
        if (scope is not ("current" or "global"))
            throw new ArgumentException($"scope must be 'current' or 'global'. Got: {scope}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (scope == "global")
            {
                SearchResult[] results = await search.SearchGlobalAsync(query, k: k, ct: ct);

                logger.LogInformation(
                    "recall(global): query_hash={Hash} k={K} found={Count} elapsed={ElapsedMs}ms",
                    ContentFingerprint(query), k, results.Length, sw.ElapsedMilliseconds);

                var items = results.Select(r => new
                {
                    id = r.Id,
                    type = r.Type,
                    content = r.Content,
                    tags = r.Tags,
                    created_at = r.CreatedAt,
                    score = r.Score,
                    workspace = r.WorkspaceName,
                });
                return JsonSerializer.Serialize(items);
            }
            else
            {
                IReadOnlyList<SearchResult> results = await search.SearchAsync(
                    ws.WorkspaceId, query, k: k, types: types, ct: ct);

                logger.LogInformation(
                    "recall: query_hash={Hash} k={K} found={Count} elapsed={ElapsedMs}ms",
                    ContentFingerprint(query), k, results.Count, sw.ElapsedMilliseconds);

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
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "recall: failed workspace={WorkspaceId} scope={Scope} query_hash={Hash}",
                ws.WorkspaceId, scope, ContentFingerprint(query));
            throw new InvalidOperationException("Search failed. Check the server logs for details.");
        }
    }

    private static string ContentFingerprint(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..8].ToLowerInvariant();
}

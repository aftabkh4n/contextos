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

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
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
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "recall: failed workspace={WorkspaceId} query_hash={Hash}",
                ws.WorkspaceId, ContentFingerprint(query));
            throw new InvalidOperationException("Search failed. Check the server logs for details.");
        }
    }

    private static string ContentFingerprint(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..8].ToLowerInvariant();
}

using System.ComponentModel;
using ContextOS.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class ContextTool(IMemoryStore store, WorkspaceContext ws, ILogger<ContextTool> logger)
{
    [McpServerTool(Name = "context")]
    [Description("Read-only. Returns a Markdown summary of this workspace's memories. Use it when you need more than the summary injected at session start.")]
    public async Task<string> ContextAsync(
        [Description("current (default): recent decisions, todos, skills and git state. week: memories from the last 7 days. all: every stored memory.")] string scope = "current",
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string workspaceName = Path.GetFileName(
                ws.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                ?? ws.WorkspaceId;

            string result = await ContextBuilder.BuildAsync(
                store, ws.WorkspaceId, workspaceName, scope, ws.GitInfo, ct);

            logger.LogInformation(
                "context: scope={Scope} elapsed={ElapsedMs}ms",
                scope, sw.ElapsedMilliseconds);

            return result;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "context: failed workspace={WorkspaceId} scope={Scope}",
                ws.WorkspaceId, scope);
            throw new InvalidOperationException("Failed to build context. Check the server logs for details.");
        }
    }
}

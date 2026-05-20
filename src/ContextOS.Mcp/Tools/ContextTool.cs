using System.ComponentModel;
using ContextOS.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class ContextTool(IMemoryStore store, WorkspaceContext ws, ILogger<ContextTool> logger)
{
    [McpServerTool(Name = "context")]
    [Description("Assemble the current engineering context for this workspace.")]
    public async Task<string> ContextAsync(
        [Description("Scope: current, week, or all.")] string scope = "current",
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

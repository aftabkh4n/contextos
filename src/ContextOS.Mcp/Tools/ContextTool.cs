using System.ComponentModel;
using ContextOS.Core;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class ContextTool(IMemoryStore store, WorkspaceContext ws)
{
    [McpServerTool(Name = "context")]
    [Description("Assemble the current engineering context for this workspace.")]
    public Task<string> ContextAsync(
        [Description("Scope: current, week, or all.")] string scope = "current",
        CancellationToken ct = default)
    {
        string workspaceName = Path.GetFileName(
            ws.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? ws.WorkspaceId;

        return ContextBuilder.BuildAsync(store, ws.WorkspaceId, workspaceName, scope, ws.GitInfo, ct);
    }
}

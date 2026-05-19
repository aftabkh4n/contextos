using System.Security.Cryptography;
using System.Text;

namespace ContextOS.Core;

/// <summary>
/// Assembles the instructions blob injected into the MCP initialize response.
/// Wraps <see cref="ContextBuilder"/> and adds the framing prefix and empty-workspace handling.
/// </summary>
public static class HydrationBuilder
{
    /// <summary>
    /// Returned when there are no memories and no git info for a workspace.
    /// Tells the agent where it is without wasting tokens on empty sections.
    /// </summary>
    public const string EmptyWorkspaceMessage =
        "ContextOS connected. No memory yet for this workspace. " +
        "Use the remember tool to store decisions, todos, and notes.";

    private const string Framing =
        "The following is the persistent engineering context for the current workspace, " +
        "automatically loaded by ContextOS. Treat it as background knowledge the user " +
        "expects you to already have.\n\n";

    private const int MaxTotalBytes = 2048;

    /// <summary>
    /// Builds the instructions string for the MCP initialize response.
    /// Returns <see cref="EmptyWorkspaceMessage"/> when there are no memories and no git info.
    /// The returned string is always at or under 2 KB in UTF-8.
    /// </summary>
    public static async Task<string> BuildAsync(
        IMemoryStore store,
        string workspaceId,
        string workspaceName,
        GitInfo? gitInfo,
        CancellationToken ct = default)
    {
        IReadOnlyList<Memory> probe = await store.ListMemoriesAsync(
            workspaceId, new MemoryFilter(Limit: 1), ct);

        bool hasGit = gitInfo is not null;
        bool hasMemories = probe.Count > 0;

        if (!hasGit && !hasMemories)
            return EmptyWorkspaceMessage;

        int framingBytes = Encoding.UTF8.GetByteCount(Framing);
        int contextBudget = MaxTotalBytes - framingBytes;

        string context = await ContextBuilder.BuildAsync(
            store, workspaceId, workspaceName,
            scope: "current", gitInfo: gitInfo, ct: ct, maxBytes: contextBudget);

        return Framing + context;
    }

    /// <summary>Returns the lowercase hex SHA-256 of <paramref name="blob"/> for hydration_log deduplication.</summary>
    public static string ComputeHash(string blob) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blob))).ToLowerInvariant();
}

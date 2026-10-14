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
    /// Single line: tells the agent not to go searching for prior work.
    /// </summary>
    public const string EmptyWorkspaceMessage =
        "ContextOS: no stored memory for this workspace. Do not search for prior work; tell the user nothing is recorded.";

    private const string Framing = "ContextOS workspace context (auto-loaded):\n" + AnswerFromContextRule + "\n\n";

    /// <summary>
    /// Always-loaded rule telling the agent to answer prior-work questions from this blob.
    /// Lives here, not in SKILL.md, because skill bodies only load on demand.
    /// </summary>
    public const string AnswerFromContextRule =
        "Use this context to answer questions about prior work, decisions or todos. " +
        "Do not run git, list files, or read session history to answer them. " +
        "Branch and recent commits are already listed here.";

    private const int MaxTotalBytes = 1000;

    /// <summary>
    /// Builds the instructions string for the MCP initialize response.
    /// Returns <see cref="EmptyWorkspaceMessage"/> when there are no memories and no git info.
    /// The returned string is always at or under 1000 bytes in UTF-8.
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
            scope: "current", gitInfo: gitInfo, ct: ct, maxBytes: contextBudget, maxSkills: 3);

        return Framing + context;
    }

    /// <summary>Returns the lowercase hex SHA-256 of <paramref name="blob"/> for hydration_log deduplication.</summary>
    public static string ComputeHash(string blob) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blob))).ToLowerInvariant();
}

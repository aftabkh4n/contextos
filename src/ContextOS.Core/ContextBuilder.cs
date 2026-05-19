using System.Text;

namespace ContextOS.Core;

/// <summary>
/// Assembles the markdown context blob for a workspace. Extracted from the MCP
/// tool layer so it can be tested without the ModelContextProtocol dependency.
/// </summary>
public static class ContextBuilder
{
    private const int MaxBytes = 2048;
    private const string TruncationNote = "\n(truncated to fit 2 KB budget)";

    /// <summary>
    /// Builds the context markdown for the given workspace and scope.
    /// </summary>
    /// <param name="store">Memory store to query.</param>
    /// <param name="workspaceId">Workspace identifier.</param>
    /// <param name="workspaceName">Display name (e.g. the repo directory name).</param>
    /// <param name="scope">One of: current, week, all. Case-insensitive.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Markdown string, always at or under 2 KB.</returns>
    /// <exception cref="ArgumentException">Thrown for an unrecognised scope value.</exception>
    public static async Task<string> BuildAsync(
        IMemoryStore store,
        string workspaceId,
        string workspaceName,
        string scope = "current",
        CancellationToken ct = default)
    {
        string normalScope = scope.ToLowerInvariant();
        if (normalScope is not ("current" or "week" or "all"))
            throw new ArgumentException($"scope must be current, week, or all. Got: {scope}");

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        IReadOnlyList<Memory> all = await store.ListMemoriesAsync(
            workspaceId, new MemoryFilter(Limit: 1000), ct);

        return normalScope switch
        {
            "week" => BuildWeek(workspaceName, all, nowMs),
            "all"  => BuildAll(workspaceName, all, nowMs),
            _      => BuildCurrent(workspaceName, all, nowMs),
        };
    }

    // -------------------------------------------------------------------------
    // Scope builders
    // -------------------------------------------------------------------------

    private static string BuildCurrent(string workspaceName, IReadOnlyList<Memory> all, long nowMs)
    {
        List<Memory> activeTasks = all
            .Where(m => m.Type == MemoryTypes.Todo || HasTag(m.Tags, "active"))
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .ToList();

        List<Memory> decisions = all
            .Where(m => m.Type == MemoryTypes.Decision)
            .OrderByDescending(m => RecencyScore(m, nowMs))
            .Take(3)
            .ToList();

        return TruncateCurrent(workspaceName, activeTasks, decisions, nowMs);
    }

    private static string BuildWeek(string workspaceName, IReadOnlyList<Memory> all, long nowMs)
    {
        long weekAgoMs = nowMs - (7L * 24 * 60 * 60 * 1000);
        List<Memory> items = all
            .Where(m => m.CreatedAt >= weekAgoMs)
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .ToList();

        return TruncateList(workspaceName, "Memories from the last 7 days", items,
            m => $"- [{m.Type}] {m.Content} (created {FormatAge(m.CreatedAt, nowMs)})");
    }

    private static string BuildAll(string workspaceName, IReadOnlyList<Memory> all, long nowMs)
    {
        List<Memory> items = all
            .OrderByDescending(m => RecencyScore(m, nowMs))
            .Take(50)
            .ToList();

        return TruncateList(workspaceName, "All memories (by recency and importance)", items,
            m => $"- [{m.Type}] {m.Content} (importance {m.Importance:F1}, created {FormatAge(m.CreatedAt, nowMs)})");
    }

    // -------------------------------------------------------------------------
    // Markdown formatters
    // -------------------------------------------------------------------------

    private static string FormatCurrent(
        string workspaceName, List<Memory> activeTasks, List<Memory> decisions, long nowMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# ContextOS workspace: {workspaceName}");
        sb.AppendLine();
        sb.AppendLine("## Branch");
        sb.AppendLine("(git probe not yet wired)");
        sb.AppendLine();
        sb.AppendLine("## Active tasks");
        if (activeTasks.Count == 0)
            sb.AppendLine("(none)");
        else
            foreach (Memory m in activeTasks)
            {
                string label = m.Type == MemoryTypes.Todo ? "todo" : "active";
                sb.AppendLine($"- [{label}] {m.Content} (created {FormatAge(m.CreatedAt, nowMs)})");
            }
        sb.AppendLine();
        sb.AppendLine("## Recent decisions");
        if (decisions.Count == 0)
            sb.AppendLine("(none)");
        else
            foreach (Memory m in decisions)
                sb.AppendLine($"- {m.Content} (importance {m.Importance:F1})");
        return sb.ToString();
    }

    private static string FormatList(
        string workspaceName, string sectionTitle, List<Memory> items, Func<Memory, string> lineFormat)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# ContextOS workspace: {workspaceName}");
        sb.AppendLine();
        sb.AppendLine($"## {sectionTitle}");
        if (items.Count == 0)
            sb.AppendLine("(none)");
        else
            foreach (Memory m in items)
                sb.AppendLine(lineFormat(m));
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Truncation
    // -------------------------------------------------------------------------

    private static string TruncateCurrent(
        string workspaceName, List<Memory> activeTasks, List<Memory> decisions, long nowMs)
    {
        int noteBytes = Encoding.UTF8.GetByteCount(TruncationNote);
        var tasks = new List<Memory>(activeTasks);
        var decs = new List<Memory>(decisions);
        bool truncated = false;

        while (true)
        {
            string md = FormatCurrent(workspaceName, tasks, decs, nowMs);
            int mdBytes = Encoding.UTF8.GetByteCount(md);

            if (!truncated && mdBytes <= MaxBytes) return md;
            if (truncated && mdBytes + noteBytes <= MaxBytes) return md + TruncationNote;

            // Drop oldest decision first (last in the list, since sorted by recency DESC),
            // then oldest active task.
            if (decs.Count > 0) { decs.RemoveAt(decs.Count - 1); truncated = true; continue; }
            if (tasks.Count > 0) { tasks.RemoveAt(tasks.Count - 1); truncated = true; continue; }

            return HardTruncate(md);
        }
    }

    private static string TruncateList(
        string workspaceName, string sectionTitle, List<Memory> items, Func<Memory, string> lineFormat)
    {
        int noteBytes = Encoding.UTF8.GetByteCount(TruncationNote);
        var remaining = new List<Memory>(items);
        bool truncated = false;

        while (true)
        {
            string md = FormatList(workspaceName, sectionTitle, remaining, lineFormat);
            int mdBytes = Encoding.UTF8.GetByteCount(md);

            if (!truncated && mdBytes <= MaxBytes) return md;
            if (truncated && mdBytes + noteBytes <= MaxBytes) return md + TruncationNote;

            if (remaining.Count > 0) { remaining.RemoveAt(remaining.Count - 1); truncated = true; continue; }

            return HardTruncate(md);
        }
    }

    private static string HardTruncate(string md)
    {
        int budget = MaxBytes - Encoding.UTF8.GetByteCount(TruncationNote);
        byte[] bytes = Encoding.UTF8.GetBytes(md);
        if (bytes.Length <= budget) return md + TruncationNote;
        // Walk back to the nearest valid UTF-8 character boundary.
        while (budget > 0 && (bytes[budget] & 0xC0) == 0x80) budget--;
        return Encoding.UTF8.GetString(bytes, 0, budget) + TruncationNote;
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    private static double RecencyScore(Memory m, long nowMs)
    {
        double ageDays = Math.Max(0, nowMs - m.CreatedAt) / 86_400_000.0;
        return Math.Exp(-ageDays / 30.0) * (0.5 + m.Importance);
    }

    private static bool HasTag(string? tags, string target) =>
        tags is not null &&
        tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));

    private static string FormatAge(long createdAtMs, long nowMs)
    {
        double hours = Math.Max(0, nowMs - createdAtMs) / 3_600_000.0;
        if (hours < 1) return "just now";
        if (hours < 24) return $"{(int)hours}h ago";
        return $"{(int)(hours / 24)}d ago";
    }
}

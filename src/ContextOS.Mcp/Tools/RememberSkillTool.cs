using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextOS.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class RememberSkillTool(IMemoryStore store, WorkspaceContext ws, ILogger<RememberSkillTool> logger)
{
    [McpServerTool(Name = "remember_skill")]
    [Description("Saves a reusable procedure for this project, such as how to release or run a migration. Skills never decay and appear in the current context summary. Returns JSON {id, message}.")]
    public async Task<string> RememberSkillAsync(
        [Description("Short label, for example Release a new version")] string name,
        [Description("Numbered steps, one per line")] string steps,
        [Description("What success looks like")] string outcome,
        [Description("Comma-separated tags, for example release,ci")] string? tags = null,
        [Description("0.0 to 1.0 (default 0.8). Higher ranks earlier in recall.")] double importance = 0.8,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.");
        if (string.IsNullOrWhiteSpace(steps))
            throw new ArgumentException("steps must not be empty.");
        if (string.IsNullOrWhiteSpace(outcome))
            throw new ArgumentException("outcome must not be empty.");
        if (importance < 0.0 || importance > 1.0)
            throw new ArgumentException($"importance must be between 0.0 and 1.0. Got: {importance}");

        string content = $"Skill: {name}\n\nSteps:\n{steps}\n\nOutcome: {outcome}";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Memory memory = await store.AddMemoryAsync(
                ws.WorkspaceId,
                MemoryTypes.Skill,
                content,
                tags: tags,
                importance: importance,
                decayDays: 0,
                ct: ct);

            // Like remember: log length and a short hash, never the skill text,
            // name, or tags.
            logger.LogInformation(
                "remember_skill: id={Id} length={Length} hash={Hash} elapsed={ElapsedMs}ms",
                memory.Id, content.Length, ContentFingerprint(content), sw.ElapsedMilliseconds);

            return JsonSerializer.Serialize(new { id = memory.Id, message = $"Skill '{name}' remembered." });
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "remember_skill: failed workspace={WorkspaceId} length={Length}",
                ws.WorkspaceId, content.Length);
            throw new InvalidOperationException("Failed to store skill. Check the server logs for details.");
        }
    }

    private static string ContentFingerprint(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..8].ToLowerInvariant();
}

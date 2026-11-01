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
    [Description("Store a reusable skill, pinned and never decays.")]
    public async Task<string> RememberSkillAsync(
        [Description("Short label")] string name,
        [Description("Numbered steps")] string steps,
        [Description("Success outcome")] string outcome,
        [Description("Comma-separated tags")] string? tags = null,
        [Description("0.0-1.0")] double importance = 0.8,
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

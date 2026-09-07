using System.ComponentModel;
using System.Text.Json;
using ContextOS.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class RememberSkillTool(IMemoryStore store, WorkspaceContext ws, ILogger<RememberSkillTool> logger)
{
    [McpServerTool(Name = "remember_skill")]
    [Description("Store a reusable skill (a named procedure with steps and a success outcome).")]
    public async Task<string> RememberSkillAsync(
        [Description("Short label for the skill, e.g. 'deploy to production'.")] string name,
        [Description("Numbered steps as plain text.")] string steps,
        [Description("What success looks like.")] string outcome,
        [Description("Comma-separated tags.")] string? tags = null,
        [Description("Importance from 0.0 to 1.0.")] double importance = 0.8,
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

            logger.LogInformation(
                "remember_skill: id={Id} name={Name} elapsed tags={Tags}",
                memory.Id, name, tags);

            return JsonSerializer.Serialize(new { id = memory.Id, message = $"Skill '{name}' remembered." });
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "remember_skill: failed workspace={WorkspaceId} name={Name}",
                ws.WorkspaceId, name);
            throw new InvalidOperationException("Failed to store skill. Check the server logs for details.");
        }
    }
}

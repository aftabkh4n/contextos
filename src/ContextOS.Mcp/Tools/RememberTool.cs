using System.ComponentModel;
using System.Text.Json;
using ContextOS.Core;
using ModelContextProtocol.Server;

namespace ContextOS.Mcp.Tools;

[McpServerToolType]
public sealed class RememberTool(IMemoryStore store, WorkspaceContext ws)
{
    private static readonly HashSet<string> ValidTypes =
        [MemoryTypes.Note, MemoryTypes.Decision, MemoryTypes.Gotcha, MemoryTypes.Todo];

    [McpServerTool(Name = "remember")]
    [Description("Store a memory for later recall.")]
    public async Task<string> RememberAsync(
        [Description("The content to store.")] string content,
        [Description("Memory type: note, decision, gotcha, or todo.")] string type = "note",
        [Description("Comma-separated tags.")] string? tags = null,
        [Description("Importance from 0.0 to 1.0.")] double importance = 0.5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("content must not be empty.");
        if (!ValidTypes.Contains(type))
            throw new ArgumentException($"type must be one of: note, decision, gotcha, todo. Got: {type}");
        if (importance < 0.0 || importance > 1.0)
            throw new ArgumentException($"importance must be between 0.0 and 1.0. Got: {importance}");

        Memory memory = await store.AddMemoryAsync(
            ws.WorkspaceId, type, content, tags: tags, importance: importance, ct: ct);

        string preview = content.Length <= 60 ? content : content[..60] + "...";
        return JsonSerializer.Serialize(new { id = memory.Id, message = $"Remembered: {preview}" });
    }
}

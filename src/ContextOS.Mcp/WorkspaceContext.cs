using ContextOS.Core;

namespace ContextOS.Mcp;

/// <summary>Immutable workspace identity resolved at startup and shared via DI.</summary>
public record WorkspaceContext(string WorkspaceId, string RootPath, GitInfo? GitInfo = null);

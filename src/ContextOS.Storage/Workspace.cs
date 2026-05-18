namespace ContextOS.Storage;

/// <summary>A registered workspace (one per repository root).</summary>
public record Workspace(
    string Id,
    string RootPath,
    string Name,
    string? RepoUrl,
    long CreatedAt
);

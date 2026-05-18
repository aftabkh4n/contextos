namespace ContextOS.Core;

/// <summary>A stored engineering memory entry.</summary>
public record Memory(
    string Id,
    string WorkspaceId,
    string Type,
    string Content,
    string? Source,
    string? Tags,
    double Importance,
    long CreatedAt,
    long? ArchivedAt
);

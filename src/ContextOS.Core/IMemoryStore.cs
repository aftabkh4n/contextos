namespace ContextOS.Core;

/// <summary>Persistence contract for memories and workspaces.</summary>
public interface IMemoryStore
{
    /// <summary>Inserts a new memory and returns it with its generated ID and timestamp.</summary>
    Task<Memory> AddMemoryAsync(
        string workspaceId,
        string type,
        string content,
        string? source = null,
        string? tags = null,
        double importance = 0.5,
        CancellationToken ct = default);

    /// <summary>Returns the memory with <paramref name="id"/>, or null if not found.</summary>
    Task<Memory?> GetMemoryByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Lists memories for a workspace, newest first, with optional filters.</summary>
    Task<IReadOnlyList<Memory>> ListMemoriesAsync(
        string workspaceId,
        MemoryFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>Deletes the memory with <paramref name="id"/>. Returns true if a row was removed.</summary>
    Task<bool> DeleteMemoryAsync(string id, CancellationToken ct = default);

    /// <summary>Inserts or updates the workspace record.</summary>
    Task UpsertWorkspaceAsync(Workspace workspace, CancellationToken ct = default);

    /// <summary>Returns the workspace with <paramref name="id"/>, or null if not found.</summary>
    Task<Workspace?> GetWorkspaceAsync(string id, CancellationToken ct = default);

    /// <summary>Records a hydration event. Overwrites any existing row for the same session.</summary>
    Task LogHydrationAsync(
        string workspaceId,
        string sessionId,
        string contextHash,
        CancellationToken ct = default);
}

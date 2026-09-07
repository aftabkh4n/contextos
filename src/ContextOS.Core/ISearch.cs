namespace ContextOS.Core;

/// <summary>Contract for hybrid memory retrieval.</summary>
public interface ISearch
{
    /// <summary>Searches memories within a single workspace.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string workspaceId,
        string query,
        int k = 5,
        IReadOnlyCollection<string>? types = null,
        CancellationToken ct = default);

    /// <summary>Searches memories across all known workspaces in CONTEXTOS_HOME.</summary>
    Task<SearchResult[]> SearchGlobalAsync(
        string query,
        int k = 10,
        CancellationToken ct = default);
}

/// <summary>A single retrieval result with its final reranked score.</summary>
public record SearchResult(
    string Id,
    string Type,
    string Content,
    IReadOnlyList<string> Tags,
    long CreatedAt,
    double Score,
    string? WorkspaceName = null);

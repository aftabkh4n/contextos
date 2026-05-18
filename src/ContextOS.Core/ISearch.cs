namespace ContextOS.Core;

/// <summary>Contract for hybrid memory retrieval within a workspace.</summary>
public interface ISearch
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string workspaceId,
        string query,
        int k = 5,
        IReadOnlyCollection<string>? types = null,
        CancellationToken ct = default);
}

/// <summary>A single retrieval result with its final reranked score.</summary>
public record SearchResult(
    string Id,
    string Type,
    string Content,
    IReadOnlyList<string> Tags,
    long CreatedAt,
    double Score);

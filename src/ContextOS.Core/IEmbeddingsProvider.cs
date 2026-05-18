namespace ContextOS.Core;

/// <summary>Contract for computing text embeddings from strings.</summary>
public interface IEmbeddingsProvider
{
    string Name { get; }
    int Dimension { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

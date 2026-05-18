using ContextOS.Core;

namespace ContextOS.Mcp;

/// <summary>
/// Zero-vector fallback used when the ONNX model is absent.
/// FTS5 keyword search still works; vector ranking is a no-op.
/// </summary>
internal sealed class NullEmbeddingsProvider : IEmbeddingsProvider
{
    public string Name => "null";
    public int Dimension => 384;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
        Task.FromResult(new float[384]);

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
        Task.FromResult(texts.Select(_ => new float[384]).ToArray());
}

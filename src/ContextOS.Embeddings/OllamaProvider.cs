using ContextOS.Core;

namespace ContextOS.Embeddings;

/// <summary>
/// Calls a local Ollama instance for embeddings.
/// Not implemented in v1 — configure via <c>~/.contextos/config.json</c>.
/// </summary>
public sealed class OllamaProvider : IEmbeddingsProvider
{
    private readonly string _url;
    private readonly string _model;

    public string Name => "ollama";
    public int Dimension => 0; // determined at runtime from first response

    public OllamaProvider(string url, string model)
    {
        _url = url;
        _model = model;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        throw new NotImplementedException(
            $"OllamaProvider is not yet implemented (url={_url}, model={_model}).");

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new NotImplementedException("OllamaProvider is not yet implemented.");
}

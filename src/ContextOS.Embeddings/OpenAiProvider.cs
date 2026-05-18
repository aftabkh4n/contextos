using ContextOS.Core;

namespace ContextOS.Embeddings;

/// <summary>
/// Calls the OpenAI embeddings API.
/// Not implemented in v1 — configure via <c>~/.contextos/config.json</c>.
/// </summary>
public sealed class OpenAiProvider : IEmbeddingsProvider
{
    private readonly string _model;
    private readonly string _apiKey;

    public string Name => "openai";
    public int Dimension => 1536; // text-embedding-3-small default

    public OpenAiProvider(string model, string apiKey)
    {
        _model = model;
        _apiKey = apiKey;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        throw new NotImplementedException(
            $"OpenAiProvider is not yet implemented (model={_model}).");

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new NotImplementedException("OpenAiProvider is not yet implemented.");
}

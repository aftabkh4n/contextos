using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ContextOS.Core;

namespace ContextOS.Embeddings;

/// <summary>
/// Calls a local Ollama instance for embeddings via POST /api/embeddings.
/// L2-normalizes the output because Ollama does not always do so.
/// </summary>
public sealed class OllamaProvider : IEmbeddingsProvider, IDisposable
{
    // nomic-embed-text default. Changing the model requires reindexing the workspace DB.
    private const int DefaultDimension = 768;

    private readonly string _baseUrl;
    private readonly string _model;
    private readonly HttpClient _http;

    public string Name => "ollama";
    public int Dimension => DefaultDimension;

    /// <param name="url">Base URL of the Ollama server, e.g. http://localhost:11434.</param>
    /// <param name="model">Model name, e.g. nomic-embed-text.</param>
    /// <param name="http">HttpClient instance. The provider takes ownership and disposes it.</param>
    public OllamaProvider(string url, string model, HttpClient http)
    {
        _baseUrl = url.TrimEnd('/');
        _model = model;
        _http = http;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var body = new { model = _model, prompt = text };
        string endpoint = $"{_baseUrl}/api/embeddings";

        HttpResponseMessage response;
        try
        {
            using var content = JsonContent.Create(body);
            response = await _http.PostAsync(endpoint, content, ct);
        }
        catch (HttpRequestException ex) when (IsConnectionRefused(ex))
        {
            throw new InvalidOperationException(
                $"Ollama not reachable at {_baseUrl}. " +
                "Start it with `ollama serve` or check the configured URL in ~/.contextos/config.json.",
                ex);
        }

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("error", out JsonElement errEl))
            throw new InvalidOperationException($"Ollama returned error: {errEl.GetString()}");

        float[] raw = doc.RootElement
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(e => e.GetSingle())
            .ToArray();

        return VectorMath.L2Normalize(raw);
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct);
        return results;
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    private static bool IsConnectionRefused(HttpRequestException ex) =>
        ex.InnerException is SocketException se &&
        se.SocketErrorCode == SocketError.ConnectionRefused;
}

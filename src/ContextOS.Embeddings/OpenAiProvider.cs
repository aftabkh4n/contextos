using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContextOS.Core;

namespace ContextOS.Embeddings;

/// <summary>
/// Calls the OpenAI embeddings API (POST /v1/embeddings).
/// Batches up to 100 inputs per request. Throws at construction time for unknown models.
/// </summary>
public sealed class OpenAiProvider : IEmbeddingsProvider, IDisposable
{
    /// <summary>
    /// Known output dimensions keyed by model name. Add new entries here if needed.
    /// Changing the model requires reindexing the workspace DB.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> KnownDimensions =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["text-embedding-3-small"] = 1536,
            ["text-embedding-3-large"] = 3072,
            ["text-embedding-ada-002"] = 1536,
        };

    private const string ApiUrl = "https://api.openai.com/v1/embeddings";
    private const int BatchSize = 100;

    private readonly string _model;
    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly int _dimension;

    public string Name => "openai";
    public int Dimension => _dimension;

    /// <param name="model">OpenAI model name. Must be in <see cref="KnownDimensions"/>.</param>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="http">HttpClient instance. The provider takes ownership and disposes it.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> is not recognised.</exception>
    public OpenAiProvider(string model, string apiKey, HttpClient http)
    {
        if (!KnownDimensions.TryGetValue(model, out int dim))
            throw new ArgumentException(
                $"ContextOS doesn't know the dimension of '{model}'. " +
                $"Add it to KnownDimensions in OpenAiProvider.cs or pick a supported model " +
                $"({string.Join(", ", KnownDimensions.Keys)}).");

        _model = model;
        _apiKey = apiKey;
        _http = http;
        _dimension = dim;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        float[][] batch = await EmbedBatchInternalAsync([text], ct);
        return batch[0];
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var all = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i += BatchSize)
        {
            int count = Math.Min(BatchSize, texts.Count - i);
            IReadOnlyList<string> chunk = texts.Skip(i).Take(count).ToList();
            float[][] chunkResult = await EmbedBatchInternalAsync(chunk, ct);
            for (int j = 0; j < count; j++)
                all[i + j] = chunkResult[j];
        }
        return all;
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    private async Task<float[][]> EmbedBatchInternalAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var body = new { model = _model, input = texts };
        using var content = JsonContent.Create(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response = await _http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException(
                "OpenAI API key is invalid or missing. Check openAiApiKey in ~/.contextos/config.json " +
                "or set the OPENAI_API_KEY environment variable.");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("OpenAI rate limit hit. Wait a moment and try again.");

        if ((int)response.StatusCode >= 500)
            throw new InvalidOperationException(
                $"OpenAI returned server error {(int)response.StatusCode}. Try again later.");

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement data = doc.RootElement.GetProperty("data");
        var results = new float[texts.Count][];

        foreach (JsonElement item in data.EnumerateArray())
        {
            int idx = item.GetProperty("index").GetInt32();
            results[idx] = item.GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();
        }

        return results;
    }
}

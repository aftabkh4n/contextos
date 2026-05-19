using System.Net;
using ContextOS.Embeddings;

namespace ContextOS.Tests;

public sealed class OpenAiProviderTests
{
    private const string Model  = "text-embedding-3-small";
    private const string ApiKey = "sk-test-key";

    private static OpenAiProvider Provider(FakeHttpMessageHandler h) =>
        new(Model, ApiKey, new HttpClient(h));

    private static string OpenAiResponse(params (int index, float[] embedding)[] items)
    {
        string data = string.Join(",", items.Select(item =>
        {
            string emb = string.Join(",", item.embedding);
            // Use regular $ string so embedded braces are explicit.
            return $"{{\"object\":\"embedding\",\"index\":{item.index},\"embedding\":[{emb}]}}";
        }));
        return $"{{\"object\":\"list\",\"data\":[{data}],\"model\":\"text-embedding-3-small\",\"usage\":{{\"prompt_tokens\":8,\"total_tokens\":8}}}}";
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmbedAsync_HappyPath_ReturnsVector()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.OkJson(OpenAiResponse((0, [1f, 0f, 0f]))));

        using OpenAiProvider provider = Provider(handler);
        float[] result = await provider.EmbedAsync("hello");

        Assert.Equal(3, result.Length);
        Assert.Equal(1f, result[0]);
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsResultsInInputOrder()
    {
        // OpenAI may return results in any index order; provider must reorder.
        string json = OpenAiResponse(
            (2, [0f, 0f, 1f]),
            (0, [1f, 0f, 0f]),
            (1, [0f, 1f, 0f]));

        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.OkJson(json));

        using OpenAiProvider provider = Provider(handler);
        float[][] results = await provider.EmbedBatchAsync(["a", "b", "c"]);

        Assert.Equal(3, results.Length);
        Assert.Equal(1f, results[0][0]); // index 0 → [1,0,0]
        Assert.Equal(1f, results[1][1]); // index 1 → [0,1,0]
        Assert.Equal(1f, results[2][2]); // index 2 → [0,0,1]
    }

    [Fact]
    public async Task EmbedBatchAsync_LargeInput_SplitsInto100Chunks()
    {
        int callCount = 0;
        var handler = new FakeHttpMessageHandler(async req =>
        {
            callCount++;
            string body = await req.Content!.ReadAsStringAsync();
            // 150 inputs → first call gets 100, second gets 50
            int inputCount = body.Split("\"input\"").Length - 1; // rough count
            var embeddings = Enumerable.Range(0, callCount == 1 ? 100 : 50)
                .Select(i => (i, new float[] { (float)i }))
                .ToArray();
            return FakeHttpMessageHandler.OkJson(OpenAiResponse(embeddings));
        });

        using OpenAiProvider provider = Provider(handler);
        string[] texts = Enumerable.Range(0, 150).Select(i => $"text {i}").ToArray();
        float[][] results = await provider.EmbedBatchAsync(texts);

        Assert.Equal(2, callCount);
        Assert.Equal(150, results.Length);
    }

    // -------------------------------------------------------------------------
    // Error handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmbedAsync_Unauthorized_ThrowsApiKeyError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using OpenAiProvider provider = Provider(handler);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync("test"));

        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbedAsync_RateLimited_ThrowsFriendlyError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        using OpenAiProvider provider = Provider(handler);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync("test"));

        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbedAsync_ServerError_ThrowsServerErrorMessage()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using OpenAiProvider provider = Provider(handler);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync("test"));

        Assert.Contains("500", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Factory-time validation
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_UnknownModel_ThrowsAtCreationTime()
    {
        using var http = new HttpClient();
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new OpenAiProvider("text-embedding-unknown-v99", ApiKey, http));

        Assert.Contains("KnownDimensions", ex.Message);
    }

    [Fact]
    public void Constructor_KnownModel_SetsCorrectDimension()
    {
        using var http = new HttpClient();
        using var small = new OpenAiProvider("text-embedding-3-small", ApiKey, http);
        Assert.Equal(1536, small.Dimension);
    }

    // -------------------------------------------------------------------------
    // HTTP request shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmbedAsync_SendsBearerToken()
    {
        string? authHeader = null;
        var handler = new FakeHttpMessageHandler(async req =>
        {
            authHeader = req.Headers.Authorization?.ToString();
            return FakeHttpMessageHandler.OkJson(OpenAiResponse((0, [1f])));
        });

        using OpenAiProvider provider = Provider(handler);
        await provider.EmbedAsync("test");

        Assert.Equal($"Bearer {ApiKey}", authHeader);
    }
}
